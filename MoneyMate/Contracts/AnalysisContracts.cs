using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MoneyMate.Contracts;

public sealed class GenerateReportRequest
{
    [Required, RegularExpression("^[0-9]{4}-[0-9]{2}$")] public string Month { get; set; } = "";
}
public sealed record AnalysisPeriod(DateOnly Start, DateOnly EndInclusive, int Days);
public sealed record AnalysisFact(string Id, decimal? Value, string Unit);
public sealed record AnalysisCategory(string Code, string Name, long Amount, long? PreviousAmount,
    long? DeltaAmount, decimal? ChangeRatePercent, decimal? SharePercent);
public sealed record AnalysisComparison(string Status, string Mode, AnalysisPeriod? PreviousPeriod, bool DifferentDayCounts);
public sealed record AnalysisExpense(DateOnly Date, string CategoryCode, long Amount);
public sealed record AnalysisInput(string ContractVersion, Guid RequestId, string Locale, string Currency, string TimeZone,
    long DataVersion, DateOnly AsOfDate, string Month, bool IsCalendarMonthComplete, AnalysisPeriod Period,
    string RecordingCoverage, int RecordCount, IReadOnlyList<AnalysisFact> Facts, IReadOnlyList<AnalysisCategory> Categories,
    AnalysisComparison Comparison, IReadOnlyList<AnalysisExpense> LargestExpenses, IReadOnlyList<string> Limitations);
public sealed record ReportStatement([property: JsonRequired] string Text, [property: JsonRequired] IReadOnlyList<string> EvidenceIds);
public sealed record ReportContent([property: JsonRequired] ReportStatement Summary,
    [property: JsonRequired] IReadOnlyList<ReportStatement> Highlights,
    [property: JsonRequired] IReadOnlyList<ReportStatement> ChangesToReview,
    [property: JsonRequired] IReadOnlyList<ReportStatement> Suggestions);
public sealed record AnalysisOutput([property: JsonRequired] string ContractVersion, [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] ReportContent Report);
public sealed record SavedReport(Guid Id, string Month, DateTimeOffset GeneratedAt, long DataVersion, DateOnly AsOfDate,
    string Mode, bool NeedsRefresh, IReadOnlyList<string> RefreshReasons, AnalysisInput Input, ReportContent Report)
{
    // UI metadata comes from the stored model key; preserve the public JSON contract.
    [JsonIgnore] public bool IsStub { get; init; }
}
public sealed record ReportResult(SavedReport? Value, int Status, string? Code = null, string? Message = null);
