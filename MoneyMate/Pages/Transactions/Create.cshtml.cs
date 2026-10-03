using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyMate.Services;

namespace MoneyMate.Pages.Transactions;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(TransactionService service, KoreanClock calendar) : TransactionFormPageModel(service, calendar)
{
    public override bool IsEdit => false;
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input.Type = "expense";
        Input.CategoryId = 1;
        Input.TransactionDate = Today;
        await LoadCategoriesAsync(cancellationToken);
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await Service.CreateAsync(Owner, Input, cancellationToken);
            if (result.Status == 201)
            {
                TempData["TransactionMessage"] = "거래를 등록했습니다.";
                return RedirectToPage("/Transactions/Index", new { month = result.Value!.TransactionDate.ToString("yyyy-MM") });
            }
            ModelState.AddModelError(string.Empty, result.Message!);
        }
        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }
}
