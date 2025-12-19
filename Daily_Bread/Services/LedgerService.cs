using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Service for managing ledger transactions tied to ledger accounts.
/// This service handles ONLY money operations - status coordination belongs in TrackerService.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Creates or updates the ledger transaction for a chore log based on its status.
    /// Uses its own DbContext - suitable for standalone reconciliation.
    /// </summary>
    Task<ServiceResult> ReconcileChoreLogTransactionAsync(int choreLogId);
    
    /// <summary>
    /// Reconciles ledger transaction using an existing DbContext.
    /// Use this when you need to include reconciliation in an existing transaction.
    /// CALLER is responsible for SaveChanges and transaction management.
    /// </summary>
    Task<ReconcileResult> ReconcileChoreLogTransactionAsync(ApplicationDbContext context, ChoreLog choreLog);

    /// <summary>
    /// Gets the current balance for a ledger account.
    /// </summary>
    Task<decimal> GetAccountBalanceAsync(int ledgerAccountId);

    /// <summary>
    /// Gets the current balance for a user (sum of all their accounts).
    /// </summary>
    Task<decimal> GetUserBalanceAsync(string userId);

    /// <summary>
    /// Gets transactions for a ledger account within a date range.
    /// </summary>
    Task<List<LedgerTransaction>> GetAccountTransactionsAsync(int ledgerAccountId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>
    /// Gets transactions for a user within a date range.
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
    /// Gets transaction statistics for a user.
    /// </summary>
    Task<TransactionStats> GetUserTransactionStatsAsync(string userId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>
    /// Transfers money between two ledger accounts.
    /// </summary>
    Task<ServiceResult> TransferAsync(int fromAccountId, int toAccountId, decimal amount, string reason);
}

/// <summary>
/// Result of a ledger reconciliation operation.
/// </summary>
public class ReconcileResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public LedgerTransaction? Transaction { get; init; }
    public decimal? Amount { get; init; }
    public bool TransactionCreated { get; init; }
    public bool TransactionUpdated { get; init; }
    public bool TransactionRemoved { get; init; }
    
    public static ReconcileResult Ok(LedgerTransaction? transaction = null, bool created = false, bool updated = false, bool removed = false)
        => new() { Success = true, Transaction = transaction, Amount = transaction?.Amount, TransactionCreated = created, TransactionUpdated = updated, TransactionRemoved = removed };
    public static ReconcileResult Fail(string message) => new() { Success = false, ErrorMessage = message };
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
    private readonly ILogger<LedgerService> _logger;

    public LedgerService(
        IDbContextFactory<ApplicationDbContext> contextFactory, 
        IDateProvider dateProvider,
        IFamilySettingsService familySettingsService,
        ILogger<LedgerService> logger)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _familySettingsService = familySettingsService;
        _logger = logger;
    }

    /// <summary>
    /// Standalone reconciliation - creates its own context.
    /// </summary>
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

        var result = await ReconcileChoreLogTransactionAsync(context, choreLog);
        
        if (!result.Success)
        {
            return ServiceResult.Fail(result.ErrorMessage!);
        }
        
        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Reconciles using an existing context. Caller manages transaction and SaveChanges.
    /// </summary>
    public async Task<ReconcileResult> ReconcileChoreLogTransactionAsync(ApplicationDbContext context, ChoreLog choreLog)
    {
        var choreDefinition = choreLog.ChoreDefinition;
        if (choreDefinition == null)
        {
            await context.Entry(choreLog).Reference(c => c.ChoreDefinition).LoadAsync();
            choreDefinition = choreLog.ChoreDefinition!;
        }
        
        var assignedUserId = choreDefinition.AssignedUserId;

        if (string.IsNullOrEmpty(assignedUserId))
        {
            return ReconcileResult.Ok(); // No user assigned, no transaction needed
        }

        // Ensure LedgerTransaction is loaded
        if (!context.Entry(choreLog).Reference(c => c.LedgerTransaction).IsLoaded)
        {
            await context.Entry(choreLog).Reference(c => c.LedgerTransaction).LoadAsync();
        }

        var childProfile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == assignedUserId);

        if (childProfile == null)
        {
            return ReconcileResult.Fail("Child profile not found for assigned user.");
        }

        var defaultAccount = childProfile.LedgerAccounts
            .FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? childProfile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            return ReconcileResult.Fail("No active ledger account found for child.");
        }

        var existingTransaction = choreLog.LedgerTransaction;
        var (needsTransaction, amount, transactionType, description) = 
            await DetermineTransactionAsync(context, choreLog, choreDefinition);

        if (!needsTransaction)
        {
            if (existingTransaction != null)
            {
                _logger.LogInformation(
                    "Removing ledger transaction {TransactionId} for ChoreLog {ChoreLogId}",
                    existingTransaction.Id, choreLog.Id);
                context.LedgerTransactions.Remove(existingTransaction);
                return ReconcileResult.Ok(removed: true);
            }
            return ReconcileResult.Ok();
        }

        if (existingTransaction != null)
        {
            if (existingTransaction.Amount != amount || existingTransaction.Type != transactionType)
            {
                _logger.LogInformation(
                    "Updating ledger transaction {TransactionId}: Amount {OldAmount} -> {NewAmount}",
                    existingTransaction.Id, existingTransaction.Amount, amount);
                    
                existingTransaction.Amount = amount;
                existingTransaction.Type = transactionType;
                existingTransaction.Description = description;
                existingTransaction.LedgerAccountId = defaultAccount.Id;
                existingTransaction.ModifiedAt = DateTime.UtcNow;
                existingTransaction.Version++; // Manual increment for concurrency
                return ReconcileResult.Ok(existingTransaction, updated: true);
            }
            return ReconcileResult.Ok(existingTransaction);
        }
        
        // Create new transaction
        var newTransaction = new LedgerTransaction
        {
            LedgerAccountId = defaultAccount.Id,
            ChoreLogId = choreLog.Id,
            UserId = assignedUserId,
            Amount = amount,
            Type = transactionType,
            Description = description,
            TransactionDate = choreLog.Date,
            CreatedAt = DateTime.UtcNow
        };

        context.LedgerTransactions.Add(newTransaction);
        
        _logger.LogInformation(
            "Creating ledger transaction for ChoreLog {ChoreLogId}: Amount {Amount}, Type {Type}",
            choreLog.Id, amount, transactionType);
            
        return ReconcileResult.Ok(newTransaction, created: true);
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
        
        var accountIds = await context.ChildProfiles
            .Where(p => p.UserId == userId)
            .SelectMany(p => p.LedgerAccounts)
            .Where(a => a.IsActive)
            .Select(a => a.Id)
            .ToListAsync();

        if (!accountIds.Any())
        {
            return await context.LedgerTransactions
                .Where(t => t.UserId == userId)
                .SumAsync(t => t.Amount);
        }

        return await context.LedgerTransactions
            .Where(t => accountIds.Contains(t.LedgerAccountId))
            .SumAsync(t => t.Amount);
    }

    public async Task<List<LedgerTransaction>> GetAccountTransactionsAsync(
        int ledgerAccountId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.LedgerTransactions
            .Include(t => t.ChoreLog)
            .ThenInclude(c => c!.ChoreDefinition)
            .Include(t => t.LedgerAccount)
            .Where(t => t.LedgerAccountId == ledgerAccountId);

        if (fromDate.HasValue) query = query.Where(t => t.TransactionDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(t => t.TransactionDate <= toDate.Value);

        return await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<LedgerTransaction>> GetUserTransactionsAsync(
        string userId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.LedgerTransactions
            .Include(t => t.ChoreLog)
            .ThenInclude(c => c!.ChoreDefinition)
            .Include(t => t.LedgerAccount)
            .Where(t => t.UserId == userId);

        if (fromDate.HasValue) query = query.Where(t => t.TransactionDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(t => t.TransactionDate <= toDate.Value);

        return await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<LedgerTransaction?> GetTransactionForChoreLogAsync(int choreLogId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions
            .Include(t => t.ChoreLog).ThenInclude(c => c!.ChoreDefinition)
            .Include(t => t.LedgerAccount)
            .FirstOrDefaultAsync(t => t.ChoreLogId == choreLogId);
    }

    public async Task<TransactionStats> GetAccountTransactionStatsAsync(
        int ledgerAccountId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.LedgerTransactions.Where(t => t.LedgerAccountId == ledgerAccountId);
        if (fromDate.HasValue) query = query.Where(t => t.TransactionDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(t => t.TransactionDate <= toDate.Value);
        return CalculateStats(await query.ToListAsync());
    }

    public async Task<TransactionStats> GetUserTransactionStatsAsync(
        string userId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.LedgerTransactions.Where(t => t.UserId == userId);
        if (fromDate.HasValue) query = query.Where(t => t.TransactionDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(t => t.TransactionDate <= toDate.Value);
        return CalculateStats(await query.ToListAsync());
    }

    public async Task<ServiceResult> TransferAsync(int fromAccountId, int toAccountId, decimal amount, string reason)
    {
        if (amount <= 0) return ServiceResult.Fail("Transfer amount must be positive.");
        if (fromAccountId == toAccountId) return ServiceResult.Fail("Cannot transfer to the same account.");

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var fromAccount = await context.LedgerAccounts.Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == fromAccountId);
        var toAccount = await context.LedgerAccounts.Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == toAccountId);

        if (fromAccount == null || toAccount == null)
            return ServiceResult.Fail("One or both accounts not found.");

        var fromBalance = await context.LedgerTransactions
            .Where(t => t.LedgerAccountId == fromAccountId).SumAsync(t => t.Amount);
        if (fromBalance < amount)
            return ServiceResult.Fail($"Insufficient balance. Available: ${fromBalance:F2}");

        var transferGroupId = Guid.NewGuid();
        var today = _dateProvider.Today;
        var now = DateTime.UtcNow;

        context.LedgerTransactions.AddRange(
            new LedgerTransaction
            {
                LedgerAccountId = fromAccountId,
                UserId = fromAccount.ChildProfile.UserId,
                Amount = -amount,
                Type = TransactionType.Transfer,
                Description = $"Transfer to {toAccount.ChildProfile.DisplayName}'s {toAccount.Name}: {reason}",
                TransactionDate = today,
                TransferGroupId = transferGroupId,
                CreatedAt = now
            },
            new LedgerTransaction
            {
                LedgerAccountId = toAccountId,
                UserId = toAccount.ChildProfile.UserId,
                Amount = amount,
                Type = TransactionType.Transfer,
                Description = $"Transfer from {fromAccount.ChildProfile.DisplayName}'s {fromAccount.Name}: {reason}",
                TransactionDate = today,
                TransferGroupId = transferGroupId,
                CreatedAt = now
            }
        );

        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private static TransactionStats CalculateStats(List<LedgerTransaction> transactions) => new()
    {
        TotalEarnings = transactions.Where(t => t.Type == TransactionType.ChoreEarning && t.Amount > 0).Sum(t => t.Amount),
        TotalDeductions = transactions.Where(t => t.Type == TransactionType.ChoreDeduction).Sum(t => Math.Abs(t.Amount)),
        TotalBonuses = transactions.Where(t => t.Type == TransactionType.Bonus).Sum(t => t.Amount),
        TotalPenalties = transactions.Where(t => t.Type == TransactionType.Penalty).Sum(t => Math.Abs(t.Amount)),
        TotalPayouts = transactions.Where(t => t.Type == TransactionType.Payout).Sum(t => Math.Abs(t.Amount)),
        TotalAdjustments = transactions.Where(t => t.Type == TransactionType.Adjustment).Sum(t => t.Amount),
        TotalTransfersIn = transactions.Where(t => t.Type == TransactionType.Transfer && t.Amount > 0).Sum(t => t.Amount),
        TotalTransfersOut = transactions.Where(t => t.Type == TransactionType.Transfer && t.Amount < 0).Sum(t => Math.Abs(t.Amount)),
        NetBalance = transactions.Sum(t => t.Amount),
        TransactionCount = transactions.Count
    };

    private async Task<(bool NeedsTransaction, decimal Amount, TransactionType Type, string Description)> DetermineTransactionAsync(
        ApplicationDbContext context, ChoreLog choreLog, ChoreDefinition choreDefinition)
    {
        if (choreLog.Status != ChoreStatus.Approved && choreLog.Status != ChoreStatus.Missed)
            return (false, 0, TransactionType.ChoreEarning, string.Empty);
        
        if (choreLog.Status == ChoreStatus.Missed)
        {
            return choreDefinition.PenaltyValue > 0
                ? (true, -choreDefinition.PenaltyValue, TransactionType.ChoreDeduction, $"Missed: {choreDefinition.Name}")
                : (false, 0, TransactionType.ChoreDeduction, string.Empty);
        }
        
        if (choreDefinition.EarnValue <= 0)
            return (false, 0, TransactionType.ChoreEarning, string.Empty);
        
        if (choreDefinition.ScheduleType == ChoreScheduleType.WeeklyFrequency)
        {
            var earnAmount = await CalculateWeeklyChoreEarningAsync(context, choreLog, choreDefinition);
            if (earnAmount <= 0) return (false, 0, TransactionType.ChoreEarning, string.Empty);
            
            var weekStart = await _familySettingsService.GetWeekStartForDateAsync(choreLog.Date);
            var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(choreLog.Date);
            
            var completionsBefore = await context.ChoreLogs
                .Where(l => l.ChoreDefinitionId == choreDefinition.Id)
                .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
                .Where(l => l.Status == ChoreStatus.Approved)
                .Where(l => l.Id < choreLog.Id)
                .CountAsync();
            
            var isBonus = completionsBefore >= choreDefinition.WeeklyTargetCount;
            var description = isBonus 
                ? $"Bonus: {choreDefinition.Name} (+{completionsBefore - choreDefinition.WeeklyTargetCount + 1} extra)"
                : $"Completed: {choreDefinition.Name} ({completionsBefore + 1}/{choreDefinition.WeeklyTargetCount})";
                
            return (true, earnAmount, TransactionType.ChoreEarning, description);
        }
        
        return (true, choreDefinition.EarnValue, TransactionType.ChoreEarning, $"Completed: {choreDefinition.Name}");
    }

    private async Task<decimal> CalculateWeeklyChoreEarningAsync(
        ApplicationDbContext context, ChoreLog choreLog, ChoreDefinition choreDefinition)
    {
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(choreLog.Date);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(choreLog.Date);
        
        var completionsBefore = await context.ChoreLogs
            .Where(l => l.ChoreDefinitionId == choreDefinition.Id)
            .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
            .Where(l => l.Status == ChoreStatus.Approved)
            .Where(l => l.Id < choreLog.Id)
            .CountAsync();
        
        if (completionsBefore < choreDefinition.WeeklyTargetCount)
            return choreDefinition.EarnValue;
        
        if (!choreDefinition.IsRepeatable)
            return 0;
        
        var bonusCompletions = completionsBefore - choreDefinition.WeeklyTargetCount;
        var multiplier = Math.Pow(0.5, bonusCompletions + 1);
        return Math.Round(choreDefinition.EarnValue * (decimal)multiplier, 2);
    }
}
