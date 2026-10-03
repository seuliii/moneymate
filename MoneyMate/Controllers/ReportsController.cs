using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Controllers;

[ApiController, Authorize, Route("api/analysis/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(ReportService service) : ControllerBase
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpPost] public async Task<IActionResult> Generate(GenerateReportRequest input, CancellationToken ct)
    {
        var result = await service.GenerateAsync(Owner, input.Month, ct);
        return result.Status == 201 ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value) : Respond(result);
    }
    [HttpGet("latest")] public async Task<IActionResult> Latest([FromQuery] string month, CancellationToken ct) => Respond(await service.LatestAsync(Owner, month, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Respond(await service.GetAsync(Owner, id, ct));
    private IActionResult Respond(ReportResult result) => result.Status == 200 ? Ok(result.Value)
        : Problem(statusCode: result.Status, title: "리포트 요청을 완료하지 못했습니다.", detail: result.Message,
            extensions: new Dictionary<string, object?> { ["code"] = result.Code });
}
