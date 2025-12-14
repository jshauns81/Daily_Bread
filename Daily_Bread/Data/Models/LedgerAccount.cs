namespace Daily_Bread.Data.Models;

/// <summary>
/// Represents a ledger account for a child.
/// Each child can have multiple accounts (e.g., Main, Savings).
/// </summary>
public class LedgerAccount
{
    public int Id { get; set; }

    /// <summary>
    /// The child profile this account belongs to.
    /// </summary>
    public int ChildProfileId { get; set; }
    public ChildProfile ChildProfile { get; set; } = null!;

    /// <summary>
    /// Name of the account (e.g., "Main", "Savings").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether this is the default account for new transactions.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this account is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when the account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the account was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    // Navigation properties
    public ICollection<LedgerTransaction> LedgerTransactions { get; set; } = [];
}
