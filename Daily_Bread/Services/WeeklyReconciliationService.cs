using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Result of weekly reconciliation for a single child.
/// </summary>
public class WeeklyReconciliationResult
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    
    /// <summary>
    /// Chores that were incomplete and had penalties applied.
    /// </summary>
    public List<IncompleteChoreRecord> IncompleteChores { get; init; } = [];
    
    /// <summary>
    /// Total penalty amount deducted.
    /// </summary>
    public decimal TotalPenalty { get; init; }
    
    /// <summary>
    /// Whether any penalties were applied.
    /// </summary>
    public bool HadPenalties => IncompleteChores.Count > 0;
}

/// <summary>
/// Record of an incomplete chore and its penalty.
/// </summary>
public class IncompleteChoreRecord
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public int TargetCount { get; init; }
    public int CompletedCount { get; init; }
    public int MissedCount => TargetCount - CompletedCount;
    public decimal PenaltyAmount { get; init; }
}

/// <summary>
/// Service for running weekly reconciliation to apply penalties for incomplete chores.
/// </summary>
public interface IWeeklyReconciliationService
{
    /// <summary>
    /// Runs weekly reconciliation for all children.
    /// Should be called at the end of each week (e.g., Sunday night).
    /// </summary>
    Task<List<WeeklyReconciliationResult>> RunWeeklyReconciliationAsync(DateOnly weekEndDate);
    
    /// <summary>
    /// Runs weekly reconciliation for a specific child.
    /// </summary>
    Task<WeeklyReconciliationResult> ReconcileChildWeekAsync(string userId, DateOnly weekEndDate);
    
    /// <summary>
    /// Gets the last reconciliation date from audit records.
    /// </summary>
    Task<DateOnly?> GetLastReconciliationDateAsync();
    
    /// <summary>
    /// Checks if reconciliation is needed (week has ended and not yet reconciled).
    /// </summary>
    Task<bool> IsReconciliationNeededAsync();
}

