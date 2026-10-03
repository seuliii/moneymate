using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Transactions;

public abstract class TransactionFormPageModel(TransactionService service, KoreanClock calendar) : PageModel
{
    [BindProperty] public UpdateTransactionRequest Input { get; set; } = new() { Version = 1 };
    public IReadOnlyList<CategorySummary> Categories { get; private set; } = [];
    public DateOnly Today => calendar.Today;
    public abstract bool IsEdit { get; }
    protected string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    protected TransactionService Service => service;
    protected Task LoadCategoriesAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);
    private async Task LoadAsync(CancellationToken cancellationToken) => Categories = await service.CategoriesAsync(null, cancellationToken);
    protected void Fill(TransactionSummary item) => Input = new()
    {
        Type = item.Type, Amount = item.Amount, CategoryId = item.CategoryId,
        TransactionDate = item.TransactionDate, Title = item.Title, Memo = item.Memo, Version = item.Version
    };
}
