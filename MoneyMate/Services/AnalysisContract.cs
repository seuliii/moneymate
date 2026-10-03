using System.Text.Json;
using System.Text.Json.Serialization;
using MoneyMate.Contracts;

namespace MoneyMate.Services;

public static class AnalysisContract
{
    public const string Version = "1.0";
    public const string PromptVersion = "monthly-v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };

    public static AnalysisInput Build(MonthlyStatistics stats, IReadOnlyDictionary<int, string> codes)
    {
        var facts = new List<AnalysisFact> {
            new("income.total", stats.Income, "KRW"), new("expense.total", stats.Expense, "KRW"),
            new("net.total", stats.Net, "KRW"), new("expense.dailyAverage", stats.DailyAverageExpense, "KRW"),
            new("comparison.previousExpense", stats.Comparison.PreviousExpense, "KRW"),
            new("comparison.delta", stats.Comparison.ChangeAmount, "KRW"), new("comparison.rate", stats.Comparison.ChangePercent, "percent") };
        var categories = stats.Categories.Select(x => new AnalysisCategory(codes[x.Id], x.Name, x.Amount,
            x.PreviousAmount, x.ChangeAmount, x.ChangePercent, x.SharePercent)).ToArray();
        foreach (var item in categories)
        {
            var prefix = "category." + item.Code;
            facts.Add(new(prefix + ".amount", item.Amount, "KRW"));
            facts.Add(new(prefix + ".share", item.SharePercent, "percent"));
            facts.Add(new(prefix + ".delta", item.DeltaAmount, "KRW"));
            facts.Add(new(prefix + ".rate", item.ChangeRatePercent, "percent"));
        }
        var limitations = new List<string> { "기록 완전성은 확인되지 않았습니다.", "월간 수지는 실제 계좌 잔액이나 저축액이 아닙니다.", "장기 추세 분석 데이터는 제공하지 않았습니다." };
        if (stats.Comparison.DifferentDayCounts) limitations.Add("비교 기간의 달력 일수가 다릅니다.");
        if (stats.Comparison.Status == "no_previous_records") limitations.Add("전월 비교 기간에 기록이 없습니다.");
        if (stats.Comparison.Status == "zero_previous_expense") limitations.Add("전월 지출이 0원으로 증감률을 계산하지 않습니다.");
        return new(Version, Guid.CreateVersion7(), "ko-KR", "KRW", "Asia/Seoul", stats.DataVersion, stats.AsOfDate,
            stats.Month, !stats.IsCurrentMonth, Convert(stats.Period), "unknown", stats.RecordCount, facts, categories,
            new(stats.Comparison.Status, stats.IsCurrentMonth ? "same_day" : "full_month",
                stats.Comparison.PreviousPeriod is { } previous ? Convert(previous) : null, stats.Comparison.DifferentDayCounts),
            stats.LargestExpenses.Select(x => new AnalysisExpense(x.TransactionDate, x.CategoryCode, x.Amount)).ToArray(), limitations);
    }
    private static AnalysisPeriod Convert(StatisticsPeriod period) => new(period.Start, period.End, period.Days);

    public static bool Valid(AnalysisOutput? output, AnalysisInput input)
    {
        if (output is null || output.ContractVersion != input.ContractVersion || output.RequestId != input.RequestId || output.Report is null) return false;
        var report = output.Report;
        var ids = input.Facts.Where(x => x.Value is not null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        bool Statement(ReportStatement? item) => item is not null && !string.IsNullOrWhiteSpace(item.Text) && item.Text.Length <= 500
            && item.EvidenceIds is { Count: > 0 and <= 20 } && item.EvidenceIds.All(x => x is not null && ids.Contains(x));
        bool Section(IReadOnlyList<ReportStatement>? items) => items is { Count: <= 3 } && items.All(Statement);
        return Statement(report.Summary) && Section(report.Highlights) && Section(report.ChangesToReview) && Section(report.Suggestions);
    }
}
