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
public sealed class RegisterModel(AccountService accounts) : PageModel
{
    [BindProperty] public RegisterRequest Input { get; set; } = new();
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? RedirectToPage("/Account/Index") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var result = await accounts.RegisterAsync(Input, cancellationToken);
        if (result.User is null)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
            return Page();
        }
        TempData["AccountMessage"] = "회원가입이 완료되었습니다. 로그인해주세요.";
        return RedirectToPage("/Account/Login");
    }
}
