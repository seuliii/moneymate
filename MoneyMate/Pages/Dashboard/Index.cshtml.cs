using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Dashboard;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(StatisticsService service, KoreanClock calendar) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    public MonthlyStatistics Report { get; private set; } = null!;
    public string? ErrorMessage { get; private set; }
    public string CurrentMonth => calendar.CurrentMonth;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, Month, cancellationToken);
        if (result.Status == 401) return Challenge();
        if (result.Status == 503)
        {
            ErrorMessage = result.Message;
            Response.StatusCode = 503;
            return Page();
        }
        if (result.Status != 200) return BadRequest(result.Message ?? "조회 월이 올바르지 않습니다.");
        Report = result.Value!;
        Month = Report.Month;
        return Page();
    }
    public static string Money(long value) => value.ToString("N0") + "원";
    public static string Change(long value) => (value > 0 ? "+" : "") + Money(value);
}
