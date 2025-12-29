using Daily_Bread.Data;
using Microsoft.AspNetCore.Identity;

namespace Daily_Bread.Services;

/// <summary>
/// Authentication credentials discriminated by type.
/// </summary>
public abstract class AuthCredential
{
    public bool RememberDevice { get; set; }
}

/// <summary>
/// Password-based authentication credential.
/// </summary>
public class PasswordCredential : AuthCredential
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
}

/// <summary>
/// PIN-based authentication credential (child login).
/// </summary>
public class PinCredential : AuthCredential
{
    public required string Pin { get; set; }
}

/// <summary>
/// Result of an authentication attempt.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? UserFacingMessage { get; set; }
    public UserSummary? User { get; set; }

    public static AuthResult Ok(UserSummary user) => new()
    {
        Success = true,
        User = user
    };

    public static AuthResult Fail(string errorCode, string userMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        UserFacingMessage = userMessage
    };
}

/// <summary>
/// Summary of authenticated user information.
/// </summary>
public class UserSummary
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required List<string> Roles { get; init; }
    public Guid? HouseholdId { get; init; }
}

/// <summary>
/// Centralized authentication service.
/// All login flows must go through this service.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a user with the provided credential.
    /// </summary>
    Task<AuthResult> SignInAsync(AuthCredential credential, CancellationToken ct = default);

    /// <summary>
    /// Signs out the current user.
    /// </summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the currently authenticated user summary.
    /// </summary>
    Task<UserSummary?> GetCurrentUserAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementation of the authentication service.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IKidModeService _kidModeService;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IKidModeService kidModeService,
        IAuditLogService auditLog,
        ILogger<AuthenticationService> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _kidModeService = kidModeService;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<AuthResult> SignInAsync(AuthCredential credential, CancellationToken ct = default)
    {
        return credential switch
        {
            PasswordCredential pwd => await SignInWithPasswordAsync(pwd, ct),
            PinCredential pin => await SignInWithPinAsync(pin, ct),
            _ => AuthResult.Fail("UnsupportedCredential", "Authentication method not supported.")
        };
    }

    private async Task<AuthResult> SignInWithPasswordAsync(PasswordCredential credential, CancellationToken ct)
    {
        // Normalize username
        var normalizedUserName = credential.UserName.Trim();

        // Attempt sign-in with lockout protection
        var result = await _signInManager.PasswordSignInAsync(
            normalizedUserName,
            credential.Password,
            credential.RememberDevice,
            lockoutOnFailure: true); // ✅ LOCKOUT ENABLED

        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(normalizedUserName);
            if (user == null)
            {
                _logger.LogWarning("User {UserName} signed in but could not be found", normalizedUserName);
                return AuthResult.Fail("UnknownError", "An error occurred during sign-in.");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var summary = new UserSummary
            {
                UserId = user.Id,
                UserName = user.UserName ?? "",
                Roles = roles.ToList(),
                HouseholdId = user.HouseholdId
            };

            await _auditLog.LogLoginSuccessAsync(user.Id, "Password", user.HouseholdId);
            _logger.LogInformation("User {UserName} logged in successfully", user.UserName);

            return AuthResult.Ok(summary);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User {UserName} account locked out", normalizedUserName);
            await _auditLog.LogLoginFailureAsync(normalizedUserName, "Password", "LockedOut", null);
            return AuthResult.Fail("LockedOut", "This account has been locked out due to too many failed login attempts. Please try again later.");
        }

        if (result.RequiresTwoFactor)
        {
            await _auditLog.LogLoginFailureAsync(normalizedUserName, "Password", "TwoFactorRequired", null);
            return AuthResult.Fail("TwoFactorRequired", "Two-factor authentication is required but not configured.");
        }

        // Invalid credentials - use consistent message to avoid user enumeration
        _logger.LogWarning("Failed login attempt for {UserName}", normalizedUserName);
        await _auditLog.LogLoginFailureAsync(normalizedUserName, "Password", "InvalidCredentials", null);
        return AuthResult.Fail("InvalidCredentials", "Invalid username or password.");
    }

    private async Task<AuthResult> SignInWithPinAsync(PinCredential credential, CancellationToken ct)
    {
        // Validate PIN format
        if (string.IsNullOrEmpty(credential.Pin) || credential.Pin.Length != 4 || !credential.Pin.All(char.IsDigit))
        {
            await _auditLog.LogLoginFailureAsync("PIN", "PIN", "InvalidFormat", null);
            return AuthResult.Fail("InvalidCredentials", "Invalid PIN format.");
        }

        // Validate PIN and get session
        var session = await _kidModeService.ValidatePinAsync(credential.Pin);
        if (session == null)
        {
            await _auditLog.LogLoginFailureAsync("PIN", "PIN", "InvalidPin", null);
            return AuthResult.Fail("InvalidCredentials", "Invalid PIN. Please try again.");
        }

        // Get user and sign in
        var user = await _userManager.FindByIdAsync(session.UserId);
        if (user == null)
        {
            _logger.LogError("PIN validated for user {UserId} but user not found", session.UserId);
            await _auditLog.LogLoginFailureAsync(session.UserId, "PIN", "UserNotFound", null);
            return AuthResult.Fail("InvalidCredentials", "Account not found. Please contact a parent.");
        }

        // Check if household is active
        if (user.HouseholdId.HasValue && user.Household != null && !user.Household.IsActive)
        {
            await _auditLog.LogLoginFailureAsync(user.UserName ?? user.Id, "PIN", "HouseholdInactive", user.HouseholdId);
            return AuthResult.Fail("HouseholdInactive", "This household is currently inactive.");
        }

        await _signInManager.SignInAsync(user, isPersistent: credential.RememberDevice);

        var roles = await _userManager.GetRolesAsync(user);
        var summary = new UserSummary
        {
            UserId = user.Id,
            UserName = user.UserName ?? session.DisplayName,
            Roles = roles.ToList(),
            HouseholdId = user.HouseholdId
        };

        await _auditLog.LogLoginSuccessAsync(user.Id, "PIN", user.HouseholdId);
        _logger.LogInformation("User {UserName} logged in via PIN", user.UserName);

        return AuthResult.Ok(summary);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User signed out");
    }

    public async Task<UserSummary?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var user = await _userManager.GetUserAsync(_signInManager.Context.User);
        if (user == null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        return new UserSummary
        {
            UserId = user.Id,
            UserName = user.UserName ?? "",
            Roles = roles.ToList(),
            HouseholdId = user.HouseholdId
        };
    }
}
