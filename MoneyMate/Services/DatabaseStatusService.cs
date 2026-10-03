using Microsoft.EntityFrameworkCore;
using MoneyMate.Contracts;
using MoneyMate.Data;

namespace MoneyMate.Services;

public sealed class DatabaseStatusService(MoneyMateDbContext db, IConfiguration configuration,
    ILogger<DatabaseStatusService> logger)
{
    public async Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("MoneyMate")))
            return new("not_configured", "DB 연결 설정이 필요합니다. README의 로컬 설정 안내를 확인해주세요.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await db.Database.OpenConnectionAsync(timeout.Token);
            if ((await db.Database.GetPendingMigrationsAsync(timeout.Token)).Any())
                return new("migration_required", "DB에 연결되었습니다. 초기 마이그레이션을 적용해주세요.");
            // Verify that the mapped table is queryable, rather than only checking the port.
            await db.Categories.CountAsync(timeout.Token);
            return new("ready", "DB 연결 및 테이블 조회가 정상입니다.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("unavailable", "DB 연결 확인 시간이 초과되었습니다.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not log connection strings, credentials, or exception message content.
            logger.LogWarning("Database check failed ({ErrorType}).", exception.GetType().Name);
            return new("unavailable", "DB에 연결하거나 테이블을 조회하지 못했습니다. 로컬 접속 설정을 확인해주세요.");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
