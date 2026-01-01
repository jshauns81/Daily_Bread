using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Daily_Bread.Components;
using Daily_Bread.Components.Account;
using Daily_Bread.Data;
using Daily_Bread.Services;

// Load .env file for local development test
LoadDotEnv();

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
    
    // ✅ LOCKOUT PROTECTION ENABLED
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders()
.AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>(); // Add custom claims factory

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
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    
    // ✅ ROLE-BASED POLICIES
    options.AddPolicy("RequireParent", policy =>
        policy.RequireRole("Parent", "Admin"));
    
    options.AddPolicy("RequireChild", policy =>
        policy.RequireRole("Child"));
    
    options.AddPolicy("RequireAdmin", policy =>
        policy.RequireRole("Admin"));
    
    // ✅ HOUSEHOLD ISOLATION POLICY
    // This can be used on endpoints that need to verify household context
    options.AddPolicy("RequireHousehold", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "HouseholdId")));
});

// Add application services
builder.Services.AddScoped<IDateProvider, SystemDateProvider>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
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
builder.Services.AddScoped<IFamilySettingsService, FamilySettingsService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<IWeeklyProgressService, WeeklyProgressService>();
builder.Services.AddScoped<IWeeklyReconciliationService, WeeklyReconciliationService>();
builder.Services.AddScoped<IBiometricAuthService, BiometricAuthService>();
builder.Services.AddScoped<IAppStateService, AppStateService>();
builder.Services.AddScoped<INavigationService, NavigationService>();

