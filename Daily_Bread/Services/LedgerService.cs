using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Service for managing ledger transactions tied to ledger accounts.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Creates or updates the ledger transaction for a chore log based on its status.
    /// </summary>
    Task<ServiceResult> ReconcileChoreLogTransactionAsync(int choreLogId);

    /// <summary>
    /// Gets the current balance for a ledger account.
    /// </summary>
    Task<decimal> GetAccountBalanceAsync(int ledgerAccountId);

    /// <summary>
    /// Gets the current balance for a user (sum of all their accounts) - legacy method.
    /// </summary>
    Task<decimal> GetUserBalanceAsync(string userId);

    /// <summary>
    /// Gets transactions for a ledger account within a date range.
    /// </summary>
    Task<List<LedgerTransaction>> GetAccountTransactionsAsync(int ledgerAccountId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>
    /// Gets transactions for a user within a date range - legacy method.
    /// </summary>
    Task<List<LedgerTransaction>> GetUserTransactionsAsync(string userId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>
    /// Gets the ledger transaction for a specific chore log.
    /// </summary>
    Task<LedgerTransaction?> GetTransactionForChoreLogAsync(int choreLogId);

    /// <summary>
    /// Gets transaction statistics for a ledger account.
    /// </summary>
    Task<TransactionStats> GetAccountTransactionStatsAsync(int ledgerAccountId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>
    /// Gets transaction statistics for a user - legacy method.
    /// </summary>
    Task<TransactionStats> GetUserTransactionStatsAsync(string userId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>
    /// Transfers money between two ledger accounts.
    /// Creates paired debit/credit transactions with same TransferGroupId.
    /// </summary>
    Task<ServiceResult> TransferAsync(int fromAccountId, int toAccountId, decimal amount, string reason);
}

/// <summary>
/// Statistics for transactions.
/// </summary>
public class TransactionStats
{
    public decimal TotalEarnings { get; init; }
    public decimal TotalDeductions { get; init; }
    public decimal TotalBonuses { get; init; }
    public decimal TotalPenalties { get; init; }
    public decimal TotalPayouts { get; init; }
    public decimal TotalAdjustments { get; init; }
    public decimal TotalTransfersIn { get; init; }
    public decimal TotalTransfersOut { get; init; }
    public decimal NetBalance { get; init; }
    public int TransactionCount { get; init; }
}

public class LedgerService : ILedgerService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IFamilySettingsService _familySettingsService;

    public LedgerService(
        IDbContextFactory<ApplicationDbContext> contextFactory, 
        IDateProvider dateProvider,
        IFamilySettingsService familySettingsService)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _familySettingsService = familySettingsService;
    }

    public async Task<ServiceResult> ReconcileChoreLogTransactionAsync(int choreLogId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var choreLog = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Include(c => c.LedgerTransaction)
            .FirstOrDefaultAsync(c => c.Id == choreLogId);

        if (choreLog == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }

        var choreDefinition = choreLog.ChoreDefinition;
        var assignedUserId = choreDefinition.AssignedUserId;

        if (string.IsNullOrEmpty(assignedUserId))
        {
            // No user assigned, no transaction needed
            return ServiceResult.Ok();
        }

        // Find the child's default ledger account
        var childProfile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == assignedUserId);

        if (childProfile == null)
        {
            return ServiceResult.Fail("Child profile not found for assigned user.");
        }

        var defaultAccount = childProfile.LedgerAccounts
            .FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? childProfile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            return ServiceResult.Fail("No active ledger account found for child.");
        }

        var existingTransaction = choreLog.LedgerTransaction;
        
        // Calculate amount - may need weekly context for diminishing returns
        var (needsTransaction, amount, transactionType, description) = await DetermineTransactionAsync(
            context, choreLog, choreDefinition);

        if (!needsTransaction)
        {
            if (existingTransaction != null)
            {
                context.LedgerTransactions.Remove(existingTransaction);
                await context.SaveChangesAsync();
            }
            return ServiceResult.Ok();
        }

        if (existingTransaction != null)
        {
            if (existingTransaction.Amount != amount || existingTransaction.Type != transactionType)
            {
                existingTransaction.Amount = amount;
                existingTransaction.Type = transactionType;
                existingTransaction.Description = description;
                existingTransaction.LedgerAccountId = defaultAccount.Id;
                existingTransaction.ModifiedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            var transaction = new LedgerTransaction
            {
                LedgerAccountId = defaultAccount.Id,
                ChoreLogId = choreLogId,
                UserId = assignedUserId,
                Amount = amount,
                Type = transactionType,
                Description = description,
                TransactionDate = choreLog.Date,
                CreatedAt = DateTime.UtcNow
            };

            context.LedgerTransactions.Add(transaction);
            await context.SaveChangesAsync();
        }

        return ServiceResult.Ok();
    }

    public async Task<decimal> GetAccountBalanceAsync(int ledgerAccountId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions
            .Where(t => t.LedgerAccountId == ledgerAccountId)
            .SumAsync(t => t.Amount);
    }

    public async Task<decimal> GetUserBalanceAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Sum balance across all user's accounts
        var accountIds = await context.ChildProfiles
            .Where(p => p.UserId == userId)
            .SelectMany(p => p.LedgerAccounts)
            .Where(a => a.IsActive)
            .Select(a => a.Id)
            .ToListAsync();

        if (!accountIds.Any())
        {
            // Fall back to legacy query if no profile exists
            return await context.LedgerTransactions
                .Where(t => t.UserId == userId)
                .SumAsync(t => t.Amount);
        }

        return await context.LedgerTransactions
            .Where(t => accountIds.Contains(t.LedgerAccountId))
            .SumAsync(t => t.Amount);
    }

    public async Task<List<LedgerTransaction>> GetAccountTransactionsAsync(
        int ledgerAccountId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.LedgerTransactions
            .Include(t => t.ChoreLog)
            .ThenInclude(c => c!.ChoreDefinition)
            .Include(t => t.LedgerAccount)
            .Where(t => t.LedgerAccountId == ledgerAccountId);

        if (fromDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= toDate.Value);
        }

        return await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<LedgerTransaction>> GetUserTransactionsAsync(
        string userId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.LedgerTransactions
            .Include(t => t.ChoreLog)
            .ThenInclude(c => c!.ChoreDefinition)
            .Include(t => t.LedgerAccount)
            .Where(t => t.UserId == userId);

        if (fromDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= toDate.Value);
        }

        return await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<LedgerTransaction?> GetTransactionForChoreLogAsync(int choreLogId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.LedgerTransactions
            .Include(t => t.ChoreLog)
            .ThenInclude(c => c!.ChoreDefinition)
            .Include(t => t.LedgerAccount)
            .FirstOrDefaultAsync(t => t.ChoreLogId == choreLogId);
    }

    public async Task<TransactionStats> GetAccountTransactionStatsAsync(
        int ledgerAccountId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.LedgerTransactions
            .Where(t => t.LedgerAccountId == ledgerAccountId);

        if (fromDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= toDate.Value);
        }

        var transactions = await query.ToListAsync();
        return CalculateStats(transactions);
    }

    public async Task<TransactionStats> GetUserTransactionStatsAsync(
        string userId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.LedgerTransactions.Where(t => t.UserId == userId);

        if (fromDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= toDate.Value);
        }

        var transactions = await query.ToListAsync();
        return CalculateStats(transactions);
    }

    public async Task<ServiceResult> TransferAsync(
        int fromAccountId,
        int toAccountId,
        decimal amount,
        string reason)
    {
        if (amount <= 0)
        {
            return ServiceResult.Fail("Transfer amount must be positive.");
        }

        if (fromAccountId == toAccountId)
        {
            return ServiceResult.Fail("Cannot transfer to the same account.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var fromAccount = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == fromAccountId);

        var toAccount = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == toAccountId);

        if (fromAccount == null || toAccount == null)
        {
            return ServiceResult.Fail("One or both accounts not found.");
        }

        // Check balance
        var fromBalance = await context.LedgerTransactions
            .Where(t => t.LedgerAccountId == fromAccountId)
            .SumAsync(t => t.Amount);
            
        if (fromBalance < amount)
        {
            return ServiceResult.Fail($"Insufficient balance. Available: ${fromBalance:F2}");
        }

        var transferGroupId = Guid.NewGuid();
        var today = _dateProvider.Today;
        var now = DateTime.UtcNow;

        var fromUserName = fromAccount.ChildProfile.DisplayName;
        var toUserName = toAccount.ChildProfile.DisplayName;

        // Debit transaction (from account)
        var debitTransaction = new LedgerTransaction
        {
            LedgerAccountId = fromAccountId,
            UserId = fromAccount.ChildProfile.UserId,
            Amount = -amount,
            Type = TransactionType.Transfer,
            Description = $"Transfer to {toUserName}'s {toAccount.Name}: {reason}",
            TransactionDate = today,
            TransferGroupId = transferGroupId,
            CreatedAt = now
        };

        // Credit transaction (to account)
        var creditTransaction = new LedgerTransaction
        {
            LedgerAccountId = toAccountId,
            UserId = toAccount.ChildProfile.UserId,
            Amount = amount,
            Type = TransactionType.Transfer,
            Description = $"Transfer from {fromUserName}'s {fromAccount.Name}: {reason}",
            TransactionDate = today,
            TransferGroupId = transferGroupId,
            CreatedAt = now
        };

        context.LedgerTransactions.AddRange(debitTransaction, creditTransaction);
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    private static TransactionStats CalculateStats(List<LedgerTransaction> transactions)
    {
        return new TransactionStats
        {
            TotalEarnings = transactions
                .Where(t => t.Type == TransactionType.ChoreEarning && t.Amount > 0)
                .Sum(t => t.Amount),
            TotalDeductions = transactions
                .Where(t => t.Type == TransactionType.ChoreDeduction)
                .Sum(t => Math.Abs(t.Amount)),
            TotalBonuses = transactions
                .Where(t => t.Type == TransactionType.Bonus)
                .Sum(t => t.Amount),
            TotalPenalties = transactions
                .Where(t => t.Type == TransactionType.Penalty)
                .Sum(t => Math.Abs(t.Amount)),
            TotalPayouts = transactions
                .Where(t => t.Type == TransactionType.Payout)
                .Sum(t => Math.Abs(t.Amount)),
            TotalAdjustments = transactions
                .Where(t => t.Type == TransactionType.Adjustment)
                .Sum(t => t.Amount),
            TotalTransfersIn = transactions
                .Where(t => t.Type == TransactionType.Transfer && t.Amount > 0)
                .Sum(t => t.Amount),
            TotalTransfersOut = transactions
                .Where(t => t.Type == TransactionType.Transfer && t.Amount < 0)
                .Sum(t => Math.Abs(t.Amount)),
            NetBalance = transactions.Sum(t => t.Amount),
            TransactionCount = transactions.Count
        };
    }

    private static (bool NeedsTransaction, decimal Amount, TransactionType Type, string Description) DetermineTransaction(
        ChoreLog choreLog,
        ChoreDefinition choreDefinition)
    {
        return choreLog.Status switch
        {
            // Approved earns the EarnValue (if any)
            ChoreStatus.Approved when choreDefinition.EarnValue > 0 => (
                true,
                choreDefinition.EarnValue,
                TransactionType.ChoreEarning,
                $"Completed: {choreDefinition.Name}"
            ),
            
            // Approved but no earn value (expectation chore done) - no transaction
            ChoreStatus.Approved => (false, 0, TransactionType.ChoreEarning, string.Empty),

            // Completed (waiting approval) - no transaction until approved
            // Money only moves when fully approved
            ChoreStatus.Completed => (false, 0, TransactionType.ChoreEarning, string.Empty),

            // Missed deducts the PenaltyValue (if any)
            ChoreStatus.Missed when choreDefinition.PenaltyValue > 0 => (
                true,
                -choreDefinition.PenaltyValue,
                TransactionType.ChoreDeduction,
                $"Missed: {choreDefinition.Name}"
            ),
            
            // Missed but no penalty - no transaction
            ChoreStatus.Missed => (false, 0, TransactionType.ChoreDeduction, string.Empty),

            // Pending, Skipped, Help have no financial impact
            ChoreStatus.Pending => (false, 0, TransactionType.ChoreEarning, string.Empty),
            ChoreStatus.Skipped => (false, 0, TransactionType.ChoreEarning, string.Empty),
            ChoreStatus.Help => (false, 0, TransactionType.ChoreEarning, string.Empty),

            _ => (false, 0, TransactionType.ChoreEarning, string.Empty)
        };
    }

    private async Task<(bool NeedsTransaction, decimal Amount, TransactionType Type, string Description)> DetermineTransactionAsync(
        ApplicationDbContext context,
        ChoreLog choreLog,
        ChoreDefinition choreDefinition)
    {
        // Non-approved statuses - no transaction
        if (choreLog.Status != ChoreStatus.Approved && choreLog.Status != ChoreStatus.Missed)
        {
            return (false, 0, TransactionType.ChoreEarning, string.Empty);
        }
        
        // Missed chores - apply penalty if any
        if (choreLog.Status == ChoreStatus.Missed)
        {
            if (choreDefinition.PenaltyValue > 0)
            {
                return (true, -choreDefinition.PenaltyValue, TransactionType.ChoreDeduction, 
                    $"Missed: {choreDefinition.Name}");
            }
            return (false, 0, TransactionType.ChoreDeduction, string.Empty);
        }
        
        // Approved status - calculate earnings
        if (choreDefinition.EarnValue <= 0)
        {
            // Expectation chore - no earnings
            return (false, 0, TransactionType.ChoreEarning, string.Empty);
        }
        
        // For weekly frequency chores with diminishing returns
        if (choreDefinition.ScheduleType == ChoreScheduleType.WeeklyFrequency)
        {
            var earnAmount = await CalculateWeeklyChoreEarningAsync(context, choreLog, choreDefinition);
            
            if (earnAmount <= 0)
            {
                return (false, 0, TransactionType.ChoreEarning, string.Empty);
            }
            
            // Determine if this is a bonus completion (beyond quota)
            var weekStart = await _familySettingsService.GetWeekStartForDateAsync(choreLog.Date);
            var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(choreLog.Date);
            
            var completionsBefore = await context.ChoreLogs
                .Where(l => l.ChoreDefinitionId == choreDefinition.Id)
                .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
                .Where(l => l.Status == ChoreStatus.Approved)
                .Where(l => l.Id < choreLog.Id) // Only logs BEFORE this one
                .CountAsync();
            
            var isBonus = completionsBefore >= choreDefinition.WeeklyTargetCount;
            
            var description = isBonus 
                ? $"Bonus: {choreDefinition.Name} (+{completionsBefore - choreDefinition.WeeklyTargetCount + 1} extra)"
                : $"Completed: {choreDefinition.Name} ({completionsBefore + 1}/{choreDefinition.WeeklyTargetCount})";
                
            return (true, earnAmount, TransactionType.ChoreEarning, description);
        }
        
        // Standard daily chore
        return (true, choreDefinition.EarnValue, TransactionType.ChoreEarning, 
            $"Completed: {choreDefinition.Name}");
    }

    /// <summary>
    /// Calculates the earning amount for a weekly chore completion,
    /// applying diminishing returns for bonus completions beyond quota.
    /// </summary>
    private async Task<decimal> CalculateWeeklyChoreEarningAsync(
        ApplicationDbContext context,
        ChoreLog choreLog,
        ChoreDefinition choreDefinition)
    {
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(choreLog.Date);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(choreLog.Date);
        
        // Count approved completions BEFORE this one in the week
        var completionsBefore = await context.ChoreLogs
            .Where(l => l.ChoreDefinitionId == choreDefinition.Id)
            .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
            .Where(l => l.Status == ChoreStatus.Approved)
            .Where(l => l.Id < choreLog.Id) // Only logs with lower ID (created before)
            .CountAsync();
        
        // If under quota, earn full value
        if (completionsBefore < choreDefinition.WeeklyTargetCount)
        {
            return choreDefinition.EarnValue;
        }
        
        // At or over quota - check if repeatable
        if (!choreDefinition.IsRepeatable)
        {
            // Not repeatable and quota met - no additional earnings
            return 0;
        }
        
        // Repeatable: diminishing returns
        // Bonus completions: 50% → 25% → 12.5% → ...
        var bonusCompletions = completionsBefore - choreDefinition.WeeklyTargetCount;
        var multiplier = Math.Pow(0.5, bonusCompletions + 1);
        return Math.Round(choreDefinition.EarnValue * (decimal)multiplier, 2);
    }
}
