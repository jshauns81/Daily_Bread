using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Daily_Bread.Services;

/// <summary>
/// A pending or decided reward claim, shaped for the parent approval list and the
/// child's own achievement view.
/// </summary>
public record RewardClaimDisplay
{
    public int Id { get; init; }
    public string UserId { get; init; } = "";
    public string ChildName { get; init; } = "";
    public string AchievementName { get; init; } = "";
    public string AchievementIcon { get; init; } = "";
    public RewardClaimType RewardType { get; init; }
    public decimal? CashAmount { get; init; }
    public string? ItemLabel { get; init; }
    public decimal? ItemEstValue { get; init; }
    public RewardClaimStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? RejectionReason { get; init; }
}

/// <summary>
/// Service for the TangibleReward approval flow: a child earning an achievement that
/// carries a real-world reward never touches the balance directly. It creates a claim;
/// only a parent approving (or rejecting) it has any further effect.
/// </summary>
public interface IAchievementRewardClaimService
{
    /// <summary>
    /// Creates a pending reward claim for a just-earned TangibleReward achievement.
    /// No-op if achievement.BonusType isn't TangibleReward, or if a claim for this
    /// userAchievementId already exists (idempotent - safe to call more than once for
    /// the same earn event, e.g. if the achievement evaluator re-runs).
    /// </summary>
    Task CreateClaimIfNeededAsync(string userId, Achievement achievement, int userAchievementId);

    /// <summary>
    /// Gets all claims still awaiting a parent decision, oldest first.
    /// </summary>
    Task<List<RewardClaimDisplay>> GetPendingClaimsAsync();

    /// <summary>
    /// Gets all claims (any status) for a specific user, newest first.
    /// </summary>
    Task<List<RewardClaimDisplay>> GetClaimsForUserAsync(string userId);

    /// <summary>
    /// Approves a pending claim. Cash claims write exactly one LedgerService transaction
    /// and move to Approved. Item claims move straight to FulfilledByParent - no balance
    /// effect, est_value is reporting-only. Fails if the claim isn't still PendingApproval
    /// (already decided, or doesn't exist).
    /// </summary>
    Task<ServiceResult> ApproveClaimAsync(int claimId, string parentUserId);

    /// <summary>
    /// Rejects a pending claim. The underlying achievement/badge stays earned either way -
    /// rejecting only declines the attached reward. No balance effect.
    /// </summary>
    Task<ServiceResult> RejectClaimAsync(int claimId, string parentUserId, string? reason = null);
}

