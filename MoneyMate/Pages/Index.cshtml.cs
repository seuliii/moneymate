using System.Security.Claims;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(IWebHostEnvironment environment, StatisticsService statistics) : PageModel
{
    public bool IsDevelopment => environment.IsDevelopment();
    public MonthlyStatistics? Report { get; private set; }
    public bool StatisticsUnavailable { get; private set; }
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return;
        try
        {
            var result = await statistics.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, null, cancellationToken);
            Report = result.Value;
            StatisticsUnavailable = result.Status != 200;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            StatisticsUnavailable = true;
        }
    }
}
