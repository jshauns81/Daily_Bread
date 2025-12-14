namespace Daily_Bread.Data.Models;

/// <summary>
/// Records financial transactions (earnings/deductions) tied to a ledger account.
/// One LedgerTransaction per ChoreLog (for chore-related transactions).
/// </summary>
public class LedgerTransaction
{
    public int Id { get; set; }

    /// <summary>
    /// The ledger account this transaction belongs to.
    /// </summary>
    public int LedgerAccountId { get; set; }
    public LedgerAccount LedgerAccount { get; set; } = null!;

    /// <summary>
    /// The chore log this transaction is tied to. Null for non-chore transactions (bonus, payout, etc.).
    /// </summary>
    public int? ChoreLogId { get; set; }
    public ChoreLog? ChoreLog { get; set; }

    /// <summary>
    /// The user this transaction belongs to (kept for backwards compatibility and quick queries).
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Groups related transactions together (e.g., transfer debit/credit pair).
    /// </summary>
    public Guid? TransferGroupId { get; set; }

    /// <summary>
    /// Transaction amount. Positive for earnings, negative for deductions/payouts.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Type of transaction for reporting purposes.
    /// </summary>
    public TransactionType Type { get; set; }

    /// <summary>
    /// Description of the transaction.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The date this transaction applies to.
    /// </summary>
    public DateOnly TransactionDate { get; set; }

    /// <summary>
    /// Timestamp when the transaction was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the transaction was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Type of ledger transaction.
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// Earned from completing a chore.
    /// </summary>
    ChoreEarning = 0,

    /// <summary>
    /// Deducted for missing a chore.
    /// </summary>
    ChoreDeduction = 1,

    /// <summary>
    /// Bonus awarded by a parent.
    /// </summary>
    Bonus = 2,

    /// <summary>
    /// Penalty applied by a parent.
    /// </summary>
    Penalty = 3,

    /// <summary>
    /// Adjustment made by a parent.
    /// </summary>
    Adjustment = 4,

    /// <summary>
    /// Cash out / payout to child.
    /// </summary>
    Payout = 5,

    /// <summary>
    /// Transfer between accounts.
    /// </summary>
    Transfer = 6
}
