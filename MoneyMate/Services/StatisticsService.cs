using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using MoneyMate.Contracts;
using MoneyMate.Data;
using MoneyMate.Models;

namespace MoneyMate.Services;

public sealed class StatisticsService(MoneyMateDbContext db, KoreanClock calendar, ILogger<StatisticsService> logger)
{
    public async Task<LedgerResult<MonthlyStatistics>> GetAsync(string owner, string? month, CancellationToken cancellationToken)
    {
        try { return await ReadAsync(owner, month, cancellationToken); }
        catch (Exception exception) when (exception is DbException or TimeoutException
            or InvalidOperationException { InnerException: DbException or TimeoutException })
        {
            logger.LogWarning("Statistics read unavailable ({ErrorType}).", exception.GetType().Name);
            return new(null, 503, "statistics_unavailable", "통계를 불러오지 못했습니다. 잠시 후 다시 시도해주세요.");
        }
    }

    private async Task<LedgerResult<MonthlyStatistics>> ReadAsync(string owner, string? month, CancellationToken cancellationToken)
    {
        // Freeze the Korean date once so midnight cannot change the range midway through a request.
        var today = calendar.Today;
        if (!StatisticsCalculator.TryMonth(month, today, out var start))
            return new(null, 400, "invalid_month", "조회 월은 YYYY-MM 형식으로 현재 월까지 선택해주세요.");
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var version = await db.UserLedgerStates.AsNoTracking().Where(x => x.UserId == owner)
            .Select(x => (long?)x.DataVersion).SingleOrDefaultAsync(cancellationToken);
        if (version is null) return new(null, 401, "account_unavailable", "계정 정보를 확인한 뒤 다시 로그인해주세요.");
        var current = await AggregateAsync(owner, StatisticsCalculator.Period(start, today), cancellationToken);
        var previousPeriod = StatisticsCalculator.PreviousPeriod(start, today);
        var previous = previousPeriod is null ? new PeriodAggregate(0, 0, 0, [], [])
            : await AggregateAsync(owner, previousPeriod, cancellationToken);
        var report = StatisticsCalculator.Calculate(start, today, version.Value, current, previous);
        await snapshot.CommitAsync(cancellationToken);
        return new(report);
    }

    private async Task<PeriodAggregate> AggregateAsync(string owner, StatisticsPeriod period, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking().Where(x => x.UserId == owner && x.TransactionDate >= period.Start && x.TransactionDate <= period.End);
        var totals = await query.GroupBy(x => x.Type).Select(x => new { Type = x.Key, Count = x.Count(), Amount = x.Sum(y => y.Amount) }).ToListAsync(ct);
        var expenses = query.Where(x => x.Type == TransactionType.Expense);
        var categories = await (from item in expenses join category in db.Categories on item.CategoryId equals category.Id
            group item by new { category.Id, category.Name } into grouped
            select new CategoryAmount(grouped.Key.Id, grouped.Key.Name, grouped.Sum(x => x.Amount))).ToListAsync(ct);
        var maximum = await expenses.Select(x => (long?)x.Amount).MaxAsync(ct);
        var largest = maximum is null ? [] : await (from item in expenses
            join category in db.Categories on item.CategoryId equals category.Id
            where item.Amount == maximum
            orderby item.TransactionDate, item.Id
            select new LargestExpense(item.Id, item.Title, item.TransactionDate, item.Amount, category.Code)).ToListAsync(ct);
        return new(totals.Sum(x => x.Count), totals.FirstOrDefault(x => x.Type == TransactionType.Income)?.Amount ?? 0,
            totals.FirstOrDefault(x => x.Type == TransactionType.Expense)?.Amount ?? 0, categories, largest);
    }
}
