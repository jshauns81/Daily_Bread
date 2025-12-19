using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Daily_Bread.Data;

/// <summary>
/// Helper class to seed roles and bootstrap admin user on application startup.
/// Idempotent: safe to run on every startup without creating duplicates.
/// </summary>
public static class SeedData
{
    public const string AdminRole = "Admin";
    public const string ParentRole = "Parent";
    public const string ChildRole = "Child";

    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        using var scope = serviceProvider.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("SeedData");

        // Check if seeding is enabled (default: true)
        var runSeed = configuration.GetValue<bool?>("Seed:Run") ?? true;
        if (!runSeed)
        {
            logger.LogInformation("Seed:Run is false. Skipping all seeding.");
            return;
        }

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Seed roles: Admin, Parent, Child
        await EnsureRoleAsync(roleManager, AdminRole);
        await EnsureRoleAsync(roleManager, ParentRole);
        await EnsureRoleAsync(roleManager, ChildRole);

        // Seed default app settings
        await SeedAppSettingsAsync(context, logger);

        // Seed bootstrap admin user from configuration/environment
        // These come from .env file: ADMIN_USERNAME and ADMIN_PASSWORD
        var adminUserName = configuration["Seed:AdminUserName"] 
            ?? Environment.GetEnvironmentVariable("ADMIN_USERNAME");
        var adminPassword = configuration["Seed:AdminPassword"] 
            ?? Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(adminUserName) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogInformation("Admin credentials not configured. Skipping admin user creation.");
            logger.LogInformation("Set ADMIN_USERNAME and ADMIN_PASSWORD environment variables to create an admin user.");
            return;
        }

        await EnsureAdminUserAsync(userManager, adminUserName, adminPassword, logger);
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

    private static async Task SeedAppSettingsAsync(ApplicationDbContext context, ILogger logger)
    {
        var settingsToSeed = new List<AppSetting>
        {
            new()
            {
                Key = AppSettingKeys.TimeZone,
                Value = AppSettingKeys.DefaultTimeZone,
                Description = "Family timezone for determining 'today' and scheduling (IANA format)",
                DataType = SettingDataType.String
            },
            new()
            {
                Key = AppSettingKeys.CashOutThreshold,
                Value = AppSettingKeys.DefaultCashOutThreshold.ToString("F2"),
                Description = "Minimum balance required before cash out is allowed",
                DataType = SettingDataType.Decimal
            },
            new()
            {
                Key = AppSettingKeys.AllowChildSelfReport,
                Value = "true",
                Description = "Whether children can mark their own chores as completed",
                DataType = SettingDataType.Boolean
            }
        };

        foreach (var setting in settingsToSeed)
        {
            var existing = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == setting.Key);
            if (existing == null)
            {
                context.AppSettings.Add(setting);
                logger.LogInformation("Seeded app setting: {Key} = {Value}", setting.Key, setting.Value);
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task EnsureAdminUserAsync(
        UserManager<ApplicationUser> userManager,
        string userName,
        string password,
        ILogger logger)
    {
        var existingUser = await userManager.FindByNameAsync(userName);

        if (existingUser != null)
        {
            // Ensure existing user is in Admin role
            if (!await userManager.IsInRoleAsync(existingUser, AdminRole))
            {
                var roleResult = await userManager.AddToRoleAsync(existingUser, AdminRole);
                if (!roleResult.Succeeded)
                {
                    throw new Exception($"Failed to add existing user '{userName}' to role '{AdminRole}': {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
                }
            }
            logger.LogInformation("Admin user '{UserName}' already exists.", userName);
            return;
        }

        // Create new admin user
        var adminUser = new ApplicationUser
        {
            UserName = userName,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(adminUser, password);
        if (!createResult.Succeeded)
        {
            throw new Exception($"Failed to create admin user '{userName}': {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        var addRoleResult = await userManager.AddToRoleAsync(adminUser, AdminRole);
        if (!addRoleResult.Succeeded)
        {
            throw new Exception($"Failed to add admin user '{userName}' to role '{AdminRole}': {string.Join(", ", addRoleResult.Errors.Select(e => e.Description))}");
        }

        logger.LogInformation("Bootstrap admin user '{UserName}' created successfully.", userName);
    }
}
