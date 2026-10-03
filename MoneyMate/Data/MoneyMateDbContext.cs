using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MoneyMate.Models;

namespace MoneyMate.Data;

public sealed class MoneyMateDbContext(DbContextOptions<MoneyMateDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();
    public DbSet<UserLedgerState> UserLedgerStates => Set<UserLedgerState>();
    public DbSet<AnalysisReport> AnalysisReports => Set<AnalysisReport>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ApplicationUser>().Property(x => x.DisplayName).HasMaxLength(50);

        model.Entity<Category>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.Name).HasMaxLength(30);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasAlternateKey(x => new { x.Id, x.Type });
            entity.ToTable("Categories", table => table.HasCheckConstraint("CK_Categories_Type", "\"Type\" IN (1, 2)"));
            entity.HasData(CategorySeed.All);
        });

        model.Entity<LedgerTransaction>(entity =>
        {
            entity.ToTable("Transactions", table =>
            {
                table.HasCheckConstraint("CK_Transactions_Amount", "\"Amount\" BETWEEN 1 AND 1000000000");
                table.HasCheckConstraint("CK_Transactions_Type", "\"Type\" IN (1, 2)");
                table.HasCheckConstraint("CK_Transactions_Title", "length(btrim(\"Title\")) BETWEEN 1 AND 100");
                table.HasCheckConstraint("CK_Transactions_Version", "\"Version\" >= 1");
            });
            entity.Property(x => x.Title).HasMaxLength(100);
            entity.Property(x => x.Memo).HasMaxLength(500);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.TransactionDate, x.Id });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Category>().WithMany().HasForeignKey(x => new { x.CategoryId, x.Type })
                .HasPrincipalKey(x => new { x.Id, x.Type }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<UserLedgerState>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.DataVersion).IsConcurrencyToken();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserLedgerState>(x => x.UserId);
            entity.ToTable("UserLedgerStates", table => table.HasCheckConstraint("CK_UserLedgerStates_Version", "\"DataVersion\" >= 0"));
        });

        model.Entity<AnalysisReport>(entity =>
        {
            entity.Property(x => x.ContractVersion).HasMaxLength(30);
            entity.Property(x => x.PromptVersion).HasMaxLength(50);
            entity.Property(x => x.ModelKey).HasMaxLength(100);
            entity.Property(x => x.InputSnapshot).HasColumnType("jsonb");
            entity.Property(x => x.Result).HasColumnType("jsonb");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            entity.HasIndex(x => new { x.UserId, x.Month, x.GeneratedAt });
            entity.HasIndex(x => new { x.UserId, x.Month, x.DataVersion, x.AsOfDate, x.ContractVersion, x.PromptVersion, x.ModelKey }).IsUnique();
            entity.ToTable("AnalysisReports", table =>
            {
                table.HasCheckConstraint("CK_AnalysisReports_Month", "EXTRACT(DAY FROM \"Month\") = 1");
                table.HasCheckConstraint("CK_AnalysisReports_Version", "\"DataVersion\" >= 0");
            });
        });
    }
}
