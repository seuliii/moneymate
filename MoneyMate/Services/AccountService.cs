using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MoneyMate.Contracts;
using MoneyMate.Data;
using MoneyMate.Models;
using Npgsql;

namespace MoneyMate.Services;

public sealed class AccountService(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, MoneyMateDbContext db, TimeProvider clock)
{
    public async Task<AccountResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var name = request.DisplayName.Trim();
        if (name.Length == 0)
            return new(null, "invalid_name", "공백이 아닌 이름을 입력해주세요.");
        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Email = email, UserName = email, DisplayName = name,
            CreatedAt = clock.GetUtcNow()
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
                    return new(null, "duplicate_email", "이미 가입된 이메일입니다. 로그인해주세요.");
                return new(null, "invalid_registration", "가입 정보를 확인해주세요. 비밀번호는 8자 이상이며 영문 대문자·소문자·숫자를 포함해야 합니다.");
            }
            db.UserLedgerStates.Add(new UserLedgerState { UserId = user.Id, DataVersion = 0 });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(ToSummary(user));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Unique username protects simultaneous registration with the same normalized email.
            return new(null, "duplicate_email", "이미 가입된 이메일입니다. 로그인해주세요.");
        }
    }

    public async Task<AccountResult> LoginAsync(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null)
        {
            var result = await signIn.PasswordSignInAsync(user, request.Password,
                isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded) return new(ToSummary(user));
        }
        // Do not reveal whether the email exists or whether a particular account is locked.
        return new(null, "invalid_credentials", "이메일 또는 비밀번호를 확인해주세요. 반복 실패 시 5분간 로그인이 제한됩니다.");
    }

    public async Task<UserSummary?> GetCurrentAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;
        var user = await users.GetUserAsync(principal);
        return user is null ? null : ToSummary(user);
    }

    public Task LogoutAsync() => signIn.SignOutAsync();
    private static UserSummary ToSummary(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.CreatedAt);
}
