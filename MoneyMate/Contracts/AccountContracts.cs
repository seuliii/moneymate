using System.ComponentModel.DataAnnotations;

namespace MoneyMate.Contracts;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "이메일을 입력해주세요.")]
    [EmailAddress(ErrorMessage = "올바른 이메일 형식을 입력해주세요.")]
    [StringLength(254, ErrorMessage = "이메일은 254자 이하로 입력해주세요.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "이름을 입력해주세요.")]
    [StringLength(50, ErrorMessage = "이름은 50자 이하로 입력해주세요.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "비밀번호를 입력해주세요.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "비밀번호는 8~128자로 입력해주세요.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "비밀번호 확인을 입력해주세요.")]
    [Compare(nameof(Password), ErrorMessage = "비밀번호가 일치하지 않습니다.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "이메일을 입력해주세요.")]
    [EmailAddress(ErrorMessage = "올바른 이메일 형식을 입력해주세요.")]
    [StringLength(254, ErrorMessage = "이메일은 254자 이하로 입력해주세요.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "비밀번호를 입력해주세요.")]
    [StringLength(128, ErrorMessage = "비밀번호는 128자 이하로 입력해주세요.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}

public sealed record UserSummary(string Id, string Email, string DisplayName, DateTimeOffset CreatedAt);
public sealed record AccountResult(UserSummary? User, string? ErrorCode = null, string? Message = null);
