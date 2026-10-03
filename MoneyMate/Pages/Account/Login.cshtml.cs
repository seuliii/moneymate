using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginModel(AccountService accounts) : PageModel
{
    [BindProperty] public LoginRequest Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? RedirectToPage("/Account/Index") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await accounts.LoginAsync(Input);
        if (result.User is null)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
            return Page();
        }
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl!) : RedirectToPage("/Account/Index");
    }
}
