namespace Daily_Bread.Data.Models;

/// <summary>
/// Global family settings for the chore system.
/// Currently supports a single family - will need refactoring for multi-family support.
/// </summary>
public class FamilySettings
{
    public int Id { get; set; }

    /// <summary>
    /// Flat penalty amount for each missed daily expectation chore.
    /// Applied uniformly to all missed expectations regardless of individual chore settings.
    /// Default: $0.10
    /// </summary>
    public decimal DailyExpectationPenalty { get; set; } = 0.10m;

    /// <summary>
    /// Penalty percentage for weekly chores not completed by end of week.
    /// Applied as a percentage of the chore's EarnValue.
    /// Default: 10% (0.10)
    /// Example: $6 chore not done = $0.60 penalty
    /// </summary>
    public decimal WeeklyIncompletePenaltyPercent { get; set; } = 0.10m;

    /// <summary>
    /// The day that starts a new week for chore tracking.
    /// Default: Monday
    /// </summary>
    public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;

    /// <summary>
    /// Minimum balance required before a child can request a cash out.
    /// Default: $10.00
    /// </summary>
    public decimal CashOutThreshold { get; set; } = 10.00m;

    /// <summary>
    /// Whether to show confetti animation when all daily chores are completed.
    /// Default: true
    /// </summary>
    public bool EnableConfetti { get; set; } = true;

    /// <summary>
    /// Whether to track and display streak counts.
    /// Default: true
    /// </summary>
    public bool EnableStreaks { get; set; } = true;
    
    // ============================================
    // Push Notification Settings (VAPID)
    // ============================================
    
    /// <summary>
    /// VAPID public key for Web Push notifications.
    /// Generated once and stored. Used by browsers to verify push messages.
    /// </summary>
    public string? VapidPublicKey { get; set; }
    
    /// <summary>
    /// VAPID private key for Web Push notifications.
    /// Generated once and stored. Used by server to sign push messages.
    /// Keep this secret!
    /// </summary>
    public string? VapidPrivateKey { get; set; }
    
    /// <summary>
    /// VAPID subject (usually mailto: email or https: URL).
    /// Identifies who is sending push notifications.
    /// </summary>
    public string? VapidSubject { get; set; }
    
    /// <summary>
    /// Whether push notifications are enabled for the family.
    /// Default: true
    /// </summary>
    public bool EnablePushNotifications { get; set; } = true;

    /// <summary>
    /// Timestamp when settings were created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when settings were last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
