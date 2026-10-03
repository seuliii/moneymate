namespace MoneyMate.Contracts;

public sealed record StatisticsPeriod(DateOnly Start, DateOnly End, int Days);
public sealed record CategoryAmount(int Id, string Name, long Amount);
public sealed record LargestExpense(Guid Id, string Title, DateOnly TransactionDate, long Amount, string CategoryCode = "");
public sealed record PeriodAggregate(int RecordCount, long Income, long Expense,
    IReadOnlyList<CategoryAmount> Categories, IReadOnlyList<LargestExpense> LargestExpenses);
public sealed record ExpenseComparison(string Status, StatisticsPeriod? PreviousPeriod, int PreviousRecordCount,
    long? PreviousExpense, long? ChangeAmount, decimal? ChangePercent, bool DifferentDayCounts);
public sealed record CategoryStatistics(int Id, string Name, long Amount, decimal? SharePercent,
    long? PreviousAmount, long? ChangeAmount, decimal? ChangePercent);
public sealed record MonthlyStatistics(string Month, DateOnly AsOfDate, bool IsCurrentMonth,
    StatisticsPeriod Period, long DataVersion, int RecordCount, long Income, long Expense, long Net,
    decimal DailyAverageExpense, IReadOnlyList<CategoryStatistics> Categories,
    IReadOnlyList<CategoryAmount> TopCategories, IReadOnlyList<LargestExpense> LargestExpenses,
    ExpenseComparison Comparison);
