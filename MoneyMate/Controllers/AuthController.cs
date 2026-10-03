using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(AccountService accounts, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken, headerName = tokens.HeaderName });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterAsync(request, cancellationToken);
        return result.User is not null
            ? StatusCode(StatusCodes.Status201Created, result.User)
            : Failure(result, result.ErrorCode == "duplicate_email" ? 409 : 400);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await accounts.LoginAsync(request);
        return result.User is not null ? Ok(result.User) : Failure(result, 401);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await accounts.LogoutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await accounts.GetCurrentAsync(User);
        return user is null ? Unauthorized() : Ok(user);
    }

    private ObjectResult Failure(AccountResult result, int status) => Problem(
        statusCode: status, title: "요청을 완료하지 못했습니다.", detail: result.Message,
        extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
}
