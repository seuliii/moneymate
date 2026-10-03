using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Transactions;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DeleteModel(TransactionService service) : PageModel
{
    [BindProperty, Range(1, long.MaxValue)] public long Version { get; set; }
    [BindProperty] public bool ConfirmDeletion { get; set; }
    public TransactionSummary Item { get; private set; } = null!;
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(Owner, id, cancellationToken);
        if (item is null) return NotFound();
        Item = item;
        Version = item.Version;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(Owner, id, cancellationToken);
        if (item is null) return NotFound();
        Item = item;
        if (!ConfirmDeletion) ModelState.AddModelError(string.Empty, "삭제할 거래를 확인하고 동의 항목을 체크해주세요.");
        if (ModelState.IsValid)
        {
            var result = await service.DeleteAsync(Owner, id, Version, cancellationToken);
            if (result.Status == 204)
            {
                TempData["TransactionMessage"] = "거래를 삭제했습니다.";
                return RedirectToPage("/Transactions/Index", new { month = item.TransactionDate.ToString("yyyy-MM") });
            }
            if (result.Status == 404) return NotFound();
            if (result.Status == 409) Response.StatusCode = 409;
            ModelState.AddModelError(string.Empty, result.Message!);
        }
        return Page();
    }
}
