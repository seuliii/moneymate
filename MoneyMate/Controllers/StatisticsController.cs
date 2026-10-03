using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyMate.Services;

namespace MoneyMate.Controllers;

[ApiController, Authorize, Route("api/statistics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StatisticsController(StatisticsService service) : ControllerBase
{
    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly([FromQuery] string? month, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, month, cancellationToken);
        return result.Status == 200 ? Ok(result.Value) : Problem(statusCode: result.Status,
            title: "통계를 조회하지 못했습니다.", detail: result.Message,
            extensions: new Dictionary<string, object?> { ["code"] = result.Code });
    }
}