public class AchievementRewardClaimService : IAchievementRewardClaimService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly ILogger<AchievementRewardClaimService> _logger;

    public AchievementRewardClaimService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider,
        ILogger<AchievementRewardClaimService> logger)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _logger = logger;
    }

    public async Task CreateClaimIfNeededAsync(string userId, Achievement achievement, int userAchievementId)
    {
        if (achievement.BonusType != AchievementBonusType.TangibleReward)
            return;

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Idempotency check #1: an application-level guard for the common case.
        var existing = await context.AchievementRewardClaims
            .FirstOrDefaultAsync(c => c.UserAchievementId == userAchievementId);

        if (existing != null)
        {
            _logger.LogInformation(
                "Reward claim for UserAchievement {UserAchievementId} already exists, skipping", userAchievementId);
            return;
        }

        var claim = BuildClaim(userId, achievement, userAchievementId);
        if (claim == null)
            return;

        context.AchievementRewardClaims.Add(claim);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Idempotency check #2: the unique index on UserAchievementId is the real
            // guarantee. If two calls raced past the check above, the loser lands here
            // instead of creating a duplicate claim.
            _logger.LogInformation(ex,
                "Reward claim insert for UserAchievement {UserAchievementId} hit the unique constraint, treating as already-created",
                userAchievementId);
            return;
        }

        _logger.LogInformation(
            "Created {RewardType} reward claim for achievement {Code}, user {UserId}",
            claim.RewardType, achievement.Code, userId);
    }

    public async Task<List<RewardClaimDisplay>> GetPendingClaimsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var claims = await context.AchievementRewardClaims
            .Include(c => c.Achievement)
            .Include(c => c.User)
            .Where(c => c.Status == RewardClaimStatus.PendingApproval)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var childNames = await GetChildNamesAsync(context, claims.Select(c => c.UserId));

        return claims.Select(c => ToDisplay(c, childNames)).ToList();
    }

    public async Task<List<RewardClaimDisplay>> GetClaimsForUserAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var claims = await context.AchievementRewardClaims
            .Include(c => c.Achievement)
            .Include(c => c.User)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var childNames = await GetChildNamesAsync(context, [userId]);

        return claims.Select(c => ToDisplay(c, childNames)).ToList();
    }

    public async Task<ServiceResult> ApproveClaimAsync(int claimId, string parentUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var claim = await context.AchievementRewardClaims
            .Include(c => c.Achievement)
            .FirstOrDefaultAsync(c => c.Id == claimId);

        if (claim == null)
            return ServiceResult.Fail("Reward claim not found.");

        if (claim.Status != RewardClaimStatus.PendingApproval)
            return ServiceResult.Fail($"This claim was already {claim.Status switch
            {
                RewardClaimStatus.Approved => "approved",
                RewardClaimStatus.FulfilledByParent => "approved",
                RewardClaimStatus.Rejected => "rejected",
                _ => "decided"
            }}.");

        if (claim.RewardType == RewardClaimType.Cash && (claim.CashAmount ?? 0) <= 0)
            return ServiceResult.Fail("Claim has no cash amount to credit.");

        // The ledger credit (if any) and the claim's status flip must commit together or
        // not at all - wrapping both SaveChanges calls below in one explicit transaction
        // means a failure (including a concurrency conflict on the second save) rolls
        // back the first save's credit too, instead of leaving a committed ledger
        // transaction attached to a claim that never actually flipped to Approved.
        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        if (claim.RewardType == RewardClaimType.Cash)
        {
            var amount = claim.CashAmount!.Value;

            var childProfile = await context.ChildProfiles
                .Include(p => p.LedgerAccounts)
                .FirstOrDefaultAsync(p => p.UserId == claim.UserId);

            var defaultAccount = childProfile?.LedgerAccounts
                .FirstOrDefault(a => a.IsDefault && a.IsActive)
                ?? childProfile?.LedgerAccounts.FirstOrDefault(a => a.IsActive);

            if (defaultAccount == null)
                return ServiceResult.Fail("No active ledger account found for this child.");

            var transaction = new LedgerTransaction
            {
                LedgerAccountId = defaultAccount.Id,
                UserId = claim.UserId,
                Amount = amount,
                Type = TransactionType.AchievementReward,
                Description = $"Achievement Reward: {claim.Achievement.Name}",
                TransactionDate = _dateProvider.Today,
                CreatedAt = DateTime.UtcNow
            };

            context.LedgerTransactions.Add(transaction);
            await context.SaveChangesAsync(); // within dbTransaction; populates transaction.Id

            claim.LedgerTransactionId = transaction.Id;
            claim.Status = RewardClaimStatus.Approved;
        }
        else
        {
            claim.Status = RewardClaimStatus.FulfilledByParent;
        }

        claim.DecidedAt = DateTime.UtcNow;
        claim.DecidedByUserId = parentUserId;

        // Manual concurrency increment (this repo's pattern - see LedgerService.cs,
        // TrackerService.cs). Without this, the WHERE clause EF generates for the
        // claim's UPDATE would always match the current row, and a second concurrent
        // approval would never fail - it would just credit again.
        claim.Version++;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // claim.Version no longer matches the stored row's Version - someone else
            // already decided this claim. dbTransaction is never committed below, so
            // the ledger insert above (if any) rolls back too: no orphaned credit.
            return ServiceResult.Fail("This claim was just decided by someone else. Refresh and try again.");
        }

        await dbTransaction.CommitAsync();

        _logger.LogInformation(
            "Parent {ParentUserId} approved reward claim {ClaimId} ({RewardType}) for user {UserId}",
            parentUserId, claimId, claim.RewardType, claim.UserId);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectClaimAsync(int claimId, string parentUserId, string? reason = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var claim = await context.AchievementRewardClaims.FirstOrDefaultAsync(c => c.Id == claimId);

        if (claim == null)
            return ServiceResult.Fail("Reward claim not found.");

        if (claim.Status != RewardClaimStatus.PendingApproval)
            return ServiceResult.Fail("This claim was already decided.");

        claim.Status = RewardClaimStatus.Rejected;
        claim.RejectionReason = reason;
        claim.DecidedAt = DateTime.UtcNow;
        claim.DecidedByUserId = parentUserId;

        // Same manual concurrency increment as ApproveClaimAsync - without it, a reject
        // racing a concurrent approve could silently overwrite the approval (or vice
        // versa) instead of one of the two failing closed.
        claim.Version++;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail("This claim was just decided by someone else. Refresh and try again.");
        }

        _logger.LogInformation(
            "Parent {ParentUserId} rejected reward claim {ClaimId} for user {UserId}",
            parentUserId, claimId, claim.UserId);

        return ServiceResult.Ok();
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private AchievementRewardClaim? BuildClaim(string userId, Achievement achievement, int userAchievementId)
    {
        var rewardKind = ParseString(achievement.BonusValue, "type", "");

        if (rewardKind.Equals("cash", StringComparison.OrdinalIgnoreCase))
        {
            var amount = ParseDecimal(achievement.BonusValue, "amount", null);
            if (amount is null or <= 0)
            {
                _logger.LogWarning(
                    "TangibleReward achievement {Code} has type=cash but no positive amount - no claim created",
                    achievement.Code);
                return null;
            }

            return new AchievementRewardClaim
            {
                UserAchievementId = userAchievementId,
                UserId = userId,
                AchievementId = achievement.Id,
                RewardType = RewardClaimType.Cash,
                CashAmount = amount,
                Status = RewardClaimStatus.PendingApproval,
                CreatedAt = DateTime.UtcNow
            };
        }

        if (rewardKind.Equals("item", StringComparison.OrdinalIgnoreCase))
        {
            var label = ParseString(achievement.BonusValue, "label", "");
            if (string.IsNullOrWhiteSpace(label))
            {
                _logger.LogWarning(
                    "TangibleReward achievement {Code} has type=item but no label - no claim created",
                    achievement.Code);
                return null;
            }

            return new AchievementRewardClaim
            {
                UserAchievementId = userAchievementId,
                UserId = userId,
                AchievementId = achievement.Id,
                RewardType = RewardClaimType.Item,
                ItemLabel = label,
                ItemEstValue = ParseDecimal(achievement.BonusValue, "est_value", null),
                Status = RewardClaimStatus.PendingApproval,
                CreatedAt = DateTime.UtcNow
            };
        }

        _logger.LogWarning(
            "TangibleReward achievement {Code} has unrecognized/missing BonusValue type ('{Type}') - no claim created",
            achievement.Code, rewardKind);
        return null;
    }

    private static async Task<Dictionary<string, string>> GetChildNamesAsync(
        ApplicationDbContext context, IEnumerable<string> userIds)
    {
        var ids = userIds.Distinct().ToList();
        return await context.ChildProfiles
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName);
    }

    private static RewardClaimDisplay ToDisplay(AchievementRewardClaim c, Dictionary<string, string> childNames)
    {
        return new RewardClaimDisplay
        {
            Id = c.Id,
            UserId = c.UserId,
            ChildName = childNames.GetValueOrDefault(c.UserId, c.User?.UserName ?? "Unknown"),
            AchievementName = c.Achievement.Name,
            AchievementIcon = c.Achievement.Icon,
            RewardType = c.RewardType,
            CashAmount = c.CashAmount,
            ItemLabel = c.ItemLabel,
            ItemEstValue = c.ItemEstValue,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            DecidedAt = c.DecidedAt,
            RejectionReason = c.RejectionReason
        };
    }

    private static decimal? ParseDecimal(string? json, string key, decimal? defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;

        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
                return value.GetDecimal();
        }
        catch { }
        return defaultValue;
    }

    private static string ParseString(string? json, string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;

        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
                return value.GetString() ?? defaultValue;
        }
        catch { }
        return defaultValue;
    }
}
