using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Data.Models;

/// <summary>
/// A refresh token issued to a native client (iOS/macOS app).
/// Only the SHA-256 hash of the token is stored; the raw token exists solely
/// on the client (Keychain). Rotation: each successful refresh revokes the
/// presented token and records the hash of its replacement, so reuse of an
/// already-rotated token is detectable (and revokes the whole family of
/// tokens for that user as a defensive measure).
/// </summary>
[Index(nameof(TokenHash), IsUnique = true)]
[Index(nameof(UserId))]
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user this token belongs to.</summary>
    [Required]
    public string UserId { get; set; } = null!;

    public ApplicationUser? User { get; set; }

    /// <summary>SHA-256 hash (base64url) of the raw refresh token.</summary>
    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = null!;

    /// <summary>Optional device label ("the kid's iPhone") for future management UI.</summary>
    [MaxLength(100)]
    public string? DeviceName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when rotated, revoked by logout, or revoked defensively.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Hash of the token that replaced this one (rotation chain).</summary>
    [MaxLength(64)]
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAtUtc == null && DateTime.UtcNow < ExpiresAtUtc;
}
