using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Daily_Bread.Components;
using Daily_Bread.Components.Account;
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

// Add Identity services with proper Blazor configuration
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Configure authentication cookie with hardened security settings
builder.Services.ConfigureApplicationCookie(options =>
{
    // Redirect paths
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    
    // Session duration - 7 days with sliding expiration
    // Each authenticated request extends the session by this duration
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    
    // Cookie name - customize to avoid fingerprinting default ASP.NET cookies
    options.Cookie.Name = ".DailyBread.Auth";
    
    // HttpOnly = true prevents JavaScript from accessing the cookie
    // This mitigates XSS attacks that try to steal session cookies
    options.Cookie.HttpOnly = true;
    
    // SameSite = Lax provides CSRF protection while allowing navigation links
    // - Strict: Cookie only sent for same-site requests (breaks external links)
    // - Lax: Cookie sent for same-site + top-level navigations (good balance)
    // - None: Cookie sent for all requests (requires Secure, allows CSRF)
    options.Cookie.SameSite = SameSiteMode.Lax;
    
    // SecurePolicy = SameAsRequest allows HTTP locally, HTTPS in production
    // When behind a reverse proxy with HTTPS termination (Railway, Azure, etc.),
    // UseForwardedHeaders() ensures the app sees the original HTTPS scheme,
    // so cookies will be marked Secure automatically.
    //
    // For HTTPS-only deployments without a proxy, change to:
    // options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    
    // IsEssential = true means this cookie is required for the app to function
    // and won't be blocked by cookie consent policies
    options.Cookie.IsEssential = true;
});

// Add authorization with default-deny policy
// This makes [Authorize] the default for all pages/endpoints
// Pages that need anonymous access must explicitly use [AllowAnonymous]
builder.Services.AddAuthorization(options =>
{
    // FallbackPolicy applies to any endpoint that doesn't have explicit authorization
    // This means all Razor components require authentication by default
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Add application services
builder.Services.AddScoped<IDateProvider, SystemDateProvider>();
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
builder.Services.AddScoped<IChorePlannerService, ChorePlannerService>();

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

// Static files should be served BEFORE authentication redirect
// This ensures CSS, JS, images are always accessible
app.MapStaticAssets();

app.UseAuthentication();
app.UseAuthorization();

// Comprehensive server-side redirect for unauthenticated users
// This prevents UI flashes and URL probing by redirecting BEFORE Blazor renders anything
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
    
    // Skip redirect for authenticated users
    if (isAuthenticated)
    {
        await next();
        return;
    }
    
    // Allow these paths without authentication:
    // 1. Identity/Account pages (login, logout, access denied, etc.)
    // 2. Kid mode PIN login
    // 3. Static assets (CSS, JS, images, fonts)
    // 4. Blazor framework endpoints (_blazor, _framework)
    // 5. Health check endpoint
    // 6. Not found page (for proper 404 handling)
    
    var allowedPaths = new[]
    {
        "/Account",           // Identity pages
        "/kid",               // Kid PIN login
        "/_blazor",           // Blazor SignalR hub
        "/_framework",        // Blazor framework files
        "/_content",          // Razor class library content
        "/health",            // Health check endpoint
        "/not-found",         // 404 page
        "/images",            // Public images (bread-icon.png, etc.)
        "/lib",               // Library assets (bootstrap, etc.)
        "/favicon"            // Favicon
    };
    
    var allowedExtensions = new[]
    {
        ".css", ".js", ".map",           // Stylesheets and scripts
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".webp",  // Images
        ".woff", ".woff2", ".ttf", ".eot", // Fonts
        ".json", ".xml"                   // Data files
    };
    
    // Check if path starts with an allowed prefix
    var isAllowedPath = allowedPaths.Any(allowed => 
        path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase));
    
    // Check if path is a static asset by extension
    var isStaticAsset = allowedExtensions.Any(ext => 
        path.Value?.EndsWith(ext, StringComparison.OrdinalIgnoreCase) == true);
    
    // Allow if path is permitted or is a static asset
    if (isAllowedPath || isStaticAsset)
    {
        await next();
        return;
    }
    
    // Redirect all other unauthenticated requests to login
    context.Response.Redirect("/Account/Login");
});

app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map additional Identity endpoints
app.MapAdditionalIdentityEndpoints();

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

/// <summary>
/// Extension methods for mapping additional Identity endpoints.
/// </summary>
internal static class IdentityEndpointsExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Account");

        group.MapPost("/Logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect("~/Account/Login");
        });

        return group;
    }
}
