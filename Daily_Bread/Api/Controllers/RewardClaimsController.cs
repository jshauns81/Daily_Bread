using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Real-world reward claims from TangibleReward achievements. A child sees their
/// own claims and where each stands; a parent sees the household's pending
/// claims and approves or rejects them (Cash credits the ledger, Item is marked
/// fulfilled — both handled by the service).
///
/// Household discipline is enforced HERE: the underlying service queries and
/// mutates by claim/user id without a household filter, so every endpoint scopes
/// to the caller's household first. A claim belonging to another family reads as
/// 404 and can never be listed, approved, or rejected.
/// </summary>
[ApiController]
[Route("api/v1/rewards/claims")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RewardClaimsController : ControllerBase
{
    private readonly IAchievementRewardClaimService _claims;
    private readonly IChoreManagementService _choreManagement;
    private readonly IHouseholdGuard _guard;
    private readonly ICurrentUserContext _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public RewardClaimsController(
        IAchievementRewardClaimService claims,
        IChoreManagementService choreManagement,
        IHouseholdGuard guard,
        ICurrentUserContext currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _claims = claims;
        _choreManagement = choreManagement;
        _guard = guard;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    private static RewardClaimDto Map(RewardClaimDisplay c) => new(
        c.Id,
        c.UserId,
        c.ChildName,
        c.AchievementName,
        c.AchievementIcon,
        c.RewardType.ToString(),
        c.CashAmount ?? 0m,
        c.ItemLabel,
        c.Status.ToString(),
        c.CreatedAt,
        c.DecidedAt,
        c.RejectionReason);

    /// <summary>
    /// A user's own claims (all statuses). A child queries self; a parent may
    /// pass a child's userId — resolved through the guard, so a cross-household
    /// id is 404, not a leak.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RewardClaimDto>>> List([FromQuery] string? userId, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        switch (target.Outcome)
        {
            case GuardOutcome.Forbidden:
                return Forbid(JwtBearerDefaults.AuthenticationScheme);
            case GuardOutcome.NotFound:
                return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        var claims = await _claims.GetClaimsForUserAsync(target.User!.Id);
        return Ok(claims.Select(Map).ToList());
    }

    /// <summary>The household's pending claims, oldest first. Parent/Admin only.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<ActionResult<IReadOnlyList<RewardClaimDto>>> Pending()
    {
        var householdChildIds = await HouseholdChildIdsAsync();
        var all = await _claims.GetPendingClaimsAsync();
        var scoped = all.Where(c => householdChildIds.Contains(c.UserId)).Select(Map).ToList();
        return Ok(scoped);
    }

    /// <summary>Approve a pending claim: Cash credits the ledger, Item is fulfilled.</summary>
    [HttpPost("{claimId:int}/approve")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<IActionResult> Approve(int claimId)
    {
        if (!await ClaimIsInCallerHouseholdAsync(claimId))
        {
            return NotFound(new ApiError("NotFound", "Reward claim not found."));
        }

        await _currentUser.InitializeAsync();
        var result = await _claims.ApproveClaimAsync(claimId, _currentUser.UserId);
        if (!result.Success)
        {
            return BadRequest(new ApiError("ApproveFailed", result.ErrorMessage ?? "Could not approve the reward."));
        }

        return NoContent();
    }

    /// <summary>Reject a pending claim with an optional short reason.</summary>
    [HttpPost("{claimId:int}/reject")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<IActionResult> Reject(int claimId, [FromBody] RewardClaimRejectRequest? request)
    {
        if (!await ClaimIsInCallerHouseholdAsync(claimId))
        {
            return NotFound(new ApiError("NotFound", "Reward claim not found."));
        }

        await _currentUser.InitializeAsync();
        var result = await _claims.RejectClaimAsync(claimId, _currentUser.UserId, request?.Reason);
        if (!result.Success)
        {
            return BadRequest(new ApiError("RejectFailed", result.ErrorMessage ?? "Could not reject the reward."));
        }

        return NoContent();
    }

    // ── Household scoping ────────────────────────────────────────────────

    /// <summary>The set of child user ids in the caller's household.</summary>
    private async Task<HashSet<string>> HouseholdChildIdsAsync()
    {
        await _currentUser.InitializeAsync();
        var household = _currentUser.HouseholdId;
        var ids = new HashSet<string>();
        if (household == null)
        {
            return ids;
        }

        var candidates = await _choreManagement.GetAssignableUsersAsync();
        foreach (var candidate in candidates)
        {
            var user = await _userManager.FindByIdAsync(candidate.Id);
            if (user?.HouseholdId != null && user.HouseholdId == household)
            {
                ids.Add(candidate.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// True only when the claim is pending AND owned by a child in the caller's
    /// household. Approve/Reject only ever act on pending claims, so checking the
    /// household's pending set both scopes the action and avoids a global lookup.
    /// </summary>
    private async Task<bool> ClaimIsInCallerHouseholdAsync(int claimId)
    {
        var householdChildIds = await HouseholdChildIdsAsync();
        if (householdChildIds.Count == 0)
        {
            return false;
        }

        var pending = await _claims.GetPendingClaimsAsync();
        return pending.Any(c => c.Id == claimId && householdChildIds.Contains(c.UserId));
    }
}
