using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyMate.Services;

namespace MoneyMate.Pages.Transactions;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EditModel(TransactionService service, KoreanClock calendar) : TransactionFormPageModel(service, calendar)
{
    public override bool IsEdit => true;
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await Service.GetAsync(Owner, id, cancellationToken);
        if (item is null) return NotFound();
        Fill(item);
        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await Service.GetAsync(Owner, id, cancellationToken) is null) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await Service.UpdateAsync(Owner, id, Input, cancellationToken);
            if (result.Status == 200)
            {
                TempData["TransactionMessage"] = "거래를 수정했습니다.";
                return RedirectToPage("/Transactions/Index", new { month = result.Value!.TransactionDate.ToString("yyyy-MM") });
            }
            if (result.Status == 404) return NotFound();
            if (result.Status == 409) Response.StatusCode = 409;
            ModelState.AddModelError(string.Empty, result.Message!);
        }
        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }
}
