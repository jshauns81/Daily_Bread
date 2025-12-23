namespace Daily_Bread.Data.Models;

/// <summary>
/// Stores a user's Web Push subscription details.
/// Each device/browser the user subscribes from gets its own record.
/// </summary>
public class PushSubscription
{
    public int Id { get; set; }
    
    /// <summary>
    /// The user this subscription belongs to.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    
    /// <summary>
    /// The push endpoint URL provided by the browser's push service.
    /// </summary>
    public required string Endpoint { get; set; }
    
    /// <summary>
    /// The P256DH key for encryption (base64url encoded).
    /// </summary>
    public required string P256dh { get; set; }
    
    /// <summary>
    /// The auth secret for encryption (base64url encoded).
    /// </summary>
    public required string Auth { get; set; }
    
    /// <summary>
    /// Optional device/browser identifier for user's reference.
    /// e.g., "Chrome on iPhone", "Safari on MacBook"
    /// </summary>
    public string? DeviceName { get; set; }
    
    /// <summary>
    /// User agent string when subscription was created.
    /// Helps identify the device type.
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Whether this subscription is currently active.
    /// Set to false if push fails repeatedly (expired subscription).
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When the subscription was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the subscription was last used successfully.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
    
    /// <summary>
    /// Number of consecutive failed push attempts.
    /// Reset to 0 on success, subscription deactivated after threshold.
    /// </summary>
    public int FailedAttempts { get; set; } = 0;
}
