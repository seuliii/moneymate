using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Transactions;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(TransactionService service, KoreanClock calendar) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public TransactionPage Records { get; private set; } = null!;
    public IReadOnlyList<CategorySummary> Categories { get; private set; } = [];
    public string CurrentMonth => calendar.CurrentMonth;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await service.ListAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, new()
        { Month = Month, Type = Type, CategoryId = CategoryId, Page = PageNumber }, cancellationToken);
        if (result.Status != 200) return BadRequest(result.Message ?? "조회 조건이 올바르지 않습니다.");
        Records = result.Value!;
        PageNumber = Records.Page;
        Month = Records.Month;
        Categories = await service.CategoriesAsync(null, cancellationToken);
        return Page();
    }
}
