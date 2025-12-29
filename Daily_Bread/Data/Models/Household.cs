namespace Daily_Bread.Data.Models;

/// <summary>
/// Represents a household/family unit in the system.
/// All users belong to exactly one household, ensuring data isolation.
/// </summary>
public class Household
{
    public Guid Id { get; set; }

    /// <summary>
    /// Display name for the household (e.g., "Smith Family").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether this household is active.
    /// Inactive households cannot log in.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when the household was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the household was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
