using System.ComponentModel.DataAnnotations;

namespace MoneyMate.Contracts;

public class TransactionInput
{
    [Required, RegularExpression("^(income|expense)$", ErrorMessage = "수입 또는 지출을 선택해주세요.")]
    public string Type { get; set; } = string.Empty;
    [Range(1, 1_000_000_000, ErrorMessage = "금액은 1~1,000,000,000원의 정수로 입력해주세요.")]
    public long Amount { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "카테고리를 선택해주세요.")]
    public int CategoryId { get; set; }
    [Required(ErrorMessage = "거래일을 입력해주세요.")]
    [DataType(DataType.Date)]
    public DateOnly? TransactionDate { get; set; }
    [Required(ErrorMessage = "내용을 입력해주세요.")]
    [StringLength(100, ErrorMessage = "내용은 100자 이하로 입력해주세요.")]
    public string Title { get; set; } = string.Empty;
    [StringLength(500, ErrorMessage = "메모는 500자 이하로 입력해주세요.")]
    public string? Memo { get; set; }
}

public sealed class UpdateTransactionRequest : TransactionInput
{
    [Range(1, long.MaxValue, ErrorMessage = "거래를 다시 불러온 뒤 수정해주세요.")]
    public long Version { get; set; }
}

public sealed class TransactionQuery
{
    public string? Month { get; set; }
    [RegularExpression("^(income|expense)$", ErrorMessage = "유형 필터가 올바르지 않습니다.")]
    public string? Type { get; set; }
    [Range(1, int.MaxValue)] public int? CategoryId { get; set; }
    [Range(1, 1_000_000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed record CategorySummary(int Id, string Code, string Name, string Type);
public sealed record TransactionSummary(Guid Id, string Type, long Amount, int CategoryId,
    string CategoryName, DateOnly TransactionDate, string Title, string? Memo, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record TransactionPage(IReadOnlyList<TransactionSummary> Items, string Month,
    int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record LedgerResult<T>(T? Value, int Status = 200, string? Code = null,
    string? Message = null, long? DataVersion = null) where T : class;
