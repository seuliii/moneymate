using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoneyMate.Contracts;
using MoneyMate.Data;
using MoneyMate.Models;
using Npgsql;

namespace MoneyMate.Services;

public sealed class ReportService(MoneyMateDbContext db, StatisticsService statistics, IAnalysisClient client,
    AnalysisGuard guard, KoreanClock calendar, TimeProvider clock, IOptions<AnalysisOptions> configured,
    ILogger<ReportService> logger)
{
    public bool IsMock => configured.Value.Mode == "Mock";
    public bool IsStub => !IsMock && configured.Value.ModelKey == "partner-stub-local-v1";
    private string ModelKey => (IsMock ? "mock:" : "http:") + configured.Value.ModelKey;
    public Task<ReportResult> GenerateAsync(string owner, string month, CancellationToken ct) => SafeAsync(() => GenerateCoreAsync(owner, month, ct));
    public Task<ReportResult> LatestAsync(string owner, string month, CancellationToken ct) => SafeAsync(async () =>
    {
        if (!StatisticsCalculator.TryMonth(month, calendar.Today, out var date)) return Failure(400, "invalid_month");
        var entity = await db.AnalysisReports.AsNoTracking().Where(x => x.UserId == owner && x.Month == date)
            .OrderByDescending(x => x.GeneratedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return entity is null ? Failure(404, "report_not_found") : new(await PresentAsync(entity, owner, ct), 200);
    });
    public Task<ReportResult> GetAsync(string owner, Guid id, CancellationToken ct) => SafeAsync(async () =>
    {
        var entity = await db.AnalysisReports.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Id == id, ct);
        return entity is null ? Failure(404, "report_not_found") : new(await PresentAsync(entity, owner, ct), 200);
    });

    private async Task<ReportResult> GenerateCoreAsync(string owner, string month, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var gateKey = owner + ":" + month;
        if (!guard.Enter(gateKey)) return Failure(409, "analysis_in_progress");
        try
        {
            var result = await statistics.GetAsync(owner, month, token);
            if (result.Status != 200) return new(null, result.Status, result.Code, result.Message);
            var stats = result.Value!;
            if (stats.RecordCount == 0) return Failure(400, "no_records");
            var date = DateOnly.ParseExact(stats.Month + "-01", "yyyy-MM-dd");
            var existing = await CacheAsync(owner, date, stats.DataVersion, stats.AsOfDate, token);
            if (existing is not null) return new(await PresentAsync(existing, owner, token), 200);
            var codes = await db.Categories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, token);
            var input = AnalysisContract.Build(stats, codes);
            if (!guard.Charge(owner, stats.AsOfDate, configured.Value.DailyAttempts)) return Failure(429, "analysis_daily_limit");
            var output = await client.AnalyzeAsync(input, token);
            if (!AnalysisContract.Valid(output, input)) return Failure(502, "invalid_analysis_response");
            var entity = new AnalysisReport {
                Id = Guid.CreateVersion7(), UserId = owner, Month = date, DataVersion = input.DataVersion, AsOfDate = input.AsOfDate,
                ContractVersion = AnalysisContract.Version, PromptVersion = AnalysisContract.PromptVersion, ModelKey = ModelKey,
                InputSnapshot = JsonSerializer.Serialize(input, AnalysisContract.Json), Result = JsonSerializer.Serialize(output, AnalysisContract.Json),
                GeneratedAt = clock.GetUtcNow() };
            db.AnalysisReports.Add(entity);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(entity).State = EntityState.Detached;
                var winner = await CacheAsync(owner, date, stats.DataVersion, stats.AsOfDate, token);
                return winner is null ? Failure(409, "analysis_in_progress") : new(await PresentAsync(winner, owner, token), 200);
            }
            return new(await PresentAsync(entity, owner, token), 201);
        }
        catch (AnalysisFailure ex) { return Failure(ex.Status, ex.Code); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failure(504, "analysis_timeout"); }
        finally { guard.Exit(gateKey); }
    }

    private Task<AnalysisReport?> CacheAsync(string owner, DateOnly month, long version, DateOnly day, CancellationToken ct) =>
        db.AnalysisReports.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Month == month && x.DataVersion == version
            && x.AsOfDate == day && x.ContractVersion == AnalysisContract.Version && x.PromptVersion == AnalysisContract.PromptVersion && x.ModelKey == ModelKey, ct);

    private async Task<SavedReport> PresentAsync(AnalysisReport entity, string owner, CancellationToken ct)
    {
        var version = await db.UserLedgerStates.AsNoTracking().Where(x => x.UserId == owner).Select(x => x.DataVersion).SingleAsync(ct);
        var reasons = new List<string>();
        if (entity.DataVersion != version) reasons.Add("거래 기록이 변경되었습니다.");
        if (entity.AsOfDate != calendar.Today) reasons.Add("한국 날짜 기준 분석 시점이 변경되었습니다.");
        if (entity.ContractVersion != AnalysisContract.Version || entity.PromptVersion != AnalysisContract.PromptVersion || entity.ModelKey != ModelKey)
            reasons.Add("분석 방식 또는 설정이 변경되었습니다.");
        return new(entity.Id, entity.Month.ToString("yyyy-MM"), entity.GeneratedAt, entity.DataVersion, entity.AsOfDate,
            entity.ModelKey.StartsWith("mock:", StringComparison.Ordinal) ? "mock" : "http", reasons.Count > 0, reasons,
            JsonSerializer.Deserialize<AnalysisInput>(entity.InputSnapshot, AnalysisContract.Json)!,
            JsonSerializer.Deserialize<AnalysisOutput>(entity.Result, AnalysisContract.Json)!.Report)
            { IsStub = entity.ModelKey == "http:partner-stub-local-v1" };
    }
    private async Task<ReportResult> SafeAsync(Func<Task<ReportResult>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is DbException or TimeoutException or InvalidOperationException { InnerException: DbException or TimeoutException })
        { logger.LogWarning("Report database unavailable ({ErrorType}).", ex.GetType().Name); return Failure(503, "report_unavailable"); }
    }
    private static ReportResult Failure(int status, string code) => new(null, status, code, code switch {
        "no_records" => "분석할 거래 기록이 없습니다.", "report_not_found" => "저장된 리포트가 없습니다.",
        "invalid_month" => "조회 월은 YYYY-MM 형식으로 현재 월까지 선택해주세요.",
        "analysis_in_progress" => "이달 분석이 진행 중입니다. 잠시 후 다시 확인해주세요.",
        "analysis_daily_limit" => "오늘의 분석 시도 한도를 사용했습니다. 기존 리포트를 확인하거나 내일 다시 시도해주세요.",
        "analysis_timeout" => "분석 시간이 초과되었습니다. 다시 시도해주세요.",
        "report_unavailable" => "리포트를 불러오거나 저장하지 못했습니다. 잠시 후 다시 시도해주세요.",
        _ => "분석을 완료하지 못했습니다. 기존 기록은 유지됩니다. 다시 시도해주세요." });
}