// Achievement system services (order matters - dependencies first)
builder.Services.AddScoped<IAchievementConditionEvaluator, AchievementConditionEvaluator>();
builder.Services.AddScoped<IAchievementBonusService, AchievementBonusService>();

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
            await db.Database.MigrateAsync();
            Console.WriteLine("Database recreated with migrations successfully.");
        }
        else
        {
            // Apply any pending migrations
            Console.WriteLine("Applying database migrations...");
            await db.Database.MigrateAsync();
            Console.WriteLine("Database migrations applied successfully.");
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
        
        // Seed default family settings
        try
        {
            var familySettingsService = scope.ServiceProvider.GetRequiredService<IFamilySettingsService>();
            await familySettingsService.GetSettingsAsync(); // Creates default if not exists
            Console.WriteLine("Family settings initialized.");
        }
        catch (Exception seedEx)
        {
            Console.WriteLine($"Family settings initialization error (non-fatal): {seedEx.Message}");
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

// Serve static files from wwwroot
// Configure with explicit file provider to ensure correct root
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Redirect unauthenticated document requests to login
// Static files are already served above, so this only affects page requests
app.Use(async (context, next) =>
{
    // Skip if authenticated
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        await next();
        return;
    }
    
    var path = context.Request.Path.Value ?? "";
    
    // Allow static assets (files with extensions)
    if (Path.HasExtension(path))
    {
        await next();
        return;
    }
    
    // Allow framework paths
    if (path.StartsWith("/_", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    
    // Allow specific anonymous pages
    if (context.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/kid", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/auth", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/not-found", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    
    // Check headers to avoid redirecting non-document requests
    var secFetchDest = context.Request.Headers["Sec-Fetch-Dest"].ToString();
    if (!string.IsNullOrEmpty(secFetchDest) && secFetchDest != "document")
    {
        await next();
        return;
    }
    
    var accept = context.Request.Headers.Accept.ToString();
    if (string.IsNullOrEmpty(accept) || !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    
    // Redirect to login
    context.Response.Redirect("/Account/Login");
});

// Map endpoints - these run AFTER the middleware above
app.MapHealthChecks("/health").AllowAnonymous();

// PIN authentication endpoint - handles Kid Mode sign-in via HTTP POST
// This allows the auth cookie to be set properly via HTTP response
app.MapPost("/auth/pin", async (
    HttpContext context,
    Daily_Bread.Services.IAuthenticationService authService,
    ILogger<Program> logger) =>
{
    try
    {
        // Read JSON body
        var body = await context.Request.ReadFromJsonAsync<PinLoginRequest>();
        
        if (body == null || string.IsNullOrEmpty(body.Pin))
        {
            return Results.Json(new { message = "PIN is required." }, statusCode: 400);
        }
        
        // Validate PIN format
        if (body.Pin.Length != 4 || !body.Pin.All(char.IsDigit))
        {
            return Results.Json(new { message = "Invalid PIN format." }, statusCode: 400);
        }
        
        // Use the centralized authentication service
        var credential = new Daily_Bread.Services.PinCredential
        {
            Pin = body.Pin,
            RememberDevice = true
        };
        
        var result = await authService.SignInAsync(credential);
        
        if (result.Success)
        {
            logger.LogInformation("PIN login successful for user {UserId}", result.User?.UserId);
            return Results.Ok(new { success = true, userName = result.User?.UserName });
        }
        else
        {
            logger.LogWarning("PIN login failed: {ErrorCode}", result.ErrorCode);
            return Results.Json(new { message = result.UserFacingMessage ?? "Invalid PIN. Please try again." }, statusCode: 401);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception during PIN authentication endpoint");
        return Results.Json(new { message = "An error occurred. Please try again." }, statusCode: 500);
    }
}).AllowAnonymous();

// MapStaticAssets serves fingerprinted assets generated by @Assets[] directive
// MUST allow anonymous access since these are CSS/JS files needed before login
app.MapStaticAssets().AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map additional Identity endpoints
app.MapAdditionalIdentityEndpoints();

// ============================================================================
// ENDPOINT DEBUGGING: Print ALL endpoints related to Account/Identity/Login
// ============================================================================
Console.WriteLine("\n========================================");
Console.WriteLine("ENDPOINT ROUTE ANALYSIS (COMPREHENSIVE)");
Console.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
Console.WriteLine("========================================\n");

var endpointDataSource = app.Services.GetRequiredService<EndpointDataSource>();

// Get ALL endpoints that might be related to authentication
var relevantEndpoints = endpointDataSource.Endpoints
    .Where(e => 
    {
        var displayName = e.DisplayName ?? "";
        var routePattern = (e as RouteEndpoint)?.RoutePattern.RawText ?? "";
        
        return displayName.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains("Identity", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains("Logout", StringComparison.OrdinalIgnoreCase) ||
               routePattern.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
               routePattern.Contains("Login", StringComparison.OrdinalIgnoreCase);
    })
    .ToList();

Console.WriteLine($"Found {relevantEndpoints.Count} endpoint(s) matching Account/Identity/Login patterns:\n");

var loginEndpoints = new List<(string Pattern, string HttpMethods, string Type)>();

foreach (var endpoint in relevantEndpoints)
{
    Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.WriteLine($"Display Name: {endpoint.DisplayName}");
    
    // Try to get route pattern
    var routeEndpoint = endpoint as RouteEndpoint;
    var pattern = routeEndpoint?.RoutePattern.RawText ?? "(no pattern)";
    Console.WriteLine($"  Route Pattern: {pattern}");
    Console.WriteLine($"  Order: {routeEndpoint?.Order ?? -1}");
    
    // Get HTTP methods
    var httpMethodMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>();
    var methods = httpMethodMetadata?.HttpMethods != null 
        ? string.Join(", ", httpMethodMetadata.HttpMethods)
        : "ALL (no restriction)";
    Console.WriteLine($"  HTTP Methods: {methods}");
    
    // Check for component metadata (Blazor)
    var componentMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Components.Endpoints.ComponentTypeMetadata>();
    if (componentMetadata != null)
    {
        Console.WriteLine($"  ✓ Blazor Component: {componentMetadata.Type.FullName}");
    }
    
    // Check for Razor Pages
    var pageMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>();
    if (pageMetadata != null)
    {
        Console.WriteLine($"  ⚠ Razor Page: {pageMetadata.RelativePath}");
        Console.WriteLine($"  ⚠ Page Route: {pageMetadata.RouteValues["page"]}");
    }
    
    // Check for antiforgery
    var antiforgeryMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata>();
    if (antiforgeryMetadata != null)
    {
        Console.WriteLine($"  Antiforgery Required: {antiforgeryMetadata.RequiresValidation}");
    }
    
    // Check for authorization
    var authMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>();
    if (authMetadata != null)
    {
        Console.WriteLine($"  Authorization: [AllowAnonymous]");
    }
    
    // Track /Account/Login endpoints specifically
    if (pattern.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
    {
        var type = componentMetadata != null ? "Blazor" : (pageMetadata != null ? "RazorPages" : "MinimalAPI");
        loginEndpoints.Add((pattern, methods, type));
    }
    
    Console.WriteLine();
}

// Summary for /Account/Login
Console.WriteLine("========================================");
Console.WriteLine("ROUTE COLLISION CHECK: /Account/Login");
Console.WriteLine("========================================");

if (loginEndpoints.Count == 0)
{
    Console.WriteLine("⚠ WARNING: No endpoint found for /Account/Login!");
    Console.WriteLine("  Login will be handled by the Blazor catch-all route via MapRazorComponents.");
}
else if (loginEndpoints.Count == 1)
{
    var ep = loginEndpoints[0];
    Console.WriteLine($"✓ Single handler found: {ep.Type}");
    Console.WriteLine($"  Pattern: {ep.Pattern}");
    Console.WriteLine($"  Methods: {ep.HttpMethods}");
}
else
{
    Console.WriteLine($"⚠ COLLISION DETECTED: {loginEndpoints.Count} handlers for /Account/Login!");
    foreach (var ep in loginEndpoints)
    {
        Console.WriteLine($"  - {ep.Type}: {ep.Pattern} [{ep.HttpMethods}]");
    }
}

Console.WriteLine("\n========================================\n");

app.Run();

// ============================================================================
// Local helper functions (must be after all top-level statements, before types)
// ============================================================================

// Helper to load .env file for local development
static void LoadDotEnv()
{
    // When running from Visual Studio, current directory is the project folder (Daily_Bread)
    // The .env file is at the solution root (parent directory)
    var projectDir = Directory.GetCurrentDirectory();
    
    // Check solution root first (parent of project directory)
    var solutionRoot = Directory.GetParent(projectDir)?.FullName;
    var envPath = solutionRoot != null ? Path.Combine(solutionRoot, ".env") : null;
    
    // If not found at solution root, check project directory
    if (envPath == null || !File.Exists(envPath))
    {
        envPath = Path.Combine(projectDir, ".env");
    }
    
    if (!File.Exists(envPath))
    {
        Console.WriteLine("No .env file found - using existing environment variables");
        return;
    }
    
    Console.WriteLine($"Loading environment from: {envPath}");
    
    foreach (var line in File.ReadAllLines(envPath))
    {
        // Skip empty lines and comments
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            continue;
        
        var parts = line.Split('=', 2);
        if (parts.Length != 2)
            continue;
        
        var key = parts[0].Trim();
        var value = parts[1].Trim();
        
        // Only set if not already set (allows overriding via system env vars)
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

// Helper to build PostgreSQL connection string from environment variables
static string GetPostgresConnectionString(IConfiguration configuration)
{
    // Check for DATABASE_URL format (common in cloud providers: Heroku, Render, etc.)
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") 
        ?? Environment.GetEnvironmentVariable("DATABASE_PRIVATE_URL")
        ?? Environment.GetEnvironmentVariable("POSTGRES_URL");

    if (!string.IsNullOrEmpty(databaseUrl))
    {
        return ConvertDatabaseUrlToConnectionString(databaseUrl);
    }

    // Check for individual POSTGRES_* env vars (from .env file for local dev)
    var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER");
    var postgresPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
    var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB");
    var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    var postgresPort = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";

    if (!string.IsNullOrEmpty(postgresUser) && !string.IsNullOrEmpty(postgresPassword) && !string.IsNullOrEmpty(postgresDb))
    {
        Console.WriteLine($"Using POSTGRES_* environment variables (Host={postgresHost}, Database={postgresDb})");
        return $"Host={postgresHost};Port={postgresPort};Database={postgresDb};Username={postgresUser};Password={postgresPassword}";
    }

    // Fall back to appsettings connection string
    var configConnection = configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrEmpty(configConnection))
    {
        if (configConnection.StartsWith("postgresql://") || configConnection.StartsWith("postgres://"))
        {
            return ConvertDatabaseUrlToConnectionString(configConnection);
        }
        return configConnection;
    }

    // Last resort fallback
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

internal static class IdentityEndpointsExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Account");

        // Login POST endpoint - handles form submission from Blazor Login.razor
        // Uses /PerformLogin to avoid collision with the /Login Blazor page route
        group.MapPost("/PerformLogin", async (
            HttpContext context,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<Program> logger) =>
        {
            var form = await context.Request.ReadFormAsync();
            var userName = form["Input.UserName"].ToString();
            var password = form["Input.Password"].ToString();
            var rememberMe = form["Input.RememberMe"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
            var returnUrl = context.Request.Query["ReturnUrl"].FirstOrDefault() ?? "/";

            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                logger.LogWarning("Login attempt with empty username or password");
                return Results.LocalRedirect($"~/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}&error=invalid");
            }

            var result = await signInManager.PasswordSignInAsync(userName, password, rememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var user = await userManager.FindByNameAsync(userName);
                logger.LogInformation("User {UserName} logged in successfully", userName);

                // Check if admin-only user
                if (user != null)
                {
                    var roles = await userManager.GetRolesAsync(user);
                    if (roles.Contains("Admin") && !roles.Contains("Parent"))
                    {
                        return Results.LocalRedirect("~/admin/users");
                    }
                }

                return Results.LocalRedirect($"~{returnUrl}");
            }

            if (result.IsLockedOut)
            {
                logger.LogWarning("User {UserName} account locked out", userName);
                return Results.LocalRedirect($"~/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}&error=lockout");
            }

            logger.LogWarning("Failed login attempt for {UserName}", userName);
            return Results.LocalRedirect($"~/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}&error=invalid");
        }).DisableAntiforgery().AllowAnonymous();

        // Logout POST endpoint
        group.MapPost("/Logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect("~/Account/Login");
        }).DisableAntiforgery();

        return group;
    }
}

/// <summary>
/// Request body for PIN-based authentication.
/// </summary>
internal record PinLoginRequest(string? Pin);
