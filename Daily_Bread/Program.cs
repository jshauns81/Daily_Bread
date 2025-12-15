using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Daily_Bread.Components;
using Daily_Bread.Data;
using Daily_Bread.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add EF Core with automatic provider selection (PostgreSQL or SQLite)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionString) && 
        (connectionString.Contains("Host=") || connectionString.Contains("Server=")))
    {
        // PostgreSQL connection string detected (for production/Render)
        // Test
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
    // Development-friendly password requirements
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

var app = builder.Build();

// Seed roles and users on startup
await SeedData.InitializeAsync(app.Services, app.Configuration);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