public class WeeklyReconciliationService : IWeeklyReconciliationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IWeeklyProgressService _weeklyProgressService;
    private readonly IDateProvider _dateProvider;
    private readonly ILogger<WeeklyReconciliationService> _logger;

    // App setting key for tracking last reconciliation
    private const string LastReconciliationKey = "LastWeeklyReconciliation";

    public WeeklyReconciliationService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IFamilySettingsService familySettingsService,
        IWeeklyProgressService weeklyProgressService,
        IDateProvider dateProvider,
        ILogger<WeeklyReconciliationService> logger)
    {
        _contextFactory = contextFactory;
        _familySettingsService = familySettingsService;
        _weeklyProgressService = weeklyProgressService;
        _dateProvider = dateProvider;
        _logger = logger;
    }

    public async Task<List<WeeklyReconciliationResult>> RunWeeklyReconciliationAsync(DateOnly weekEndDate)
    {
        _logger.LogInformation("Starting weekly reconciliation for week ending {WeekEnd}", weekEndDate);
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Get all child profiles
        var childProfiles = await context.ChildProfiles
            .Include(p => p.User)
            .Include(p => p.LedgerAccounts.Where(a => a.IsActive))
            .Where(p => p.IsActive)
            .ToListAsync();
        
        var results = new List<WeeklyReconciliationResult>();
        
        foreach (var child in childProfiles)
        {
            try
            {
                var result = await ReconcileChildWeekAsync(child.UserId, weekEndDate);
                results.Add(result);
                
                if (result.HadPenalties)
                {
                    _logger.LogInformation(
                        "Applied ${Penalty:F2} penalty to {User} for {Count} incomplete chores",
                        result.TotalPenalty,
                        result.UserName,
                        result.IncompleteChores.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconcile week for user {UserId}", child.UserId);
            }
        }
        
        // Record that we've run reconciliation for this week
        await RecordReconciliationAsync(weekEndDate);
        
        _logger.LogInformation(
            "Weekly reconciliation complete. {Total} children processed, {WithPenalties} had penalties",
            results.Count,
            results.Count(r => r.HadPenalties));
        
        return results;
    }

    public async Task<WeeklyReconciliationResult> ReconcileChildWeekAsync(string userId, DateOnly weekEndDate)
    {
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(weekEndDate);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(weekEndDate);
        var settings = await _familySettingsService.GetSettingsAsync();
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        
        try
        {
            var childProfile = await context.ChildProfiles
                .Include(p => p.User)
                .Include(p => p.LedgerAccounts.Where(a => a.IsActive && a.IsDefault))
                .FirstOrDefaultAsync(p => p.UserId == userId);
            
            if (childProfile == null)
            {
                return new WeeklyReconciliationResult
                {
                    UserId = userId,
                    UserName = "Unknown",
                    WeekStart = weekStart,
                    WeekEnd = weekEnd,
                    TotalPenalty = 0
                };
            }
            
            var defaultAccount = childProfile.LedgerAccounts.FirstOrDefault();
            var weeklyProgress = await _weeklyProgressService.GetWeeklyProgressForUserAsync(userId, weekEndDate);
            
            // IDEMPOTENCY: Check for existing penalties using deterministic key
            // (UserId, ChoreDefinitionId, WeekEndDate, Type=Penalty)
            var existingPenaltyChoreIds = await context.LedgerTransactions
                .Where(t => t.UserId == userId)
                .Where(t => t.Type == TransactionType.Penalty)
                .Where(t => t.WeekEndDate == weekEnd)
                .Where(t => t.ChoreDefinitionId != null)
                .Select(t => t.ChoreDefinitionId!.Value)
                .ToListAsync();
            
            var incompleteChores = new List<IncompleteChoreRecord>();
            decimal totalPenalty = 0;
            var newTransactions = new List<LedgerTransaction>();
            
            foreach (var progress in weeklyProgress.ChoreProgress)
            {
                if (progress.QuotaMet)
                {
                    continue;
                }
                
                // IDEMPOTENCY: Skip if penalty already exists for this (user, chore, week)
                if (existingPenaltyChoreIds.Contains(progress.ChoreDefinition.Id))
                {
                    _logger.LogDebug(
                        "Skipping duplicate penalty: UserId={UserId}, ChoreId={ChoreId}, WeekEnd={WeekEnd}",
                        userId, progress.ChoreDefinition.Id, weekEnd);
                    continue;
                }
                
                var missedCount = progress.TargetCount - progress.CompletedCount;
                var penaltyAmount = missedCount * progress.ChoreDefinition.EarnValue * settings.WeeklyIncompletePenaltyPercent;
                
                if (penaltyAmount <= 0)
                {
                    continue;
                }
                
                incompleteChores.Add(new IncompleteChoreRecord
                {
                    ChoreDefinitionId = progress.ChoreDefinition.Id,
                    ChoreName = progress.ChoreDefinition.Name,
                    TargetCount = progress.TargetCount,
                    CompletedCount = progress.CompletedCount,
                    PenaltyAmount = penaltyAmount
                });
                
                totalPenalty += penaltyAmount;
                
                if (defaultAccount != null)
                {
                    var penaltyTransaction = new LedgerTransaction
                    {
                        LedgerAccountId = defaultAccount.Id,
                        UserId = userId,
                        ChoreDefinitionId = progress.ChoreDefinition.Id, // For idempotency
                        WeekEndDate = weekEnd, // For idempotency
                        Amount = -penaltyAmount,
                        Type = TransactionType.Penalty,
                        Description = $"Incomplete: {progress.ChoreDefinition.Name} ({progress.CompletedCount}/{progress.TargetCount})",
                        TransactionDate = weekEnd,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    newTransactions.Add(penaltyTransaction);
                    
                    _logger.LogInformation(
                        "Creating weekly penalty: UserId={UserId}, ChoreId={ChoreId}, WeekEnd={WeekEnd}, Amount=-${Amount:F2}",
                        userId, progress.ChoreDefinition.Id, weekEnd, penaltyAmount);
                }
            }
            
            if (newTransactions.Count > 0)
            {
                context.LedgerTransactions.AddRange(newTransactions);
                await context.SaveChangesAsync();
            }
            
            await transaction.CommitAsync();
            
            return new WeeklyReconciliationResult
            {
                UserId = userId,
                UserName = childProfile.DisplayName,
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                IncompleteChores = incompleteChores,
                TotalPenalty = totalPenalty
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to reconcile week for user {UserId}", userId);
            throw;
        }
    }

    public async Task<DateOnly?> GetLastReconciliationDateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LastReconciliationKey);
        
        if (setting != null && DateOnly.TryParse(setting.Value, out var date))
        {
            return date;
        }
        
        return null;
    }

    public async Task<bool> IsReconciliationNeededAsync()
    {
        var today = _dateProvider.Today;
        var currentWeekEnd = await _familySettingsService.GetWeekEndForDateAsync(today);
        
        // If today is after the week end, reconciliation might be needed
        if (today <= currentWeekEnd)
        {
            return false; // Week not over yet
        }
        
        var lastReconciliation = await GetLastReconciliationDateAsync();
        
        // If never reconciled, or last reconciliation was before this week end
        return lastReconciliation == null || lastReconciliation < currentWeekEnd;
    }

    private async Task RecordReconciliationAsync(DateOnly weekEndDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LastReconciliationKey);
        
        if (setting == null)
        {
            setting = new AppSetting
            {
                Key = LastReconciliationKey,
                Value = weekEndDate.ToString("O"),
                Description = "Date of last weekly chore reconciliation",
                DataType = SettingDataType.String
            };
            context.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = weekEndDate.ToString("O");
            setting.ModifiedAt = DateTime.UtcNow;
        }
        
        await context.SaveChangesAsync();
    }
}
