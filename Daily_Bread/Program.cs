using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Daily_Bread.Components;
using Daily_Bread.Components.Account;
using Daily_Bread.Data;
using Daily_Bread.Services;
using Daily_Bread.Hubs;

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

// Persist DataProtection keys to a mounted volume so auth cookies (and the
// upcoming OIDC correlation/nonce cookies) survive container redeploys.
// Without this, keys regenerate on every restart and all users are logged out.
// Path defaults to /keys (bind-mounted in docker-compose); skipped in dev when
// the path is unavailable so the developer keeps ephemeral in-memory keys.
var dataProtectionKeysPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH") ?? "/keys";
if (Directory.Exists(dataProtectionKeysPath) || builder.Environment.IsProduction())
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("DailyBread");
    Console.WriteLine($"DataProtection keys persisted to: {dataProtectionKeysPath}");
}

// Build PostgreSQL connection string from Railway environment variables
var connectionString = GetPostgresConnectionString(builder.Configuration);
Console.WriteLine($"Database configured: PostgreSQL");

// =============================================================================
// Query Monitoring for Development - measures query counts per operation
// Enable in appsettings.Development.json: "QueryMonitoring:Enabled": true
// =============================================================================
var enableQueryMonitoring = builder.Environment.IsDevelopment() && 
    builder.Configuration.GetValue<bool>("QueryMonitoring:Enabled");

if (enableQueryMonitoring)
{
    Console.WriteLine("Query monitoring ENABLED for development");
    builder.Services.AddSingleton<IQueryMonitoringService, QueryMonitoringService>();
    builder.Services.AddSingleton<QueryCountingInterceptor>();
}
else
{
    // Register a no-op implementation when monitoring is disabled
    builder.Services.AddSingleton<IQueryMonitoringService, NullQueryMonitoringService>();
}

// Configure DbContext for PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    
    // Add query counting interceptor if monitoring is enabled
    if (enableQueryMonitoring)
    {
        var interceptor = sp.GetService<QueryCountingInterceptor>();
        if (interceptor != null)
        {
            options.AddInterceptors(interceptor);
        }
    }
    
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    });
});

