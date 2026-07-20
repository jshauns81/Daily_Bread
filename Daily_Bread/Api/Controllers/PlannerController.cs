using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// The parent's planner: full chore-definition CRUD, drag-and-drop
/// reordering, activate/deactivate, and the list of children a chore can be
/// assigned to. Every action is parent-only (Parent or Admin role) — children
/// never see this surface. Household discipline: a chore assigned to a user
/// outside the caller's household is invisible (filtered from lists, 404 on
/// direct id access, never distinguished from missing); unassigned chores are
/// visible to any parent.
/// </summary>
[ApiController]
[Route("api/v1/planner")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PlannerController : ControllerBase
{
    private readonly IChoreManagementService _choreManagement;
    private readonly IHouseholdGuard _guard;
    private readonly ICurrentUserContext _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public PlannerController(
        IChoreManagementService choreManagement,
        IHouseholdGuard guard,
        ICurrentUserContext currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _choreManagement = choreManagement;
        _guard = guard;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    /// <summary>
    /// All chore definitions the caller may see, in the kid's list order
    /// (SortOrder, then name — the service's order, preserved). Optional
    /// userId narrows to one child's chores; it resolves through the guard
    /// first so a cross-household id reads as 404, not an empty list.
    /// </summary>
    [HttpGet("chores")]
    public async Task<ActionResult<PlannerChoreListResponse>> GetChores(
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        string? filterUserId = null;
        if (!string.IsNullOrEmpty(userId))
        {
            var target = await _guard.ResolveTargetUserAsync(userId, ct);
            if (target.Outcome == GuardOutcome.Forbidden)
            {
                return Forbid(JwtBearerDefaults.AuthenticationScheme);
            }
            if (target.Outcome == GuardOutcome.NotFound)
            {
                return NotFound(new ApiError("UserNotFound", "User not found."));
            }
            filterUserId = target.User!.Id;
        }

        var chores = await _choreManagement.GetAllChoresAsync(includeInactive);
        var household = _currentUser.HouseholdId;

        IEnumerable<ChoreDefinition> visible = chores.Where(c => IsHouseholdVisible(c, household));
        if (filterUserId != null)
        {
            visible = visible.Where(c => c.AssignedUserId == filterUserId);
        }

        return Ok(new PlannerChoreListResponse(
            visible.Select(PlannerMapping.FromEntity).ToList()));
    }

    /// <summary>
    /// Creates a chore. Wire-level parsing (kind, schedule type, day names)
    /// fails as 400 InvalidChore before anything touches the guard or the
    /// service; a non-null assignedUserId must resolve inside the caller's
    /// household (404 UserNotFound otherwise, so ids never leak). Returns the
    /// created chore as the client will see it from then on.
    /// </summary>
    [HttpPost("chores")]
    public async Task<ActionResult<PlannerChoreDto>> CreateChore(
        [FromBody] ChoreWriteRequest request,
        CancellationToken ct = default)
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        var invalid = ValidateWireEnums(request);
        if (invalid != null)
        {
            return invalid;
        }

        var userError = await ResolveAssignedUserAsync(request.AssignedUserId, ct);
        if (userError != null)
        {
            return userError;
        }

        var result = await _choreManagement.CreateChoreAsync(PlannerMapping.ToServiceDto(request));
        if (!result.Success)
        {
            return BadRequest(new ApiError(
                "InvalidChore", result.ErrorMessage ?? "Could not create the chore."));
        }

        // The service reloads the AssignedUser navigation before returning,
        // so the DTO carries the assigned user's name without a re-read.
        return Ok(PlannerMapping.FromEntity(result.Data!));
    }

    /// <summary>
    /// Updates a chore. The household check on the chore being edited runs
    /// BEFORE any write — a chore assigned outside the caller's household is
    /// 404 ChoreNotFound, indistinguishable from missing. Service validation
    /// failures map to 400 InvalidChore. Returns the fresh state re-read from
    /// the service so the response reflects exactly what was persisted.
    /// </summary>
    [HttpPut("chores/{id:int}")]
    public async Task<ActionResult<PlannerChoreDto>> UpdateChore(
        int id,
        [FromBody] ChoreWriteRequest request,
        CancellationToken ct = default)
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        var invalid = ValidateWireEnums(request);
        if (invalid != null)
        {
            return invalid;
        }

        var existing = await _choreManagement.GetChoreByIdAsync(id);
        if (existing == null || !IsHouseholdVisible(existing, _currentUser.HouseholdId))
        {
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        var userError = await ResolveAssignedUserAsync(request.AssignedUserId, ct);
        if (userError != null)
        {
            return userError;
        }

        var result = await _choreManagement.UpdateChoreAsync(PlannerMapping.ToServiceDto(request, id));
        if (!result.Success)
        {
            return BadRequest(new ApiError(
                "InvalidChore", result.ErrorMessage ?? "Could not update the chore."));
        }

        var updated = await _choreManagement.GetChoreByIdAsync(id);
        if (updated == null)
        {
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        return Ok(PlannerMapping.FromEntity(updated));
    }

    /// <summary>
    /// Deletes a chore (the service soft-deletes to inactive when logs exist;
    /// the wire doesn't distinguish — see the contract). Household check
    /// first, so cross-household ids read as 404 like everywhere else.
    /// </summary>
    [HttpDelete("chores/{id:int}")]
    public async Task<ActionResult<DeleteResponse>> DeleteChore(int id)
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        var existing = await _choreManagement.GetChoreByIdAsync(id);
        if (existing == null || !IsHouseholdVisible(existing, _currentUser.HouseholdId))
        {
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        var result = await _choreManagement.DeleteChoreAsync(id);
        if (!result.Success)
        {
            // The service only fails when the chore is missing (raced away
            // between our read and its read), so this stays a 404.
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        return Ok(new DeleteResponse(true));
    }

    /// <summary>
    /// Flips a chore between active and inactive and returns the fresh state,
    /// so the client updates its row in one round trip.
    /// </summary>
    [HttpPost("chores/{id:int}/toggle-active")]
    public async Task<ActionResult<PlannerChoreDto>> ToggleActive(int id)
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        var existing = await _choreManagement.GetChoreByIdAsync(id);
        if (existing == null || !IsHouseholdVisible(existing, _currentUser.HouseholdId))
        {
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        var result = await _choreManagement.ToggleActiveAsync(id);
        if (!result.Success)
        {
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        var updated = await _choreManagement.GetChoreByIdAsync(id);
        if (updated == null)
        {
            return NotFound(new ApiError("ChoreNotFound", "Chore not found."));
        }

        return Ok(PlannerMapping.FromEntity(updated));
    }

    /// <summary>
    /// Batch reorder after a drag-and-drop. Every id in the batch is
    /// household-checked against one full read (including inactive — hidden
    /// rows are still reorderable) before anything is written: one unknown or
    /// out-of-household id fails the whole batch as 400 InvalidChore, without
    /// saying which id was the problem.
    /// </summary>
    [HttpPut("chores/order")]
    public async Task<IActionResult> ReorderChores(
        [FromBody] ChoreOrderRequest request,
        CancellationToken ct = default)
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        var items = request.Items ?? [];
        if (items.Count == 0)
        {
            return NoContent();
        }

        var all = await _choreManagement.GetAllChoresAsync(includeInactive: true);
        var household = _currentUser.HouseholdId;
        var visibleIds = all
            .Where(c => IsHouseholdVisible(c, household))
            .Select(c => c.Id)
            .ToHashSet();

        if (items.Any(i => !visibleIds.Contains(i.ChoreDefinitionId)))
        {
            return BadRequest(new ApiError("InvalidChore", "One or more chores not found."));
        }

        var result = await _choreManagement.UpdateSortOrderBatchAsync(
            items.Select(i => (i.ChoreDefinitionId, i.SortOrder)).ToList());
        if (!result.Success)
        {
            return BadRequest(new ApiError(
                "InvalidChore", result.ErrorMessage ?? "Could not reorder chores."));
        }

        return NoContent();
    }

    /// <summary>
    /// The children a chore can be assigned to. The service returns every
    /// Child-role user in the system; this narrows to the caller's household
    /// (a child without a household is excluded — the guard would refuse the
    /// assignment anyway, so offering them would be a dead end).
    /// </summary>
    [HttpGet("assignable")]
    public async Task<ActionResult<AssignableChildrenResponse>> GetAssignableChildren()
    {
        var forbid = await RequireParentAsync();
        if (forbid != null)
        {
            return forbid;
        }

        var household = _currentUser.HouseholdId;
        var candidates = await _choreManagement.GetAssignableUsersAsync();

        var children = new List<AssignableChildDto>();
        foreach (var candidate in candidates)
        {
            // The service already uses UserManager, so per-id lookups are the
            // established (and, for a family-sized list, cheap) path to the
            // HouseholdId its DTO doesn't carry.
            var user = await _userManager.FindByIdAsync(candidate.Id);
            if (user?.HouseholdId != null && user.HouseholdId == household)
            {
                children.Add(new AssignableChildDto(candidate.Id, candidate.UserName));
            }
        }

        return Ok(new AssignableChildrenResponse(children));
    }

    /// <summary>
    /// Every planner action is a parental act, so the role is required
    /// explicitly — the guard alone only forbids cross-user access (imitating
    /// ScreenTimeController.UpdateSettings). Also initializes the current-user
    /// context, so callers may read HouseholdId afterwards.
    /// </summary>
    private async Task<ActionResult?> RequireParentAsync()
    {
        await _currentUser.InitializeAsync();
        if (!_currentUser.IsInRole("Parent") && !_currentUser.IsInRole("Admin"))
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        return null;
    }

    /// <summary>
    /// Wire-level validation the service can't do, because the wire carries
    /// enums and day names as strings. Returns the 400 InvalidChore result,
    /// or null when the request parses cleanly — after which
    /// PlannerMapping.ToServiceDto is guaranteed not to throw.
    /// </summary>
    private ActionResult? ValidateWireEnums(ChoreWriteRequest request)
    {
        if (!PlannerMapping.TryParseKind(request.Kind, out _))
        {
            return BadRequest(new ApiError(
                "InvalidChore", $"Unknown chore kind '{request.Kind}'."));
        }

        if (!PlannerMapping.TryParseScheduleType(request.ScheduleType, out _))
        {
            return BadRequest(new ApiError(
                "InvalidChore", $"Unknown schedule type '{request.ScheduleType}'."));
        }

        if (!PlannerMapping.TryParseDays(request.ActiveDays, out _, out var unknownDay))
        {
            return BadRequest(new ApiError(
                "InvalidChore", $"Unknown day name '{unknownDay}'."));
        }

        return null;
    }

    /// <summary>
    /// A non-null assignedUserId must resolve inside the caller's household.
    /// Cross-household or unknown ids read as 404 UserNotFound — never
    /// distinguished, so other households' ids don't leak. Null (unassigned)
    /// is always fine.
    /// </summary>
    private async Task<ActionResult?> ResolveAssignedUserAsync(string? assignedUserId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(assignedUserId))
        {
            return null;
        }

        var target = await _guard.ResolveTargetUserAsync(assignedUserId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        return null;
    }

    /// <summary>
    /// A chore is household-visible when unassigned, or when its assigned
    /// user isn't in a DIFFERENT household. An assigned user with a null
    /// HouseholdId (legacy rows) stays visible rather than orphaning the
    /// chore for every parent. Relies on the AssignedUser navigation the
    /// service Include()s on all read paths.
    /// </summary>
    private static bool IsHouseholdVisible(ChoreDefinition chore, Guid? callerHouseholdId)
    {
        if (chore.AssignedUserId == null)
        {
            return true;
        }

        var assignedHousehold = chore.AssignedUser?.HouseholdId;
        return assignedHousehold == null || assignedHousehold == callerHouseholdId;
    }
}
