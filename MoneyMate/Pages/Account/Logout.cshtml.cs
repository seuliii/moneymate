using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Services;

namespace MoneyMate.Pages.Account;

[Authorize]
public sealed class LogoutModel(AccountService accounts) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Index");
    public async Task<IActionResult> OnPostAsync()
    {
        await accounts.LogoutAsync();
        return RedirectToPage("/Index");
    }
}
