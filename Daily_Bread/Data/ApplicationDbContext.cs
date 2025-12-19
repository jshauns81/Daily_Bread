using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Data;

/// <summary>
/// Application database context for Identity and EF Core.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ChoreDefinition> ChoreDefinitions => Set<ChoreDefinition>();
    public DbSet<ChoreLog> ChoreLogs => Set<ChoreLog>();
    public DbSet<ChoreScheduleOverride> ChoreScheduleOverrides => Set<ChoreScheduleOverride>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<ChildProfile> ChildProfiles => Set<ChildProfile>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ChoreDefinition configuration
        builder.Entity<ChoreDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Value).HasPrecision(10, 2);
            entity.HasIndex(e => e.AssignedUserId);
            entity.HasIndex(e => e.IsActive);

            entity.HasOne(e => e.AssignedUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ChoreLog configuration
        builder.Entity<ChoreLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Notes).HasMaxLength(500);

            // Unique constraint: one log per chore per date
            entity.HasIndex(e => new { e.ChoreDefinitionId, e.Date }).IsUnique();
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.ChoreDefinition)
                .WithMany(c => c.ChoreLogs)
                .HasForeignKey(e => e.ChoreDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CompletedByUser)
                .WithMany()
                .HasForeignKey(e => e.CompletedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ApprovedByUser)
                .WithMany()
                .HasForeignKey(e => e.ApprovedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ChoreScheduleOverride configuration
        builder.Entity<ChoreScheduleOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OverrideValue).HasPrecision(10, 2);

            // Unique: one override per chore per date
            entity.HasIndex(e => new { e.ChoreDefinitionId, e.Date }).IsUnique();
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.Type);

            entity.HasOne(e => e.ChoreDefinition)
                .WithMany()
                .HasForeignKey(e => e.ChoreDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.OverrideAssignedUser)
                .WithMany()
                .HasForeignKey(e => e.OverrideAssignedUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ChildProfile configuration
        builder.Entity<ChildProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            
            // One user can have one child profile
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.IsActive);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // LedgerAccount configuration
        builder.Entity<LedgerAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            
            entity.HasIndex(e => e.ChildProfileId);
            entity.HasIndex(e => e.IsActive);
            // Unique account name per child profile
            entity.HasIndex(e => new { e.ChildProfileId, e.Name }).IsUnique();

            entity.HasOne(e => e.ChildProfile)
                .WithMany(c => c.LedgerAccounts)
                .HasForeignKey(e => e.ChildProfileId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete of accounts with transactions
        });

        // LedgerTransaction configuration
        builder.Entity<LedgerTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Description).HasMaxLength(200);

            // Note: ChoreLogId is not unique-indexed because:
            // 1. Filtering partial unique indexes have DB-specific syntax (SQLite: [col], PostgreSQL: "col")
            // 2. Most transactions don't have a ChoreLogId (manual adjustments, payouts, etc.)
            // The one-to-one relationship is enforced by the FK configuration below
            entity.HasIndex(e => e.ChoreLogId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.LedgerAccountId);
            entity.HasIndex(e => e.TransactionDate);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.TransferGroupId);

            entity.HasOne(e => e.LedgerAccount)
                .WithMany(a => a.LedgerTransactions)
                .HasForeignKey(e => e.LedgerAccountId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete of transactions

            entity.HasOne(e => e.ChoreLog)
                .WithOne(c => c.LedgerTransaction)
                .HasForeignKey<LedgerTransaction>(e => e.ChoreLogId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete
        });

        // SavingsGoal configuration
        builder.Entity<SavingsGoal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.TargetAmount).HasPrecision(10, 2);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsPrimary);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Achievement configuration
        builder.Entity<Achievement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Icon).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.IsActive);
        });

        // UserAchievement configuration
        builder.Entity<UserAchievement>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique: one user can earn each achievement only once
            entity.HasIndex(e => new { e.UserId, e.AchievementId }).IsUnique();
            entity.HasIndex(e => e.EarnedAt);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Achievement)
                .WithMany()
                .HasForeignKey(e => e.AchievementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AppSetting configuration
        builder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // UserPreference configuration
        builder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(1000).IsRequired();

            // Unique constraint: one preference per user per key
            entity.HasIndex(e => new { e.UserId, e.Key }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
