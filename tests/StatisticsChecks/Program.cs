using MoneyMate.Contracts;
using MoneyMate.Services;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
}
static PeriodAggregate Aggregate(DateOnly start, DateOnly end, params Entry[] entries)
{
    var records = entries.Where(x => x.Date >= start && x.Date <= end).ToArray();
    var expenses = records.Where(x => !x.Income).ToArray();
    var max = expenses.Length == 0 ? 0 : expenses.Max(x => x.Amount);
    return new(records.Length, records.Where(x => x.Income).Sum(x => x.Amount), expenses.Sum(x => x.Amount),
        expenses.GroupBy(x => x.Category).Select(x => new CategoryAmount(x.Key, "Category " + x.Key, x.Sum(y => y.Amount))).ToArray(),
        expenses.Where(x => x.Amount == max).Select(x => new LargestExpense(Guid.NewGuid(), "Expense", x.Date, x.Amount)).ToArray());
}
var today = new DateOnly(2026, 10, 3);
Entry[] fixture = [
    new(new(2026,9,1),true,101,3500000), new(new(2026,9,2),false,1,12000),
    new(new(2026,9,3),false,2,5500), new(new(2026,9,5),false,4,120000), new(new(2026,9,8),false,8,17000),
    new(new(2026,8,2),false,1,12000), new(new(2026,8,3),false,2,1000),
    new(new(2026,8,5),false,4,90000), new(new(2026,8,8),false,8,17000),
    new(new(2026,10,1),false,1,999999) // excluded from September
];
var month = new DateOnly(2026,9,1);
var period = StatisticsCalculator.Period(month,today);
var prior = StatisticsCalculator.PreviousPeriod(month,today)!;
var current = Aggregate(period.Start,period.End,fixture);
var previous = Aggregate(prior.Start,prior.End,fixture);
var report = StatisticsCalculator.Calculate(month,today,9,current,previous);
Check(report.Income == 3500000 && report.Expense == 154500 && report.Net == 3345500, "Specification sums/month boundary");
Check(report.RecordCount == 5 && report.DailyAverageExpense == 5150 && report.DataVersion == 9, "Calendar average and snapshot metadata");
Check(report.TopCategories.Single().Id == 4 && report.LargestExpenses.Single().Amount == 120000, "Category and largest expense");
var shopping = report.Categories.Single(x => x.Id == 4);
Check(shopping.SharePercent == 77.67m && shopping.ChangeAmount == 30000 && shopping.ChangePercent == 33.33m, "Category ratios");
Check(report.Comparison.ChangeAmount == 34500 && report.Comparison.ChangePercent == 28.75m, "Previous month comparison");
Check(period.Days == 30 && prior.Days == 31 && report.Comparison.DifferentDayCounts, "Full month calendar days");

var march = new DateOnly(2024,3,1);
var marchToday = new DateOnly(2024,3,31);
Check(StatisticsCalculator.PreviousPeriod(march,marchToday)!.End == new DateOnly(2024,2,29), "Leap-year same-date clipping");
Check(StatisticsCalculator.PreviousPeriod(new(2025,3,1),new(2025,3,31))!.Days == 28, "Non-leap February");
Check(StatisticsCalculator.PreviousPeriod(new(2026,1,1),new(2026,1,3))!.End == new DateOnly(2025,12,3), "Year rollover");
Check(StatisticsCalculator.Period(new(2026,10,1),today).Days == 3 && StatisticsCalculator.PreviousPeriod(new(2026,10,1),today)!.Days == 3, "Current month elapsed days");
Check(!StatisticsCalculator.TryMonth("2026-11",today,out _) && !StatisticsCalculator.TryMonth("2026-1",today,out _) && !StatisticsCalculator.TryMonth("invalid",today,out _), "Future/malformed months");
Check(StatisticsCalculator.PreviousPeriod(DateOnly.MinValue,today) is null, "Minimum supported month");

var empty = new PeriodAggregate(0,0,0,[],[]);
var noPrevious = StatisticsCalculator.Calculate(month,today,0,current,empty);
Check(noPrevious.Comparison.Status == "no_previous_records" && noPrevious.Comparison.ChangeAmount is null && noPrevious.Comparison.ChangePercent is null, "Missing previous records");
var incomeOnly = new PeriodAggregate(1,10000,0,[],[]);
var zeroPrevious = StatisticsCalculator.Calculate(month,today,0,current,incomeOnly);
Check(zeroPrevious.Comparison.Status == "zero_previous_expense" && zeroPrevious.Comparison.ChangeAmount == 154500 && zeroPrevious.Comparison.ChangePercent is null, "Previous income-only month");
var noCurrent = StatisticsCalculator.Calculate(month,today,0,empty,previous);
Check(noCurrent.RecordCount == 0 && noCurrent.Comparison.ChangePercent == -100 && noCurrent.TopCategories.Count == 0 && noCurrent.LargestExpenses.Count == 0, "Empty current period");
Check(noCurrent.Categories.Count == 4 && noCurrent.Categories.All(x => x.Amount == 0 && x.SharePercent is null && x.ChangeAmount == -x.PreviousAmount), "Previous-only categories remain in comparison");
var tied = new PeriodAggregate(2,0,200,[new(1,"Food",100),new(2,"Cafe",100)],
    [new(Guid.NewGuid(),"A",month,100),new(Guid.NewGuid(),"B",month,100)]);
var tieReport = StatisticsCalculator.Calculate(month,today,0,tied,empty);
Check(tieReport.TopCategories.Count == 2 && tieReport.LargestExpenses.Count == 2, "All tied maxima");
var small = StatisticsCalculator.Calculate(new(2026,2,1),today,0,new(1,0,7,[new(1,"A",7)],[]),empty);
Check(small.DailyAverageExpense == 0.25m, "Average uses every calendar day");
var rounding = StatisticsCalculator.Calculate(new(2026,9,1),today,0,new(2,0,320,[new(1,"A",1),new(2,"B",319)],[]),empty);
Check(rounding.Categories.Single(x => x.Id==1).SharePercent == 0.31m, "Decimal ratio precision");
var halfway = StatisticsCalculator.Calculate(month,today,0,new(2,0,20000,[new(1,"A",1),new(2,"B",19999)],[]),empty);
Check(halfway.Categories.Single(x => x.Id==1).SharePercent == 0.01m, "Halfway rounding away from zero");
var clock = new KoreanClock(new FixedClock(new(2026,9,30,15,0,0,TimeSpan.Zero)));
Check(clock.Today == new DateOnly(2026,10,1), "Korean date crosses UTC month boundary");
Console.WriteLine("PASS: specification totals/ratios, month boundaries, elapsed days, leap years, zero/missing records, ties, previous-only categories, Korean midnight.");

sealed record Entry(DateOnly Date, bool Income, int Category, long Amount);
sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
