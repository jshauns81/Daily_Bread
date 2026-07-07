using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Manages a child's QOL (vacuum-fill) routine shares — the stacked mixer that splits each week's
/// applied screen-time loss into explicit target-minute shares (MECHANICS_AMENDMENT.md §C). All
/// rebalancing math lives in the pure <see cref="QolRebalancer"/>; this service persists the results.
/// </summary>
public interface IQolShareService
{
    /// <summary>
    /// Gets the child's QOL routine shares, seeding a 0% row for any active inverse-fill routine that
    /// does not yet have one so the full mixer is always represented.
    /// </summary>
    Task<List<QolShare>> GetSharesAsync(int childProfileId);

    /// <summary>
    /// Sets one routine's share to <paramref name="newPercent"/> and proportionally rebalances the
    /// other unlocked routines (see <see cref="QolRebalancer"/>), persisting every changed row.
    /// Fails if all other shares are locked (the drag is blocked).
    /// </summary>
    Task<ServiceResult> SetShareAsync(int childProfileId, int choreDefinitionId, int newPercent);

    /// <summary>
    /// Locks or unlocks a routine's share. A locked share is pinned and exempt from proportional
    /// rebalancing when another segment is changed.
    /// </summary>
    Task<ServiceResult> SetLockAsync(int childProfileId, int choreDefinitionId, bool isLocked);
}

public class QolShareService : IQolShareService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

    public QolShareService(IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<QolShare>> GetSharesAsync(int childProfileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        await EnsureSharesSeededAsync(context, childProfileId);

        return await context.QolShares
            .Include(s => s.ChoreDefinition)
            .Where(s => s.ChildProfileId == childProfileId)
            .OrderBy(s => s.ChoreDefinitionId)
            .ToListAsync();
    }

    public async Task<ServiceResult> SetShareAsync(int childProfileId, int choreDefinitionId, int newPercent)
    {
        if (newPercent < 0 || newPercent > 100)
        {
            return ServiceResult.Fail("Share percent must be between 0 and 100.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        await EnsureSharesSeededAsync(context, childProfileId);

        var shares = await context.QolShares
            .Where(s => s.ChildProfileId == childProfileId)
            .ToListAsync();

        var changed = shares.FirstOrDefault(s => s.ChoreDefinitionId == choreDefinitionId);
        if (changed == null)
        {
            return ServiceResult.Fail("QOL routine not found for this child.");
        }

        // Blocked drag: nothing unlocked (other than the target) can absorb the delta.
        if (shares.All(s => s.ChoreDefinitionId == choreDefinitionId || s.IsLocked)
            && changed.SharePercent != newPercent)
        {
            return ServiceResult.Fail("Cannot change this share while all other routines are locked.");
        }

        var current = shares
            .Select(s => new QolShareValue(s.ChoreDefinitionId, s.SharePercent, s.IsLocked))
            .ToList();

        var rebalanced = QolRebalancer.Rebalance(current, choreDefinitionId, newPercent);

        var byChore = shares.ToDictionary(s => s.ChoreDefinitionId);
        foreach (var value in rebalanced)
        {
            if (byChore.TryGetValue(value.ChoreDefinitionId, out var row) && row.SharePercent != value.SharePercent)
            {
                row.SharePercent = value.SharePercent;
            }
        }

        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetLockAsync(int childProfileId, int choreDefinitionId, bool isLocked)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        await EnsureSharesSeededAsync(context, childProfileId);

        var share = await context.QolShares
            .FirstOrDefaultAsync(s => s.ChildProfileId == childProfileId && s.ChoreDefinitionId == choreDefinitionId);

        if (share == null)
        {
            return ServiceResult.Fail("QOL routine not found for this child.");
        }

        share.IsLocked = isLocked;
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Ensures every active inverse-fill routine has a share row (at 0%) for the child. Persists any
    /// newly seeded rows. Operates on the supplied context so callers share the same unit of work.
    /// </summary>
    private static async Task EnsureSharesSeededAsync(ApplicationDbContext context, int childProfileId)
    {
        var inverseFillChoreIds = await context.ChoreDefinitions
            .Where(c => c.IsInverseFill && c.IsActive)
            .Select(c => c.Id)
            .ToListAsync();

        if (inverseFillChoreIds.Count == 0)
        {
            return;
        }

        var existingChoreIds = await context.QolShares
            .Where(s => s.ChildProfileId == childProfileId)
            .Select(s => s.ChoreDefinitionId)
            .ToListAsync();

        var missing = inverseFillChoreIds.Except(existingChoreIds).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var choreId in missing)
        {
            context.QolShares.Add(new QolShare
            {
                ChildProfileId = childProfileId,
                ChoreDefinitionId = choreId,
                SharePercent = 0,
                IsLocked = false
            });
        }

        await context.SaveChangesAsync();
    }
}
