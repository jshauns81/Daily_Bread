using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Daily_Bread.Services;

/// <summary>
/// Summary of a ledger account's financial status.
/// </summary>
public class AccountBalanceSummary
{
    public int AccountId { get; init; }
    public required string AccountName { get; init; }
    public int ChildProfileId { get; init; }
    public required string ChildDisplayName { get; init; }
    public required string UserId { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal TotalEarned { get; init; }
    public decimal TotalDeducted { get; init; }
    public decimal TotalPaidOut { get; init; }
    public decimal CashOutThreshold { get; init; }
    public bool CanCashOut => CurrentBalance >= CashOutThreshold;
    public decimal AvailableForCashOut => CanCashOut ? CurrentBalance : 0;
}

/// <summary>
/// Legacy balance summary for backward compatibility.
/// </summary>
public class BalanceSummary
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal TotalEarned { get; init; }
    public decimal TotalDeducted { get; init; }
    public decimal TotalPaidOut { get; init; }
    public decimal CashOutThreshold { get; init; }
    public bool CanCashOut => CurrentBalance >= CashOutThreshold;
    public decimal AvailableForCashOut => CanCashOut ? CurrentBalance : 0;
}

/// <summary>
/// Service for managing payouts and cash out operations.
/// </summary>
public interface IPayoutService
{
    // Account-based methods
    Task<AccountBalanceSummary> GetAccountBalanceSummaryAsync(int ledgerAccountId);
    Task<List<AccountBalanceSummary>> GetAllAccountBalancesAsync();
    Task<ServiceResult> ProcessAccountCashOutAsync(int ledgerAccountId, decimal amount, string parentUserId, string? notes = null);
    Task<ServiceResult> AddAccountBonusAsync(int ledgerAccountId, decimal amount, string parentUserId, string description);
    Task<ServiceResult> AddAccountPenaltyAsync(int ledgerAccountId, decimal amount, string parentUserId, string description);
    Task<ServiceResult> AddAccountAdjustmentAsync(int ledgerAccountId, decimal amount, string parentUserId, string description);

    // Legacy user-based methods (for backward compatibility)
    Task<BalanceSummary> GetBalanceSummaryAsync(string userId);
    Task<List<BalanceSummary>> GetAllChildBalancesAsync();
    Task<decimal> GetCashOutThresholdAsync();
    Task<ServiceResult> SetCashOutThresholdAsync(decimal threshold);
    Task<ServiceResult> ProcessCashOutAsync(string userId, decimal amount, string parentUserId, string? notes = null);
    Task<ServiceResult> AddBonusAsync(string userId, decimal amount, string parentUserId, string description);
    Task<ServiceResult> AddPenaltyAsync(string userId, decimal amount, string parentUserId, string description);
    Task<ServiceResult> AddAdjustmentAsync(string userId, decimal amount, string parentUserId, string description);
}

