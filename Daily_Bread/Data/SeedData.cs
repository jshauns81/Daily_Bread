using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Data;

/// <summary>
/// Helper class to seed roles and development users on application startup.
/// Idempotent: safe to run on every startup without creating duplicates.
/// </summary>
public static class SeedData
{
    public const string ParentRole = "Parent";
    public const string ChildRole = "Child";

    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Ensure database is created and migrations applied
        await context.Database.MigrateAsync();

        // Seed roles
        await EnsureRoleAsync(roleManager, ParentRole);
        await EnsureRoleAsync(roleManager, ChildRole);

        // Seed development users from configuration
        var seedUsers = configuration.GetSection("SeedUsers");
        
        // Parent1
        var parent1Config = seedUsers.GetSection("Parent1");
        if (parent1Config.Exists())
        {
            await EnsureUserAsync(userManager,
                parent1Config["UserName"]!,
                parent1Config["Email"]!,
                parent1Config["Password"]!,
                ParentRole);
        }

        // Parent2
        var parent2Config = seedUsers.GetSection("Parent2");
        if (parent2Config.Exists())
        {
            await EnsureUserAsync(userManager,
                parent2Config["UserName"]!,
                parent2Config["Email"]!,
                parent2Config["Password"]!,
                ParentRole);
        }

        // Child1
        var child1Config = seedUsers.GetSection("Child1");
        if (child1Config.Exists())
        {
            var childUser = await EnsureUserAsync(userManager,
                child1Config["UserName"]!,
                child1Config["Email"]!,
                child1Config["Password"]!,
                ChildRole);
            
            // Create ChildProfile and LedgerAccount for this child
            await EnsureChildProfileAndAccountAsync(context, childUser);
        }

        // Migrate any existing transactions to ledger accounts
        await MigrateExistingTransactionsAsync(context);
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                throw new Exception($"Failed to create role '{roleName}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager, 
        string userName, 
        string email, 
        string password, 
        string role)
    {
        var user = await userManager.FindByNameAsync(userName);
        
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true // Skip email confirmation for dev users
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new Exception($"Failed to create user '{userName}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        // Ensure user is in the specified role
        if (!await userManager.IsInRoleAsync(user, role))
        {
            var result = await userManager.AddToRoleAsync(user, role);
            if (!result.Succeeded)
            {
                throw new Exception($"Failed to add user '{userName}' to role '{role}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        return user;
    }

    private static async Task EnsureChildProfileAndAccountAsync(ApplicationDbContext context, ApplicationUser user)
    {
        // Check if profile already exists
        var existingProfile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        if (existingProfile != null)
        {
            // Ensure at least one account exists
            if (!existingProfile.LedgerAccounts.Any())
            {
                existingProfile.LedgerAccounts.Add(new LedgerAccount
                {
                    Name = "Main",
                    IsDefault = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
            return;
        }

        // Create new profile with default account
        var profile = new ChildProfile
        {
            UserId = user.Id,
            DisplayName = user.UserName ?? "Child",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        profile.LedgerAccounts.Add(new LedgerAccount
        {
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        context.ChildProfiles.Add(profile);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Migrates any existing LedgerTransactions that don't have a LedgerAccountId
    /// to the appropriate child's default account.
    /// </summary>
    private static async Task MigrateExistingTransactionsAsync(ApplicationDbContext context)
    {
        // Find transactions without a LedgerAccountId (legacy data)
        var orphanedTransactions = await context.LedgerTransactions
            .Where(t => t.LedgerAccountId == 0)
            .ToListAsync();

        if (!orphanedTransactions.Any())
        {
            return;
        }

        // Group by UserId
        var transactionsByUser = orphanedTransactions.GroupBy(t => t.UserId);

        foreach (var userGroup in transactionsByUser)
        {
            var userId = userGroup.Key;

            // Find or create the child profile and account
            var profile = await context.ChildProfiles
                .Include(p => p.LedgerAccounts)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                // Create profile for this user
                var user = await context.Users.FindAsync(userId);
                if (user == null) continue;

                profile = new ChildProfile
                {
                    UserId = userId,
                    DisplayName = user.UserName ?? "Child",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                profile.LedgerAccounts.Add(new LedgerAccount
                {
                    Name = "Main",
                    IsDefault = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                context.ChildProfiles.Add(profile);
                await context.SaveChangesAsync();
            }

            // Get the default account
            var defaultAccount = profile.LedgerAccounts.FirstOrDefault(a => a.IsDefault)
                ?? profile.LedgerAccounts.FirstOrDefault();

            if (defaultAccount == null) continue;

            // Update all transactions for this user
            foreach (var txn in userGroup)
            {
                txn.LedgerAccountId = defaultAccount.Id;
            }
        }

        await context.SaveChangesAsync();
    }
}
