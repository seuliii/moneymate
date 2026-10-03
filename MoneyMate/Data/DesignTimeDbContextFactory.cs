using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MoneyMate.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MoneyMateDbContext>
{
    public MoneyMateDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddUserSecrets<MoneyMateDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        // A passwordless placeholder permits migration generation without DB credentials.
        // Applying migrations still requires the real local connection setting.
        var connection = configuration.GetConnectionString("MoneyMate")
            ?? "Host=localhost;Port=5432;Database=moneymate;Username=moneymate_app";
        return new MoneyMateDbContext(new DbContextOptionsBuilder<MoneyMateDbContext>()
            .UseNpgsql(connection).Options);
    }
}