public class PayoutService : IPayoutService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IMemoryCache _cache;
    
    // Cache key and duration for CashOutThreshold
    private const string CashOutThresholdCacheKey = "CashOutThreshold";
    private static readonly TimeSpan CashOutThresholdCacheDuration = TimeSpan.FromMinutes(5);

    public PayoutService(
        IDbContextFactory<ApplicationDbContext> contextFactory, 
        IDateProvider dateProvider,
        IMemoryCache cache)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _cache = cache;
    }

    #region Account-based methods

    public async Task<AccountBalanceSummary> GetAccountBalanceSummaryAsync(int ledgerAccountId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Get account info without loading transactions
        var account = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == ledgerAccountId);

        if (account == null)
        {
            return new AccountBalanceSummary
            {
                AccountId = ledgerAccountId,
                AccountName = "Unknown",
                ChildProfileId = 0,
                ChildDisplayName = "Unknown",
                UserId = "",
                CurrentBalance = 0,
                TotalEarned = 0,
                TotalDeducted = 0,
                TotalPaidOut = 0,
                CashOutThreshold = await GetCashOutThresholdAsync()
            };
        }

        // =============================================================================
        // OPTIMIZATION: Calculate aggregates in SQL instead of loading all transactions
        // Previously: Include(a => a.LedgerTransactions) then Sum in C#
        // Now: GroupBy/Sum projection executed in SQL
        // =============================================================================
        var aggregates = await context.LedgerTransactions
            .Where(t => t.LedgerAccountId == ledgerAccountId)
            .GroupBy(t => t.LedgerAccountId)
            .Select(g => new
            {
                CurrentBalance = g.Sum(t => t.Amount),
                TotalEarned = g.Where(t => t.Amount > 0 && t.Type != TransactionType.Adjustment && t.Type != TransactionType.Transfer)
                    .Sum(t => t.Amount),
                TotalDeducted = g.Where(t => t.Amount < 0 && t.Type != TransactionType.Payout && t.Type != TransactionType.Adjustment && t.Type != TransactionType.Transfer)
                    .Sum(t => -t.Amount), // Use -t.Amount instead of Math.Abs for SQL translation
                TotalPaidOut = g.Where(t => t.Type == TransactionType.Payout)
                    .Sum(t => -t.Amount) // Payouts are negative, so negate to get positive sum
            })
            .FirstOrDefaultAsync();

        return new AccountBalanceSummary
        {
            AccountId = account.Id,
            AccountName = account.Name,
            ChildProfileId = account.ChildProfileId,
            ChildDisplayName = account.ChildProfile.DisplayName,
            UserId = account.ChildProfile.UserId,
            CurrentBalance = aggregates?.CurrentBalance ?? 0,
            TotalEarned = aggregates?.TotalEarned ?? 0,
            TotalDeducted = aggregates?.TotalDeducted ?? 0,
            TotalPaidOut = aggregates?.TotalPaidOut ?? 0,
            CashOutThreshold = await GetCashOutThresholdAsync()
        };
    }

    public async Task<List<AccountBalanceSummary>> GetAllAccountBalancesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // =============================================================================
        // OPTIMIZATION: Calculate aggregates in SQL instead of loading all transactions
        // Previously: Include(a => a.LedgerTransactions) for ALL accounts, iterate in C#
        // Now: Single SQL query with GroupBy/Sum projection
        // =============================================================================
        
        // Get all active accounts with their child profile info (no transactions)
        var accounts = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .Where(a => a.IsActive && a.ChildProfile.IsActive)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.ChildProfileId,
                ChildDisplayName = a.ChildProfile.DisplayName,
                UserId = a.ChildProfile.UserId
            })
            .ToListAsync();

        var accountIds = accounts.Select(a => a.Id).ToList();

        // Calculate all aggregates in a single SQL query
        var aggregates = await context.LedgerTransactions
            .Where(t => accountIds.Contains(t.LedgerAccountId))
            .GroupBy(t => t.LedgerAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                CurrentBalance = g.Sum(t => t.Amount),
                TotalEarned = g.Where(t => t.Amount > 0 && t.Type != TransactionType.Adjustment && t.Type != TransactionType.Transfer)
                    .Sum(t => t.Amount),
                TotalDeducted = g.Where(t => t.Amount < 0 && t.Type != TransactionType.Payout && t.Type != TransactionType.Adjustment && t.Type != TransactionType.Transfer)
                    .Sum(t => -t.Amount),
                TotalPaidOut = g.Where(t => t.Type == TransactionType.Payout)
                    .Sum(t => -t.Amount)
            })
            .ToDictionaryAsync(a => a.AccountId);

        var threshold = await GetCashOutThresholdAsync();

        // Join account info with aggregates in memory (just the IDs and computed sums)
        var summaries = accounts.Select(account =>
        {
            aggregates.TryGetValue(account.Id, out var agg);
            return new AccountBalanceSummary
            {
                AccountId = account.Id,
                AccountName = account.Name,
                ChildProfileId = account.ChildProfileId,
                ChildDisplayName = account.ChildDisplayName,
                UserId = account.UserId,
                CurrentBalance = agg?.CurrentBalance ?? 0,
                TotalEarned = agg?.TotalEarned ?? 0,
                TotalDeducted = agg?.TotalDeducted ?? 0,
                TotalPaidOut = agg?.TotalPaidOut ?? 0,
                CashOutThreshold = threshold
            };
        }).ToList();

        return summaries.OrderBy(s => s.ChildDisplayName).ThenBy(s => s.AccountName).ToList();
    }

    public async Task<ServiceResult> ProcessAccountCashOutAsync(
        int ledgerAccountId,
        decimal amount,
        string parentUserId,
        string? notes = null)
    {
        if (amount <= 0)
        {
            return ServiceResult.Fail("Cash out amount must be positive.");
        }

        var summary = await GetAccountBalanceSummaryAsync(ledgerAccountId);

        if (amount > summary.CurrentBalance)
        {
            return ServiceResult.Fail($"Insufficient balance. Current balance is ${summary.CurrentBalance:F2}.");
        }

        if (summary.CurrentBalance < summary.CashOutThreshold)
        {
            return ServiceResult.Fail($"Balance must be at least ${summary.CashOutThreshold:F2} to cash out.");
        }

        var description = string.IsNullOrWhiteSpace(notes)
            ? $"Cash out: ${amount:F2}"
            : $"Cash out: ${amount:F2} - {notes}";

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var transaction = new LedgerTransaction
        {
            LedgerAccountId = ledgerAccountId,
            UserId = summary.UserId,
            Amount = -amount,
            Type = TransactionType.Payout,
            Description = description,
            TransactionDate = _dateProvider.Today,
            CreatedAt = DateTime.UtcNow
        };

        context.LedgerTransactions.Add(transaction);
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AddAccountBonusAsync(
        int ledgerAccountId,
        decimal amount,
        string parentUserId,
        string description)
    {
        if (amount <= 0)
        {
            return ServiceResult.Fail("Bonus amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return ServiceResult.Fail("Description is required for bonuses.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var account = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == ledgerAccountId);

        if (account == null)
        {
            return ServiceResult.Fail("Account not found.");
        }

        var transaction = new LedgerTransaction
        {
            LedgerAccountId = ledgerAccountId,
            UserId = account.ChildProfile.UserId,
            Amount = amount,
            Type = TransactionType.Bonus,
            Description = $"Bonus: {description}",
            TransactionDate = _dateProvider.Today,
            CreatedAt = DateTime.UtcNow
        };

        context.LedgerTransactions.Add(transaction);
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AddAccountPenaltyAsync(
        int ledgerAccountId,
        decimal amount,
        string parentUserId,
        string description)
    {
        if (amount <= 0)
        {
            return ServiceResult.Fail("Penalty amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return ServiceResult.Fail("Description is required for penalties.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var account = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == ledgerAccountId);

        if (account == null)
        {
            return ServiceResult.Fail("Account not found.");
        }

        var transaction = new LedgerTransaction
        {
            LedgerAccountId = ledgerAccountId,
            UserId = account.ChildProfile.UserId,
            Amount = -amount,
            Type = TransactionType.Penalty,
            Description = $"Penalty: {description}",
            TransactionDate = _dateProvider.Today,
            CreatedAt = DateTime.UtcNow
        };

        context.LedgerTransactions.Add(transaction);
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AddAccountAdjustmentAsync(
        int ledgerAccountId,
        decimal amount,
        string parentUserId,
        string description)
    {
        if (amount == 0)
        {
            return ServiceResult.Fail("Adjustment amount cannot be zero.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return ServiceResult.Fail("Description is required for adjustments.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var account = await context.LedgerAccounts
            .Include(a => a.ChildProfile)
            .FirstOrDefaultAsync(a => a.Id == ledgerAccountId);

        if (account == null)
        {
            return ServiceResult.Fail("Account not found.");
        }

        var adjustmentType = amount > 0 ? "Credit" : "Debit";
        var transaction = new LedgerTransaction
        {
            LedgerAccountId = ledgerAccountId,
            UserId = account.ChildProfile.UserId,
            Amount = amount,
            Type = TransactionType.Adjustment,
            Description = $"Adjustment ({adjustmentType}): {description}",
            TransactionDate = _dateProvider.Today,
            CreatedAt = DateTime.UtcNow
        };

        context.LedgerTransactions.Add(transaction);
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    #endregion

    #region Legacy user-based methods

    public async Task<BalanceSummary> GetBalanceSummaryAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return new BalanceSummary
            {
                UserId = userId,
                UserName = "Unknown",
                CurrentBalance = 0,
                TotalEarned = 0,
                TotalDeducted = 0,
                TotalPaidOut = 0,
                CashOutThreshold = await GetCashOutThresholdAsync()
            };
        }

        // Get default account for this user
        var profile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .ThenInclude(a => a.LedgerTransactions)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile != null)
        {
            var defaultAccount = profile.LedgerAccounts.FirstOrDefault(a => a.IsDefault && a.IsActive)
                ?? profile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

            if (defaultAccount != null)
            {
                var summary = await GetAccountBalanceSummaryAsync(defaultAccount.Id);
                return new BalanceSummary
                {
                    UserId = summary.UserId,
                    UserName = summary.ChildDisplayName,
                    CurrentBalance = summary.CurrentBalance,
                    TotalEarned = summary.TotalEarned,
                    TotalDeducted = summary.TotalDeducted,
                    TotalPaidOut = summary.TotalPaidOut,
                    CashOutThreshold = summary.CashOutThreshold
                };
            }
        }

        // Fall back to legacy transaction query
        var transactions = await context.LedgerTransactions
            .Where(t => t.UserId == userId)
            .ToListAsync();

        var totalEarned = transactions
            .Where(t => t.Amount > 0 && t.Type != TransactionType.Adjustment)
            .Sum(t => t.Amount);

        var totalDeducted = transactions
            .Where(t => t.Amount < 0 && t.Type != TransactionType.Payout && t.Type != TransactionType.Adjustment)
            .Sum(t => Math.Abs(t.Amount));

        var totalPaidOut = transactions
            .Where(t => t.Type == TransactionType.Payout)
            .Sum(t => Math.Abs(t.Amount));

        var currentBalance = transactions.Sum(t => t.Amount);

        return new BalanceSummary
        {
            UserId = userId,
            UserName = user.UserName ?? "Unknown",
            CurrentBalance = currentBalance,
            TotalEarned = totalEarned,
            TotalDeducted = totalDeducted,
            TotalPaidOut = totalPaidOut,
            CashOutThreshold = await GetCashOutThresholdAsync()
        };
    }

    public async Task<List<BalanceSummary>> GetAllChildBalancesAsync()
    {
        var accountSummaries = await GetAllAccountBalancesAsync();

        // Group by child and sum balances (in case of multiple accounts)
        var grouped = accountSummaries
            .GroupBy(a => a.UserId)
            .Select(g => new BalanceSummary
            {
                UserId = g.Key,
                UserName = g.First().ChildDisplayName,
                CurrentBalance = g.Sum(a => a.CurrentBalance),
                TotalEarned = g.Sum(a => a.TotalEarned),
                TotalDeducted = g.Sum(a => a.TotalDeducted),
                TotalPaidOut = g.Sum(a => a.TotalPaidOut),
                CashOutThreshold = g.First().CashOutThreshold
            })
            .OrderBy(s => s.UserName)
            .ToList();

        return grouped;
    }

    public async Task<decimal> GetCashOutThresholdAsync()
    {
        // =============================================================================
        // OPTIMIZATION: Cache the threshold value to avoid repeated DB queries
        // This value rarely changes but is called frequently (balance summaries, etc.)
        // =============================================================================
        if (_cache.TryGetValue(CashOutThresholdCacheKey, out decimal cachedThreshold))
        {
            return cachedThreshold;
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == AppSettingKeys.CashOutThreshold);

        decimal threshold;
        if (setting != null && decimal.TryParse(setting.Value, out threshold))
        {
            // Cache the value
            _cache.Set(CashOutThresholdCacheKey, threshold, CashOutThresholdCacheDuration);
            return threshold;
        }

        // Cache the default value
        threshold = AppSettingKeys.DefaultCashOutThreshold;
        _cache.Set(CashOutThresholdCacheKey, threshold, CashOutThresholdCacheDuration);
        return threshold;
    }

    public async Task<ServiceResult> SetCashOutThresholdAsync(decimal threshold)
    {
        if (threshold < 0)
        {
            return ServiceResult.Fail("Threshold cannot be negative.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == AppSettingKeys.CashOutThreshold);

        if (setting == null)
        {
            setting = new AppSetting
            {
                Key = AppSettingKeys.CashOutThreshold,
                Value = threshold.ToString("F2"),
                Description = "Minimum balance required before cash out is allowed",
                DataType = SettingDataType.Decimal
            };
            context.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = threshold.ToString("F2");
            setting.ModifiedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
        
        // =============================================================================
        // OPTIMIZATION: Invalidate the cache when threshold is updated
        // =============================================================================
        _cache.Remove(CashOutThresholdCacheKey);
        
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ProcessCashOutAsync(
        string userId,
        decimal amount,
        string parentUserId,
        string? notes = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Find default account and delegate
        var profile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return ServiceResult.Fail("Child profile not found.");
        }

        var defaultAccount = profile.LedgerAccounts.FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? profile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            return ServiceResult.Fail("No active account found.");
        }

        return await ProcessAccountCashOutAsync(defaultAccount.Id, amount, parentUserId, notes);
    }

    public async Task<ServiceResult> AddBonusAsync(
        string userId,
        decimal amount,
        string parentUserId,
        string description)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var profile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return ServiceResult.Fail("Child profile not found.");
        }

        var defaultAccount = profile.LedgerAccounts.FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? profile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            return ServiceResult.Fail("No active account found.");
        }

        return await AddAccountBonusAsync(defaultAccount.Id, amount, parentUserId, description);
    }

    public async Task<ServiceResult> AddPenaltyAsync(
        string userId,
        decimal amount,
        string parentUserId,
        string description)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var profile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return ServiceResult.Fail("Child profile not found.");
        }

        var defaultAccount = profile.LedgerAccounts.FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? profile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            return ServiceResult.Fail("No active account found.");
        }

        return await AddAccountPenaltyAsync(defaultAccount.Id, amount, parentUserId, description);
    }

    public async Task<ServiceResult> AddAdjustmentAsync(
        string userId,
        decimal amount,
        string parentUserId,
        string description)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var profile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return ServiceResult.Fail("Child profile not found.");
        }

        var defaultAccount = profile.LedgerAccounts.FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? profile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            return ServiceResult.Fail("No active account found.");
        }

        return await AddAccountAdjustmentAsync(defaultAccount.Id, amount, parentUserId, description);
    }

    #endregion
}
