using Daily_Bread.Data;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Api;

/// <summary>Outcome of a household-scoped target resolution.</summary>
public enum GuardOutcome
{
    Ok,
    /// <summary>Caller lacks the role to act on another user.</summary>
    Forbidden,
    /// <summary>Target doesn't exist or is outside the caller's household (never distinguished, to avoid leaking ids).</summary>
    NotFound
}

public sealed record TargetUser(ApplicationUser? User, GuardOutcome Outcome)
{
    public static TargetUser Fail(GuardOutcome outcome) => new(null, outcome);
}

/// <summary>
/// Centralized household-isolation checks for the API layer (plan §5b
/// discipline #1: every API query household-scoped, even with one household).
/// </summary>
public interface IHouseholdGuard
{
    /// <summary>
    /// Resolves the user an action targets. Null/self → the caller.
    /// Another user → caller must be Parent/Admin and the target must be in
    /// the caller's household.
    /// </summary>
    Task<TargetUser> ResolveTargetUserAsync(string? requestedUserId, CancellationToken ct = default);

    /// <summary>
    /// True if the chore log's assigned user belongs to the caller's household.
    /// Used to guard approve/help-respond, which address logs by id.
    /// </summary>
    Task<bool> ChoreLogIsInCallerHouseholdAsync(int choreLogId, CancellationToken ct = default);
}

public class HouseholdGuard : IHouseholdGuard
{
    private readonly ICurrentUserContext _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

    public HouseholdGuard(
        ICurrentUserContext currentUser,
        UserManager<ApplicationUser> userManager,
        IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _currentUser = currentUser;
        _userManager = userManager;
        _contextFactory = contextFactory;
    }

    public async Task<TargetUser> ResolveTargetUserAsync(string? requestedUserId, CancellationToken ct = default)
    {
        await _currentUser.InitializeAsync();

        if (string.IsNullOrEmpty(requestedUserId) || requestedUserId == _currentUser.UserId)
        {
            var self = await _userManager.FindByIdAsync(_currentUser.UserId);
            return self == null
                ? TargetUser.Fail(GuardOutcome.NotFound)
                : new TargetUser(self, GuardOutcome.Ok);
        }

        if (!_currentUser.IsInRole("Parent") && !_currentUser.IsInRole("Admin"))
        {
            return TargetUser.Fail(GuardOutcome.Forbidden);
        }

        var target = await _userManager.FindByIdAsync(requestedUserId);
        if (target == null
            || target.HouseholdId == null
            || target.HouseholdId != _currentUser.HouseholdId)
        {
            return TargetUser.Fail(GuardOutcome.NotFound);
        }

        return new TargetUser(target, GuardOutcome.Ok);
    }

    public async Task<bool> ChoreLogIsInCallerHouseholdAsync(int choreLogId, CancellationToken ct = default)
    {
        await _currentUser.InitializeAsync();
        var callerHousehold = _currentUser.HouseholdId;
        if (callerHousehold == null)
        {
            return false;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var assignedUserId = await db.ChoreLogs
            .Where(cl => cl.Id == choreLogId)
            .Select(cl => cl.ChoreDefinition.AssignedUserId)
            .FirstOrDefaultAsync(ct);

        if (assignedUserId == null)
        {
            return false;
        }

        var targetHousehold = await db.Users
            .Where(u => u.Id == assignedUserId)
            .Select(u => u.HouseholdId)
            .FirstOrDefaultAsync(ct);

        return targetHousehold != null && targetHousehold == callerHousehold;
    }
}
