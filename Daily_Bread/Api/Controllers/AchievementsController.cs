using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Achievements: the trophy case. Children see their own; parents may view a
/// household member's.
/// </summary>
[ApiController]
[Route("api/v1/achievements")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AchievementsController : ControllerBase
{
    private readonly IAchievementService _achievementService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IHouseholdGuard _guard;

    public AchievementsController(
        IAchievementService achievementService,
        ICurrentUserContext currentUser,
        IHouseholdGuard guard)
    {
        _achievementService = achievementService;
        _currentUser = currentUser;
        _guard = guard;
    }

    [HttpGet]
    public async Task<ActionResult<AchievementsResponse>> List([FromQuery] string? userId, CancellationToken ct)
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

        var all = await _achievementService.GetAllAchievementsAsync(target.User!.Id);
        var totalPoints = await _achievementService.GetTotalPointsAsync(target.User!.Id);

        // Locked hidden achievements stay mysterious: hint text only.
        var dtos = all.Select(a =>
        {
            var mysterious = a.IsHidden && !a.IsEarned && !a.IsVisibleBeforeUnlock;
            return new AchievementDto(
                a.Id,
                mysterious ? "???" : a.Name,
                mysterious ? (a.HiddenHint ?? "Keep going to discover this one.") : a.Description,
                mysterious ? (a.LockedIcon ?? "❓") : a.Icon,
                a.Category.ToString(),
                a.Rarity.ToString(),
                a.Points,
                a.IsEarned,
                a.EarnedAt,
                a.IsNew,
                !mysterious && a.ShowProgress,
                a.CurrentProgress,
                a.TargetProgress,
                a.ProgressPercent);
        }).ToList();

        return Ok(new AchievementsResponse(
            target.User!.Id,
            totalPoints,
            dtos.Count(d => d.IsEarned),
            dtos.Count,
            dtos));
    }

    /// <summary>Marks the caller's freshly-earned achievements as seen.</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> MarkSeen(CancellationToken ct)
    {
        await _currentUser.InitializeAsync();
        await _achievementService.MarkAchievementsAsSeenAsync(_currentUser.UserId);
        return NoContent();
    }
}
