using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Family members surface: see who's in the household and manage access
/// (reset a password, lock or unlock a member). Parent/Admin only.
///
/// Scoping is deliberate and defensive: the management SERVICE is not
/// household-scoped, so the member list is built from a query THIS controller
/// owns (filtered to the caller's household), and every action resolves its
/// target through the household guard first — a member of another family reads
/// as 404 and can never be listed or touched. Creating, deleting, and role
/// changes are intentionally NOT exposed here.
/// </summary>
[ApiController]
[Route("api/v1/family/members")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Parent,Admin")]
public class FamilyMembersController : ControllerBase
{
    private readonly IUserManagementService _users;
    private readonly IHouseholdGuard _guard;
    private readonly ICurrentUserContext _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public FamilyMembersController(
        IUserManagementService users,
        IHouseholdGuard guard,
        ICurrentUserContext currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _users = users;
        _guard = guard;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    /// <summary>The caller's household members, parents first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FamilyMemberDto>>> List()
    {
        await _currentUser.InitializeAsync();
        var household = _currentUser.HouseholdId;
        var members = new List<FamilyMemberDto>();
        if (household == null)
        {
            return Ok(members);
        }

        var users = await _userManager.Users
            .Where(u => u.HouseholdId == household)
            .ToListAsync();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            members.Add(new FamilyMemberDto(
                user.Id,
                user.UserName ?? "",
                roles.ToList(),
                user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow));
        }

        // Parents/Admins first, then by name — a stable, friendly order.
        return Ok(members
            .OrderByDescending(m => m.Roles.Contains("Parent") || m.Roles.Contains("Admin"))
            .ThenBy(m => m.UserName)
            .ToList());
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetMemberPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new ApiError("WeakPassword", "Enter a new password."));
        }

        var target = await Resolve(request.UserId, ct);
        if (target.Error != null) return target.Error;

        var result = await _users.ResetPasswordAsync(new ResetPasswordRequest
        {
            UserId = target.UserId!,
            NewPassword = request.NewPassword
        });
        if (!result.Success)
        {
            return BadRequest(new ApiError("ResetFailed", result.ErrorMessage ?? "Could not reset the password."));
        }

        return NoContent();
    }

    [HttpPost("{userId}/lock")]
    public async Task<IActionResult> Lock(string userId, CancellationToken ct)
    {
        var target = await Resolve(userId, ct);
        if (target.Error != null) return target.Error;

        // Never let a parent lock themselves out of the app.
        await _currentUser.InitializeAsync();
        if (target.UserId == _currentUser.UserId)
        {
            return BadRequest(new ApiError("CannotLockSelf", "You can't lock your own account."));
        }

        var result = await _users.LockoutUserAsync(target.UserId!);
        if (!result.Success)
        {
            return BadRequest(new ApiError("LockFailed", result.ErrorMessage ?? "Could not lock the account."));
        }
        return NoContent();
    }

    [HttpPost("{userId}/unlock")]
    public async Task<IActionResult> Unlock(string userId, CancellationToken ct)
    {
        var target = await Resolve(userId, ct);
        if (target.Error != null) return target.Error;

        var result = await _users.UnlockUserAsync(target.UserId!);
        if (!result.Success)
        {
            return BadRequest(new ApiError("UnlockFailed", result.ErrorMessage ?? "Could not unlock the account."));
        }
        return NoContent();
    }

    /// <summary>Resolve a target member to the caller's household, or an error result.</summary>
    private async Task<(string? UserId, ActionResult? Error)> Resolve(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (null, BadRequest(new ApiError("MissingUser", "A user id is required.")));
        }

        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        return target.Outcome switch
        {
            GuardOutcome.Forbidden => (null, Forbid(JwtBearerDefaults.AuthenticationScheme)),
            GuardOutcome.NotFound => (null, NotFound(new ApiError("UserNotFound", "User not found."))),
            _ => (target.User!.Id, null)
        };
    }
}
