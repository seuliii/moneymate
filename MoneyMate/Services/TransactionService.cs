using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MoneyMate.Contracts;
using MoneyMate.Data;
using MoneyMate.Models;

namespace MoneyMate.Services;

public sealed class TransactionService(MoneyMateDbContext db, KoreanClock calendar, TimeProvider clock)
{
    public async Task<IReadOnlyList<CategorySummary>> CategoriesAsync(string? type, CancellationToken cancellationToken)
    {
        var query = db.Categories.AsNoTracking();
        if (type is not null) query = query.Where(x => x.Type == (type == "income" ? TransactionType.Income : TransactionType.Expense));
        return await query.OrderBy(x => x.SortOrder).Select(x => new CategorySummary(
            x.Id, x.Code, x.Name, x.Type == TransactionType.Income ? "income" : "expense")).ToListAsync(cancellationToken);
    }

    public async Task<LedgerResult<TransactionPage>> ListAsync(string userId, TransactionQuery input, CancellationToken cancellationToken)
    {
        var validation = Validate(input);
        var month = input.Month ?? calendar.CurrentMonth;
        if (validation is not null || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) || start > calendar.Today)
            return new(null, 400, "invalid_query", validation ?? "조회 월은 YYYY-MM 형식으로 현재 월까지 선택해주세요.");
        var end = start.AddMonths(1);
        var query = db.Transactions.AsNoTracking().Where(x => x.UserId == userId && x.TransactionDate >= start && x.TransactionDate < end);
        if (input.Type is not null) query = query.Where(x => x.Type == (input.Type == "income" ? TransactionType.Income : TransactionType.Expense));
        if (input.CategoryId is not null) query = query.Where(x => x.CategoryId == input.CategoryId);
        // A short repeatable-read snapshot keeps pagination count and rows consistent during writes.
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)input.PageSize));
        var page = Math.Min(input.Page, totalPages);
        var items = await Summaries(query.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * input.PageSize).Take(input.PageSize)).ToListAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(new(items, month, page, input.PageSize, count, totalPages));
    }

    public async Task<TransactionSummary?> GetAsync(string userId, Guid id, CancellationToken cancellationToken) =>
        await Summaries(db.Transactions.AsNoTracking().Where(x => x.UserId == userId && x.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<LedgerResult<TransactionSummary>> CreateAsync(string userId, TransactionInput input, CancellationToken cancellationToken) =>
        MutateAsync(userId, input, null, null, false, cancellationToken);
    public Task<LedgerResult<TransactionSummary>> UpdateAsync(string userId, Guid id, UpdateTransactionRequest input, CancellationToken cancellationToken) =>
        MutateAsync(userId, input, id, input.Version, false, cancellationToken);
    public Task<LedgerResult<TransactionSummary>> DeleteAsync(string userId, Guid id, long version, CancellationToken cancellationToken) =>
        MutateAsync(userId, null, id, version, true, cancellationToken);

    private async Task<LedgerResult<TransactionSummary>> MutateAsync(string userId, TransactionInput? input, Guid? id,
        long? expectedVersion, bool delete, CancellationToken cancellationToken)
    {
        if (expectedVersion is < 1) return new(null, 400, "invalid_version", "거래 버전이 올바르지 않습니다.");
        if (input is not null)
        {
            var validation = Validate(input);
            if (validation is not null) return new(null, 400, "invalid_transaction", validation);
            if (input.TransactionDate > calendar.Today)
                return new(null, 400, "future_transaction", "미래 날짜의 거래는 등록할 수 없습니다.");
            var type = input.Type == "income" ? TransactionType.Income : TransactionType.Expense;
            if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId && x.Type == type, cancellationToken))
                return new(null, 400, "invalid_category", "거래 유형에 맞는 카테고리를 선택해주세요.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Parameterized SQL; serialize writes per owner across all web instances.
            // The lock exists only during this short transaction, never while a user edits a form.
            var state = await db.UserLedgerStates.FromSqlInterpolated(
                $"SELECT * FROM \"UserLedgerStates\" WHERE \"UserId\" = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (state is null) return new(null, 401, "account_unavailable", "계정 정보를 확인한 뒤 다시 로그인해주세요.");

            var entity = id is null ? new LedgerTransaction { Id = Guid.CreateVersion7(), UserId = userId, CreatedAt = clock.GetUtcNow() }
                : await db.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
            if (entity is null) return new(null, 404, "transaction_not_found", "거래를 찾을 수 없습니다.");
            if (id is not null && entity.Version != expectedVersion)
                return new(null, 409, "version_conflict", "다른 화면에서 거래가 변경되었습니다. 최신 내용을 불러온 뒤 다시 시도해주세요.");

            if (delete) db.Transactions.Remove(entity);
            else
            {
                entity.Type = input!.Type == "income" ? TransactionType.Income : TransactionType.Expense;
                entity.Amount = input.Amount;
                entity.CategoryId = input.CategoryId;
                entity.TransactionDate = input.TransactionDate!.Value;
                entity.Title = input.Title.Trim();
                entity.Memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
                entity.UpdatedAt = clock.GetUtcNow();
                entity.Version = id is null ? 1 : checked(entity.Version + 1);
                if (id is null) db.Transactions.Add(entity);
            }
            state.DataVersion = checked(state.DataVersion + 1);
            await db.SaveChangesAsync(cancellationToken);
            var summary = delete ? null : await GetAsync(userId, entity.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(summary, delete ? 204 : id is null ? 201 : 200, DataVersion: state.DataVersion);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(null, 409, "version_conflict", "거래가 변경되었습니다. 최신 내용을 불러온 뒤 다시 시도해주세요.");
        }
    }

    private IQueryable<TransactionSummary> Summaries(IQueryable<LedgerTransaction> source) =>
        from item in source
        join category in db.Categories on item.CategoryId equals category.Id
        select new TransactionSummary(item.Id, item.Type == TransactionType.Income ? "income" : "expense", item.Amount,
            item.CategoryId, category.Name, item.TransactionDate, item.Title, item.Memo, item.Version, item.CreatedAt, item.UpdatedAt);

    private static string? Validate(object input)
    {
        var errors = new List<ValidationResult>();
        return Validator.TryValidateObject(input, new ValidationContext(input), errors, true)
            ? null : string.Join(" ", errors.Select(x => x.ErrorMessage));
    }
}
