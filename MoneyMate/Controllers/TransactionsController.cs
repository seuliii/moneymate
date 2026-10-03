using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Controllers;

[ApiController, Authorize]
[Route("api/transactions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransactionsController(TransactionService service) : ControllerBase
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TransactionQuery input, CancellationToken cancellationToken) =>
        Respond(await service.ListAsync(Owner, input, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(Owner, id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }
    [HttpPost]
    public async Task<IActionResult> Create(TransactionInput input, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(Owner, input, cancellationToken);
        if (result.Status != 201) return Respond(result);
        Response.Headers["X-Ledger-Data-Version"] = result.DataVersion!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateTransactionRequest input, CancellationToken cancellationToken) =>
        Respond(await service.UpdateAsync(Owner, id, input, cancellationToken));
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery, Required, Range(1, long.MaxValue)] long? version, CancellationToken cancellationToken) =>
        Respond(await service.DeleteAsync(Owner, id, version!.Value, cancellationToken));

    private IActionResult Respond<T>(LedgerResult<T> result) where T : class
    {
        if (result.DataVersion is not null)
            Response.Headers["X-Ledger-Data-Version"] = result.DataVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return result.Status switch
        {
            200 => Ok(result.Value),
            204 => NoContent(),
            _ => Problem(statusCode: result.Status, title: "거래 요청을 완료하지 못했습니다.", detail: result.Message,
                extensions: new Dictionary<string, object?> { ["code"] = result.Code })
        };
    }
}
