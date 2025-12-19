using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Daily_Bread.Components;
using Daily_Bread.Data;
using Daily_Bread.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add EF Core with automatic provider selection (PostgreSQL or SQLite)
// Supports Railway DATABASE_URL format and standard connection strings
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Check for Railway's DATABASE_URL or DATABASE_PRIVATE_URL environment variable
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") 
    ?? Environment.GetEnvironmentVariable("DATABASE_PRIVATE_URL")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL");

if (!string.IsNullOrEmpty(databaseUrl))
{
    connectionString = ConvertDatabaseUrlToConnectionString(databaseUrl);
    Console.WriteLine($"Using PostgreSQL database connection from environment variable");
}
else if (!string.IsNullOrEmpty(connectionString) && connectionString.StartsWith("postgresql://"))
{
    // ConnectionStrings__DefaultConnection might already be a URL format
    connectionString = ConvertDatabaseUrlToConnectionString(connectionString);
    Console.WriteLine($"Converted connection string from URL format");
}

// Log connection info (without sensitive data) for debugging
if (!string.IsNullOrEmpty(connectionString))
{
    var safeLog = connectionString.Contains("Host=") 
        ? $"Host={connectionString.Split(';').FirstOrDefault(s => s.StartsWith("Host="))?.Split('=').LastOrDefault()}"
        : "SQLite";
    Console.WriteLine($"Database connection type: {safeLog}");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // Suppress the PendingModelChangesWarning - we handle migrations at startup
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    
    if (!string.IsNullOrEmpty(connectionString) && 
        (connectionString.Contains("Host=") || connectionString.Contains("Server=") || connectionString.Contains("postgres")))
    {
        // PostgreSQL connection string detected (for production/Azure/Render/Railway)
        options.UseNpgsql(connectionString);
    }
    else
    {
        // SQLite for local development
        options.UseSqlite(connectionString ?? "Data Source=DailyBread.db");
    }
});

// Add Identity services
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password requirements
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Add authorization
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add application services
builder.Services.AddSingleton<IDateProvider, SystemDateProvider>();
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

// Ensure database schema is up to date
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        Console.WriteLine("Starting database setup...");
        
        // Check if we're using PostgreSQL (production) or SQLite (development)
        var isPostgres = db.Database.ProviderName?.Contains("Npgsql") == true;
        
        if (isPostgres)
        {
            // For PostgreSQL: Use EnsureCreated to create schema directly from model
            // This avoids SQLite-specific migration issues
            Console.WriteLine("PostgreSQL detected - using EnsureCreated...");
            var created = await db.Database.EnsureCreatedAsync();
            Console.WriteLine($"Database EnsureCreated result: {(created ? "Created new database" : "Database already exists")}");
        }
        else
        {
            // For SQLite: Use migrations as usual
            Console.WriteLine("SQLite detected - using migrations...");
            await db.Database.MigrateAsync();
        }
        
        Console.WriteLine("Database setup completed successfully.");
        logger.LogInformation("Database setup completed successfully.");
        
        // Verify the Achievements table exists before seeding
        try
        {
            var achievementCount = await db.Achievements.CountAsync();
            Console.WriteLine($"Achievements table exists with {achievementCount} records.");
            
            // Seed achievements
            var achievementService = scope.ServiceProvider.GetRequiredService<IAchievementService>();
            await achievementService.SeedAchievementsAsync();
            Console.WriteLine("Achievement seeding completed.");
        }
        catch (Exception seedEx)
        {
            Console.WriteLine($"Achievement seeding skipped - table may not exist yet: {seedEx.Message}");
            // Don't throw - the app can still run without achievements initially
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database setup error: {ex.Message}");
        logger.LogError(ex, "An error occurred while setting up the database.");
        throw;
    }
}

// Seed roles and admin user on startup
Console.WriteLine("Starting data seeding...");
await SeedData.InitializeAsync(app.Services, app.Configuration);
Console.WriteLine("Data seeding completed.");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Health check endpoint
app.MapHealthChecks("/health");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map logout endpoint to handle POST with antiforgery token
app.MapPost("/Account/Logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
});

app.Run();

// Helper method to convert Railway DATABASE_URL to Npgsql connection string
static string ConvertDatabaseUrlToConnectionString(string databaseUrl)
{
    // Railway format: postgresql://user:password@host:port/database
    // Npgsql format: Host=host;Port=port;Database=database;Username=user;Password=password;SSL Mode=Require;Trust Server Certificate=true
    
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
        // If parsing fails, return as-is (might already be in correct format)
        return databaseUrl;
    }
}
