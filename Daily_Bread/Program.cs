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

// CRITICAL: Static files middleware MUST run FIRST, before any other middleware
// This ensures CSS, JS, images are served directly without ANY processing
// iPhone Safari is particularly sensitive to this ordering
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Add cache headers for static files
        // But also allow revalidation to ensure fresh content after deployments
        var headers = ctx.Context.Response.Headers;
        
        // Cache static files for 1 day, but allow revalidation
        headers.CacheControl = "public, max-age=86400, must-revalidate";
        
        // Ensure correct content types are set (helps Safari)
        // The static file middleware should do this, but be explicit
        var contentType = ctx.Context.Response.ContentType;
        if (string.IsNullOrEmpty(contentType))
        {
            var path = ctx.File.Name.ToLowerInvariant();
            if (path.EndsWith(".css")) ctx.Context.Response.ContentType = "text/css";
            else if (path.EndsWith(".js")) ctx.Context.Response.ContentType = "application/javascript";
            else if (path.EndsWith(".png")) ctx.Context.Response.ContentType = "image/png";
            else if (path.EndsWith(".svg")) ctx.Context.Response.ContentType = "image/svg+xml";
            else if (path.EndsWith(".ico")) ctx.Context.Response.ContentType = "image/x-icon";
            else if (path.EndsWith(".woff2")) ctx.Context.Response.ContentType = "font/woff2";
            else if (path.EndsWith(".woff")) ctx.Context.Response.ContentType = "font/woff";
        }
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Server-side redirect for unauthenticated document navigations only
// This middleware ONLY redirects page/document requests, never static assets
// 
// WHY THIS MATTERS FOR IPHONE SAFARI:
// - Safari sends different Accept headers than desktop browsers
// - Safari may request CSS with Accept: */* instead of text/css
// - Safari is less forgiving of redirects on static asset requests
// - If we redirect a CSS request to /Account/Login, Safari receives HTML
//   with Content-Type: text/html, causing the page to render unstyled
//
// SOLUTION: Use multiple detection methods to identify static assets:
// 1. Path.HasExtension() - catches files with any extension
// 2. Allowed path prefixes - framework paths, identity pages
// 3. Accept header inspection - only redirect if client wants HTML
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
    
    // Authenticated users proceed without redirect
    if (isAuthenticated)
    {
        await next();
        return;
    }
    
    // METHOD 1: Static asset detection by file extension
    // Path.HasExtension() returns true for any path ending in .xxx
    // This catches ALL static files: .css, .js, .png, .woff2, .json, etc.
    // This is more robust than maintaining an allowlist of extensions
    if (Path.HasExtension(path.Value))
    {
        await next();
        return;
    }
    
    // METHOD 2: Framework and allowed path prefixes
    // These paths must be accessible without authentication
    var allowedPathPrefixes = new[]
    {
        "/Account",           // Identity pages (login, logout, access denied)
        "/kid",               // Kid mode PIN login
        "/_blazor",           // Blazor SignalR hub and negotiation
        "/_framework",        // Blazor framework files
        "/_content",          // Razor class library static content
        "/health",            // Health check endpoint
        "/not-found"          // 404 error page
    };
    
    foreach (var prefix in allowedPathPrefixes)
    {
        if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }
    }
    
    // METHOD 3: Accept header inspection for document navigation detection
    // Only redirect requests that explicitly want HTML documents
    // 
    // Desktop Chrome: Accept: text/html,application/xhtml+xml,...
    // iPhone Safari:  Accept: text/html,application/xhtml+xml,...
    // CSS request:    Accept: text/css,*/*;q=0.1  (or just */*)
    // Image request:  Accept: image/webp,image/apng,image/*,*/*;q=0.8
    // Font request:   Accept: */*
    //
    // A request is a document navigation if it accepts text/html
    var acceptHeader = context.Request.Headers.Accept.ToString();
    var isDocumentRequest = !string.IsNullOrEmpty(acceptHeader) && 
                            acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    
    // If the request doesn't want HTML, don't redirect
    // This prevents redirecting asset requests that Safari sends with Accept: */*
    if (!isDocumentRequest)
    {
        await next();
        return;
    }
    
    // This is an unauthenticated document navigation request - redirect to login
    context.Response.Redirect("/Account/Login");
});

app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapStaticAssets();
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
