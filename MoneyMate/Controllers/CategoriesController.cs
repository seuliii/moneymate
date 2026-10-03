using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyMate.Services;

namespace MoneyMate.Controllers;

[ApiController, Authorize]
[Route("api/categories")]
public sealed class CategoriesController(TransactionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery, RegularExpression("^(income|expense)$")] string? type, CancellationToken cancellationToken) =>
        Ok(await service.CategoriesAsync(type, cancellationToken));
}
