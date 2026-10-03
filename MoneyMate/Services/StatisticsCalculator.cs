using System.Globalization;
using MoneyMate.Contracts;

namespace MoneyMate.Services;

public static class StatisticsCalculator
{
    public static bool TryMonth(string? input, DateOnly today, out DateOnly month) =>
        DateOnly.TryParseExact((input ?? today.ToString("yyyy-MM", CultureInfo.InvariantCulture)) + "-01",
            "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out month) && month <= today;

    public static StatisticsPeriod Period(DateOnly month, DateOnly today)
    {
        var end = month.Year == today.Year && month.Month == today.Month
            ? today : month.AddMonths(1).AddDays(-1);
        return new(month, end, end.DayNumber - month.DayNumber + 1);
    }

    public static StatisticsPeriod? PreviousPeriod(DateOnly month, DateOnly today)
    {
        if (month == DateOnly.MinValue) return null;
        var start = month.AddMonths(-1);
        var days = DateTime.DaysInMonth(start.Year, start.Month);
        var endDay = month.Year == today.Year && month.Month == today.Month ? Math.Min(today.Day, days) : days;
        return new(start, start.AddDays(endDay - 1), endDay);
    }

    public static MonthlyStatistics Calculate(DateOnly month, DateOnly today, long dataVersion,
        PeriodAggregate current, PeriodAggregate previous)
    {
        var period = Period(month, today);
        var priorPeriod = PreviousPeriod(month, today);
        var comparable = priorPeriod is not null && previous.RecordCount > 0;
        var status = !comparable ? "no_previous_records" : previous.Expense == 0 ? "zero_previous_expense" : "comparable";
        var categoryAmounts = current.Categories.Concat(comparable
            ? previous.Categories.Where(x => current.Categories.All(y => y.Id != x.Id)).Select(x => x with { Amount = 0 }) : []);
        var categories = categoryAmounts.Select(x =>
        {
            var prior = previous.Categories.FirstOrDefault(y => y.Id == x.Id)?.Amount ?? 0;
            return new CategoryStatistics(x.Id, x.Name, x.Amount, current.Expense == 0 ? null : Ratio(x.Amount, current.Expense),
                comparable ? prior : null, comparable ? x.Amount - prior : null,
                comparable && prior > 0 ? Ratio(x.Amount - prior, prior) : null);
        }).OrderByDescending(x => x.Amount).ThenBy(x => x.Id).ToArray();
        var maxCategory = current.Categories.Count == 0 ? 0 : current.Categories.Max(x => x.Amount);
        var comparison = new ExpenseComparison(status, priorPeriod, previous.RecordCount,
            priorPeriod is null ? null : previous.Expense, comparable ? current.Expense - previous.Expense : null,
            comparable && previous.Expense > 0 ? Ratio(current.Expense - previous.Expense, previous.Expense) : null,
            priorPeriod is not null && period.Days != priorPeriod.Days);
        return new(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), today,
            month.Year == today.Year && month.Month == today.Month, period, dataVersion, current.RecordCount,
            current.Income, current.Expense, current.Income - current.Expense,
            Round(current.Expense / (decimal)period.Days), categories,
            current.Expense == 0 ? [] : current.Categories.Where(x => x.Amount == maxCategory).OrderBy(x => x.Id).ToArray(),
            current.Expense == 0 ? [] : current.LargestExpenses, comparison);
    }

    private static decimal Ratio(long numerator, long denominator) => Round(numerator / (decimal)denominator * 100);
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
