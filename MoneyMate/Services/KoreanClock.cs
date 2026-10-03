namespace MoneyMate.Services;

public sealed class KoreanClock(TimeProvider clock)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);
    public string CurrentMonth => Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
