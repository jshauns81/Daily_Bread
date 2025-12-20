using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Daily_Bread.Components;
using Daily_Bread.Data;
using Daily_Bread.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure forwarded headers for reverse proxy (Railway, Azure, etc.)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Build PostgreSQL connection string from Railway environment variables
var connectionString = GetPostgresConnectionString(builder.Configuration);
Console.WriteLine($"Database configured: PostgreSQL");

// Configure DbContext for PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    });
});

// Add DbContext factory for Blazor Server (avoids concurrent access issues)
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    });
}, ServiceLifetime.Scoped);

// Add Identity services
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Configure authentication cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Add authorization
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add application services
builder.Services.AddSingleton<IDateProvider, SystemDateProvider>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IChoreScheduleService, ChoreScheduleService>();
builder.Services.AddScoped<IChoreLogService, ChoreLogService>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<ITrackerService, TrackerService>();
builder.Services.AddScoped<IChoreManagementService, ChoreManagementService>();
builder.Services.AddScoped<IPayoutService, PayoutService>();
builder.Services.AddScoped<IChildProfileService, ChildProfileService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<ISavingsGoalService, SavingsGoalService>();
builder.Services.AddScoped<IAchievementService, AchievementService>();
builder.Services.AddScoped<IKidModeService, KidModeService>();
builder.Services.AddScoped<IChoreChartService, ChoreChartService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// Enable forwarded headers FIRST
app.UseForwardedHeaders();

// Database setup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        Console.WriteLine("Starting PostgreSQL database setup...");
        
        var forceRecreate = Environment.GetEnvironmentVariable("RECREATE_DATABASE") == "true";
        
        if (forceRecreate)
        {
            Console.WriteLine("RECREATE_DATABASE=true - Dropping and recreating database...");
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            Console.WriteLine("Database recreated successfully.");
        }
        else
        {
            // Always ensure database and schema exist
            // EnsureCreatedAsync is idempotent - it won't recreate existing tables
            var created = await db.Database.EnsureCreatedAsync();
            if (created)
            {
                Console.WriteLine("Database schema created successfully.");
            }
            else
            {
                Console.WriteLine("Database schema already exists.");
            }
        }
        
        // Seed achievements
        try
        {
            var achievementService = scope.ServiceProvider.GetRequiredService<IAchievementService>();
            await achievementService.SeedAchievementsAsync();
            Console.WriteLine("Achievement seeding completed.");
        }
        catch (Exception seedEx)
        {
            Console.WriteLine($"Achievement seeding error (non-fatal): {seedEx.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database setup error: {ex.Message}");
        logger.LogError(ex, "Database setup failed.");
        throw;
    }
}

// Seed roles and admin user
Console.WriteLine("Starting data seeding...");
await SeedData.InitializeAsync(app.Services, app.Configuration);
Console.WriteLine("Data seeding completed.");

// Seed default chores for child users
Console.WriteLine("Starting chore seeding...");
await SeedChores.SeedDefaultChoresAsync(app.Services, app.Configuration);
Console.WriteLine("Chore seeding completed.");

// Configure HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    var showErrors = Environment.GetEnvironmentVariable("SHOW_ERRORS") == "true";
    if (showErrors)
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/Account/Logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
});

app.Run();

// Helper to build PostgreSQL connection string from Railway environment variables
static string GetPostgresConnectionString(IConfiguration configuration)
{
    // Check for Railway DATABASE_URL format
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") 
        ?? Environment.GetEnvironmentVariable("DATABASE_PRIVATE_URL")
        ?? Environment.GetEnvironmentVariable("POSTGRES_URL");

    if (!string.IsNullOrEmpty(databaseUrl))
    {
        return ConvertDatabaseUrlToConnectionString(databaseUrl);
    }

    // Check appsettings connection string
    var configConnection = configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrEmpty(configConnection))
    {
        if (configConnection.StartsWith("postgresql://") || configConnection.StartsWith("postgres://"))
        {
            return ConvertDatabaseUrlToConnectionString(configConnection);
        }
        return configConnection;
    }

    // Default for local development with Docker PostgreSQL
    return "Host=localhost;Port=5432;Database=dailybread;Username=postgres;Password=postgres";
}

static string ConvertDatabaseUrlToConnectionString(string databaseUrl)
{
    try
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
    catch
    {
        return databaseUrl;
    }
}
