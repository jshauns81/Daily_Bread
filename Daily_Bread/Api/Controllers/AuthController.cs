using Daily_Bread.Data;
using Microsoft.EntityFrameworkCore;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Token auth for native clients (iOS/macOS). Web auth stays cookie-based and
/// untouched: this controller never issues cookies
/// (CheckPasswordSignInAsync, not PasswordSignInAsync).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IApiTokenService _tokenService;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<AuthController> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IApiTokenService tokenService,
        IAuditLogService auditLog,
        ILogger<AuthController> logger,
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _auditLog = auditLog;
        _logger = logger;
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
    }

    /// <summary>Username/password login → access + refresh token pair.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized(new ApiError("InvalidCredentials", "Invalid username or password."));
        }

        var user = await _userManager.FindByNameAsync(request.UserName.Trim());
        if (user == null)
        {
            // Same message as a bad password: no user enumeration.
            await _auditLog.LogLoginFailureAsync(request.UserName, "ApiPassword", "InvalidCredentials", null);
            return Unauthorized(new ApiError("InvalidCredentials", "Invalid username or password."));
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await _auditLog.LogLoginFailureAsync(request.UserName, "ApiPassword", "LockedOut", user.HouseholdId);
            return Unauthorized(new ApiError("LockedOut",
                "This account has been locked out due to too many failed login attempts. Please try again later."));
        }

        if (!result.Succeeded)
        {
            await _auditLog.LogLoginFailureAsync(request.UserName, "ApiPassword", "InvalidCredentials", user.HouseholdId);
            return Unauthorized(new ApiError("InvalidCredentials", "Invalid username or password."));
        }

        var roles = await _userManager.GetRolesAsync(user);
        var summary = new UserSummary
        {
            UserId = user.Id,
            UserName = user.UserName ?? "",
            Roles = roles.ToList(),
            HouseholdId = user.HouseholdId
        };

        var deviceName = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
            ? ua[..Math.Min(ua.Length, 100)]
            : null;

        var tokens = await _tokenService.IssueTokensAsync(summary, deviceName, ct);
        await _auditLog.LogLoginSuccessAsync(user.Id, "ApiPassword", user.HouseholdId);
        _logger.LogInformation("API login for {UserName}", user.UserName);

        return Ok(tokens);
    }

    /// <summary>Rotates a refresh token for a new token pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized(new ApiError("InvalidToken", "Session expired. Please sign in again."));
        }

        var tokens = await _tokenService.RefreshAsync(request.RefreshToken, ct);
        if (tokens == null)
        {
            return Unauthorized(new ApiError("InvalidToken", "Session expired. Please sign in again."));
        }

        return Ok(tokens);
    }

    /// <summary>Revokes the presented refresh token (or all of the caller's tokens).</summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _tokenService.RevokeAsync(request.RefreshToken, ct);
        }
        else
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId != null)
            {
                await _tokenService.RevokeAllForUserAsync(userId, ct);
            }
        }
        return NoContent();
    }

    /// <summary>Returns the authenticated user's summary (token sanity check).</summary>
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ApiUserDto>> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);

        // Age tier comes from the child's profile birthdate against family "today".
        DateOnly? birthDate;
        await using (var db = await _contextFactory.CreateDbContextAsync())
        {
            birthDate = await db.ChildProfiles
                .Where(p => p.UserId == user.Id)
                .Select(p => p.BirthDate)
                .FirstOrDefaultAsync();
        }
        var ageTier = AgeTiers.Tier(birthDate, _dateProvider.Today);

        return Ok(new ApiUserDto(user.Id, user.UserName ?? "", roles.ToList(), user.HouseholdId, ageTier));
    }
}
