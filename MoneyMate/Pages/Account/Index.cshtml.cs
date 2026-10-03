using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Account;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(AccountService accounts) : PageModel
{
    public UserSummary CurrentUser { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await accounts.GetCurrentAsync(User);
        if (user is null)
        {
            await accounts.LogoutAsync();
            return RedirectToPage("/Account/Login");
        }
        CurrentUser = user;
        return Page();
    }
}
