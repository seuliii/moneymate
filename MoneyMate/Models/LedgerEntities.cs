namespace MoneyMate.Models;

public enum TransactionType { Income = 1, Expense = 2 }

public sealed class Category
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public int SortOrder { get; set; }
}

public sealed class LedgerTransaction
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public long Amount { get; set; }
    public int CategoryId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserLedgerState
{
    public string UserId { get; set; } = string.Empty;
    public long DataVersion { get; set; }
}

public sealed class AnalysisReport
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateOnly Month { get; set; }
    public long DataVersion { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string ContractVersion { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string ModelKey { get; set; } = string.Empty;
    public string InputSnapshot { get; set; } = "{}";
    public string Result { get; set; } = "{}";
    public DateTimeOffset GeneratedAt { get; set; }
}