// Add DbContext factory for Blazor Server (avoids concurrent access issues)
builder.Services.AddDbContextFactory<ApplicationDbContext>((sp, options) =>
{
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    
    // Add query counting interceptor if monitoring is enabled
    if (enableQueryMonitoring)
    {
        var interceptor = sp.GetService<QueryCountingInterceptor>();
        if (interceptor != null)
        {
            options.AddInterceptors(interceptor);
        }
    }
    
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

// Authentik OIDC for parents (you + wife). Wired as an external login provider
// that feeds ASP.NET Identity, so SSO logins and local password logins both end
// up holding the same .DailyBread.Auth cookie. The kid keeps local password login
// and never touches this path. Disabled automatically if env vars are absent.
var oidcAuthority = Environment.GetEnvironmentVariable("OIDC_AUTHORITY");
var oidcClientId = Environment.GetEnvironmentVariable("OIDC_CLIENT_ID");
var oidcClientSecret = Environment.GetEnvironmentVariable("OIDC_CLIENT_SECRET");
var oidcEnabled = !string.IsNullOrEmpty(oidcAuthority)
    && !string.IsNullOrEmpty(oidcClientId)
    && !string.IsNullOrEmpty(oidcClientSecret);

if (oidcEnabled)
{
    builder.Services.AddAuthentication().AddOpenIdConnect("Authentik", options =>
    {
        options.Authority = oidcAuthority;
        options.ClientId = oidcClientId;
        options.ClientSecret = oidcClientSecret;
        options.ResponseType = "code";
        options.UsePkce = true;

        // Sign into Identity's external scheme so the callback can link/provision
        // the local user and then issue the normal application cookie.
        options.SignInScheme = IdentityConstants.ExternalScheme;

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false; // keep raw claim names: sub, email, groups

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("offline_access");

        // auth.simmserv.org and dailybread.simmserv.org are same-site (eTLD+1
        // simmserv.org), so Lax correlation/nonce cookies survive the round trip.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SameSite = SameSiteMode.Lax;

        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "groups";
    });
    Console.WriteLine($"OIDC (Authentik) enabled: authority={oidcAuthority}");
}
else
{
    Console.WriteLine("OIDC (Authentik) not configured - SSO button hidden, local login only.");
}

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
    
    // Session duration - 60 days with sliding expiration.
    // Each authenticated request slides the window forward, so with near-daily
    // use (e.g. the kid's PWA on his phone) the session effectively never expires.
    options.ExpireTimeSpan = TimeSpan.FromDays(60);
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
    
    // SecurePolicy: force Secure cookies in Production (always HTTPS via the
    // Cloudflare tunnel; UseForwardedHeaders surfaces the original https scheme),
    // but fall back to SameAsRequest in Development so local http://localhost
    // login still works without TLS.
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    
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
builder.Services.AddMemoryCache(); // Required for ChoreScheduleService caching
builder.Services.AddScoped<IDateProvider, SystemDateProvider>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<ModalService>(); // Modal service for root-level modal rendering
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
builder.Services.AddScoped<IAchievementManagementService, AchievementManagementService>();
builder.Services.AddScoped<IChoreChartService, ChoreChartService>();
builder.Services.AddScoped<IChorePlannerService, ChorePlannerService>();
builder.Services.AddScoped<IFamilySettingsService, FamilySettingsService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddHttpClient<INtfyAlertService, NtfyAlertService>();
builder.Services.AddScoped<IWeeklyProgressService, WeeklyProgressService>();
builder.Services.AddScoped<IWeeklyReconciliationService, WeeklyReconciliationService>();
builder.Services.AddScoped<IBiometricAuthService, BiometricAuthService>();
builder.Services.AddScoped<IAppStateService, AppStateService>();
builder.Services.AddScoped<INavigationService, NavigationService>();

// SignalR for real-time notifications
builder.Services.AddSignalR();
builder.Services.AddSingleton<IChoreNotificationService, ChoreNotificationService>();

// Achievement system services (order matters - dependencies first)
builder.Services.AddScoped<IAchievementConditionEvaluator, AchievementConditionEvaluator>();
builder.Services.AddScoped<IAchievementRewardClaimService, AchievementRewardClaimService>();
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

#if DEBUG
// Seed development test data (only when Seed:DevData = true)
// Wrapped in #if DEBUG to exclude from Release builds entirely
if (app.Environment.IsDevelopment() && 
    builder.Configuration.GetValue<bool>("Seed:DevData"))
{
    await DevDataSeeder.SeedDevDataAsync(app.Services, builder.Configuration);
}
#endif

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

// SignalR hub endpoint - must be after UseAuthentication/UseAuthorization
app.MapHub<ChoreHub>("/chorehub");

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

// MapStaticAssets serves fingerprinted assets generated by @Assets[] directive
// MUST allow anonymous access since these are CSS/JS files needed before login
app.MapStaticAssets().AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous(); // Allow anonymous access to Blazor framework - individual pages use [Authorize]

// Map additional Identity endpoints
app.MapAdditionalIdentityEndpoints();

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
            IAntiforgery antiforgery,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<Program> logger) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException ex)
            {
                logger.LogWarning(ex, "Rejected login request with an invalid antiforgery token");
                return Results.BadRequest();
            }

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
        }).AllowAnonymous();

        // Logout POST endpoint
        group.MapPost("/Logout", async (
            HttpContext context,
            IAntiforgery antiforgery,
            SignInManager<ApplicationUser> signInManager,
            ILogger<Program> logger) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException ex)
            {
                logger.LogWarning(ex, "Rejected logout request with an invalid antiforgery token");
                return Results.BadRequest();
            }

            await signInManager.SignOutAsync();
            return Results.LocalRedirect("~/Account/Login");
        });

        // External login (Authentik SSO) - challenge endpoint.
        // Must run as a plain HTTP endpoint, not inside a Blazor interactive
        // circuit, so the OIDC redirect can be issued cleanly.
        group.MapGet("/ExternalLogin", (string? returnUrl, SignInManager<ApplicationUser> signInManager) =>
        {
            var redirectUrl = $"/Account/ExternalLoginCallback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
            // ConfigureExternalAuthenticationProperties stamps the LoginProvider marker
            // that GetExternalLoginInfoAsync() needs to reconstruct the login on callback.
            var props = signInManager.ConfigureExternalAuthenticationProperties("Authentik", redirectUrl);
            return Results.Challenge(props, new[] { "Authentik" });
        }).AllowAnonymous();

        // External login callback - after Authentik authenticates the user and the
        // OIDC middleware establishes the external cookie, link the Authentik
        // identity to the matching local account (by email) and issue the normal
        // application cookie. Access is already restricted to the
        // dailybread-parents group in Authentik, so only parents reach here.
        group.MapGet("/ExternalLoginCallback", async (
            string? returnUrl,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<Program> logger) =>
        {
            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info is null)
            {
                logger.LogWarning("External login callback with no external login info");
                return Results.LocalRedirect("~/Account/Login?error=external");
            }

            var email = info.Principal.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(email))
            {
                logger.LogWarning("External login missing email claim");
                return Results.LocalRedirect("~/Account/Login?error=external");
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("OIDC login for {Email} has no matching local account", email);
                return Results.LocalRedirect("~/Account/Login?error=notauthorized");
            }

            // Link the Authentik identity to the local account if not already linked.
            var logins = await userManager.GetLoginsAsync(user);
            var isAuthentikLinked = logins.Any(l => l.LoginProvider == info.LoginProvider && l.ProviderKey == info.ProviderKey);
            if (!isAuthentikLinked)
            {
                var linkResult = await userManager.AddLoginAsync(user, info);
                if (!linkResult.Succeeded)
                {
                    logger.LogWarning("Failed to link Authentik identity for {Email}", email);
                    return Results.LocalRedirect("~/Account/Login?error=external");
                }

                isAuthentikLinked = true;
            }

            // Roles follow Authentik group membership for Authentik-linked users.
            if (isAuthentikLinked)
            {
                var groups = info.Principal.FindAll("groups").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
                var roleMappings = new[]
                {
                    (Group: "authentik Admins", Role: "Admin"),
                    (Group: "dailybread-parents", Role: "Parent")
                };

                foreach (var (groupName, roleName) in roleMappings)
                {
                    var inGroup = groups.Contains(groupName);
                    var inRole = await userManager.IsInRoleAsync(user, roleName);

                    if (inGroup && !inRole)
                    {
                        var addResult = await userManager.AddToRoleAsync(user, roleName);
                        if (!addResult.Succeeded)
                        {
                            logger.LogError(
                                "Failed to add role {Role} for Authentik-linked user {Email}: {Errors}",
                                roleName,
                                email,
                                string.Join(", ", addResult.Errors.Select(error => error.Description)));
                            return Results.LocalRedirect("~/Account/Login?error=external");
                        }
                    }
                    else if (!inGroup && inRole)
                    {
                        var removeResult = await userManager.RemoveFromRoleAsync(user, roleName);
                        if (!removeResult.Succeeded)
                        {
                            logger.LogError(
                                "Failed to remove role {Role} for Authentik-linked user {Email}: {Errors}",
                                roleName,
                                email,
                                string.Join(", ", removeResult.Errors.Select(error => error.Description)));
                            return Results.LocalRedirect("~/Account/Login?error=external");
                        }
                    }
                }
            }

            // Persistent sign-in so parents stay logged in across the 60-day window.
            await signInManager.SignInAsync(user, isPersistent: true);
            logger.LogInformation("OIDC login: {Email} -> {User}", email, user.UserName);

            var dest = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            return Results.LocalRedirect($"~{dest}");
        }).AllowAnonymous();

        return group;
    }
}
