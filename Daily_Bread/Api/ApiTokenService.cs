using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Daily_Bread.Api;

/// <summary>
/// Issues short-lived access JWTs and long-lived rotating refresh tokens for
/// native clients. Cookies remain the web app's auth; this is the parallel
/// scheme for /api/v1 (see docs/IOS_APP_PLAN.md §1 D2).
/// </summary>
public interface IApiTokenService
{
    /// <summary>Issues a fresh access + refresh token pair for the user.</summary>
    Task<TokenResponse> IssueTokensAsync(UserSummary user, string? deviceName, CancellationToken ct = default);

    /// <summary>
    /// Validates and rotates a refresh token. Returns null if the token is
    /// unknown/expired. Reuse of an already-rotated token revokes every active
    /// token for that user (theft containment) and returns null.
    /// </summary>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes a specific refresh token (logout), if it exists.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes every active refresh token for a user.</summary>
    Task RevokeAllForUserAsync(string userId, CancellationToken ct = default);
}

public static class ApiJwt
{
    public const string Scheme = "Bearer";
    public const string HouseholdClaim = "HouseholdId";

    /// <summary>
    /// Resolves the signing key. Order: Api:Jwt:SigningKey config →
    /// JWT_SIGNING_KEY env → ephemeral random key (with a loud warning:
    /// tokens then die on every restart). Never throws, so adding the API
    /// layer cannot break an existing deployment that hasn't set the key yet.
    /// </summary>
    public static SymmetricSecurityKey ResolveSigningKey(IConfiguration config, ILogger? logger = null)
    {
        var raw = config["Api:Jwt:SigningKey"]
                  ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(raw))
        {
            logger?.LogWarning(
                "No JWT signing key configured (Api:Jwt:SigningKey / JWT_SIGNING_KEY). " +
                "Using an ephemeral key: API tokens will NOT survive an app restart. " +
                "Set JWT_SIGNING_KEY (32+ random bytes, e.g. `openssl rand -base64 48`) for production.");
            return new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48));
        }

        var bytes = Encoding.UTF8.GetBytes(raw);
        if (bytes.Length < 32)
        {
            // Stretch short keys rather than failing: still deterministic per key value.
            bytes = SHA256.HashData(bytes.Concat(Encoding.UTF8.GetBytes("DailyBread")).ToArray());
        }
        return new SymmetricSecurityKey(bytes);
    }

    public static string HashToken(string rawToken)
        => Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public static string NewRawToken()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(48));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public class ApiTokenService : IApiTokenService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<ApiTokenService> _logger;

    public ApiTokenService(
        ApplicationDbContext db,
        IConfiguration config,
        SymmetricSecurityKey signingKey,
        IAuditLogService auditLog,
        ILogger<ApiTokenService> logger)
    {
        _db = db;
        _config = config;
        _signingKey = signingKey;
        _auditLog = auditLog;
        _logger = logger;
    }

    private int AccessTokenMinutes => _config.GetValue("Api:Jwt:AccessTokenMinutes", 15);
    private int RefreshTokenDays => _config.GetValue("Api:Jwt:RefreshTokenDays", 90);
    private string Issuer => _config.GetValue("Api:Jwt:Issuer", "DailyBread")!;
    private string Audience => _config.GetValue("Api:Jwt:Audience", "DailyBread")!;

    public async Task<TokenResponse> IssueTokensAsync(UserSummary user, string? deviceName, CancellationToken ct = default)
    {
        var (accessToken, accessExpires) = CreateAccessToken(user);

        var rawRefresh = ApiJwt.NewRawToken();
        var refreshExpires = DateTime.UtcNow.AddDays(RefreshTokenDays);
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.UserId,
            TokenHash = ApiJwt.HashToken(rawRefresh),
            DeviceName = deviceName,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExpires
        });
        await _db.SaveChangesAsync(ct);

        return new TokenResponse(
            accessToken,
            accessExpires,
            rawRefresh,
            refreshExpires,
            new ApiUserDto(user.UserId, user.UserName, user.Roles, user.HouseholdId));
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = ApiJwt.HashToken(refreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored == null)
        {
            return null;
        }

        if (stored.RevokedAtUtc != null)
        {
            // Reuse of a rotated/revoked token → possible theft. Contain it.
            _logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all tokens", stored.UserId);
            await RevokeAllForUserAsync(stored.UserId, ct);
            return null;
        }

        if (DateTime.UtcNow >= stored.ExpiresAtUtc)
        {
            return null;
        }

        var user = stored.User;
        if (user == null)
        {
            return null;
        }

        var roles = await (
            from ur in _db.UserRoles
            join r in _db.Roles on ur.RoleId equals r.Id
            where ur.UserId == user.Id && r.Name != null
            select r.Name!).ToListAsync(ct);

        var summary = new UserSummary
        {
            UserId = user.Id,
            UserName = user.UserName ?? "",
            Roles = roles,
            HouseholdId = user.HouseholdId
        };

        // Rotate: revoke the presented token, record its replacement.
        var response = await IssueTokensAsync(summary, stored.DeviceName, ct);
        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = ApiJwt.HashToken(response.RefreshToken);
        await _db.SaveChangesAsync(ct);

        return response;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = ApiJwt.HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAtUtc: null })
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllForUserAsync(string userId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    private (string Token, DateTime ExpiresAtUtc) CreateAccessToken(UserSummary user)
    {
        var expires = DateTime.UtcNow.AddMinutes(AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId),
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (user.HouseholdId.HasValue)
        {
            claims.Add(new Claim(ApiJwt.HouseholdClaim, user.HouseholdId.Value.ToString()));
        }

        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
