using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// The kid's screen-time meter: this week's pools (base / effective / floor /
/// at-risk), the live minute price of every chore, and recent ledger lines.
/// Children see their own; parents may query household members. Also serves
/// the "At Risk Today" card (MECHANICS_AMENDMENT.md §E) and the parent-only
/// settings update.
/// </summary>
[ApiController]
[Route("api/v1/screentime")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ScreenTimeController : ControllerBase
{
    private readonly IHouseholdGuard _guard;
    private readonly IChildProfileService _childProfiles;
    private readonly IScreenTimePricingService _pricing;
    private readonly IFamilySettingsService _familySettings;
    private readonly IDateProvider _dateProvider;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IAtRiskService _atRisk;
    private readonly ICurrentUserContext _currentUser;

    public ScreenTimeController(
        IHouseholdGuard guard,
        IChildProfileService childProfiles,
        IScreenTimePricingService pricing,
        IFamilySettingsService familySettings,
        IDateProvider dateProvider,
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IAtRiskService atRisk,
        ICurrentUserContext currentUser)
    {
        _guard = guard;
        _childProfiles = childProfiles;
        _pricing = pricing;
        _familySettings = familySettings;
        _dateProvider = dateProvider;
        _contextFactory = contextFactory;
        _atRisk = atRisk;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ScreenTimeResponse>> Get(
        [FromQuery] string? userId,
        [FromQuery] int entryLimit = 20,
        CancellationToken ct = default)
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

        var profile = await _childProfiles.GetProfileByUserIdAsync(target.User!.Id);
        if (profile == null)
        {
            // Parents (and any user without a child profile) have no meter.
            return NotFound(new ApiError(
                "ChildProfileNotFound", "This user has no child profile."));
        }

        var response = await BuildScreenTimeResponseAsync(target.User!.Id, profile, entryLimit, ct);
        return Ok(response with { BirthDate = profile.BirthDate });
    }

    /// <summary>
    /// The kid's "At Risk Today" card (MECHANICS_AMENDMENT.md §E): what is on
    /// the line today with exact stakes, or the calm state with at most one
    /// preview line for the next pinch.
    /// </summary>
    [HttpGet("atrisk")]
    public async Task<ActionResult<AtRiskResponse>> AtRisk(
        [FromQuery] string? userId,
        CancellationToken ct = default)
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

        var profile = await _childProfiles.GetProfileByUserIdAsync(target.User!.Id);
        if (profile == null)
        {
            return NotFound(new ApiError(
                "ChildProfileNotFound", "This user has no child profile."));
        }

        var computation = await _atRisk.ComputeAsync(
            target.User!.Id, profile.Id, _dateProvider.Today);

        var items = computation.Items
            .Select(i => new AtRiskItemDto(
                i.ChoreDefinitionId,
                i.Name,
                i.Urgency.ToString(),
                i.Detail,
                i.MoneyAtRisk,
                i.MinutesAtRisk))
            .ToList();

        return Ok(new AtRiskResponse(
            computation.UserId,
            computation.Date,
            items,
            computation.TotalMoneyAtRisk,
            computation.TotalMinutesAtRisk,
            computation.PreviewLine));
    }

    /// <summary>
    /// Parent-only: updates a child's screen-time settings (pool hours, routine
    /// payout, at-risk percents) and returns the fresh meter — same shape as
    /// GET — so the client refreshes in one round trip. Validation lives in
    /// ChildProfileService; failures map to 400 InvalidSettings.
    /// </summary>
    [HttpPut("settings")]
    public async Task<ActionResult<ScreenTimeResponse>> UpdateSettings(
        [FromBody] ScreenTimeSettingsRequest request,
        CancellationToken ct = default)
    {
        // The guard only forbids cross-user access; tuning settings is a
        // parental act even on paper-self, so the role is required explicitly.
        await _currentUser.InitializeAsync();
        if (!_currentUser.IsInRole("Parent") && !_currentUser.IsInRole("Admin"))
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }

        // The contract makes userId required — parents always tune a specific kid.
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new ApiError("InvalidSettings", "userId is required."));
        }

        var target = await _guard.ResolveTargetUserAsync(request.UserId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        var profile = await _childProfiles.GetProfileByUserIdAsync(target.User!.Id);
        if (profile == null)
        {
            return NotFound(new ApiError(
                "ChildProfileNotFound", "This user has no child profile."));
        }

        var result = await _childProfiles.UpdateScreenTimeSettingsAsync(
            profile.Id,
            request.WeekdayHours,
            request.WeekendHours,
            request.WeeklyRoutinePayout,
            request.WeekdayAtRiskPercent,
            request.WeekendAtRiskPercent,
            request.MinutesPerImportancePoint ?? profile.MinutesPerImportancePoint,
            request.BirthDate);

        if (!result.Success)
        {
            return BadRequest(new ApiError(
                "InvalidSettings", result.ErrorMessage ?? "Invalid settings."));
        }

        // Re-read so the response reflects exactly what was just written.
        var updatedProfile = await _childProfiles.GetProfileByUserIdAsync(target.User!.Id);
        if (updatedProfile == null)
        {
            return NotFound(new ApiError(
                "ChildProfileNotFound", "This user has no child profile."));
        }

        var response = await BuildScreenTimeResponseAsync(
            target.User!.Id, updatedProfile, entryLimit: 20, ct);
        return Ok(response with { BirthDate = updatedProfile.BirthDate });
    }

    /// <summary>
    /// The shared GET-shaped assembly: week window, live pricing, the frozen
    /// snapshot if reconciliation wrote one, priced-chore names, and recent
    /// ledger lines — handed to the pure ScreenTimeSummary builder.
    /// </summary>
    private async Task<ScreenTimeResponse> BuildScreenTimeResponseAsync(
        string userId,
        ChildProfile profile,
        int entryLimit,
        CancellationToken ct)
    {
        var today = _dateProvider.Today;
        var weekStart = await _familySettings.GetWeekStartForDateAsync(today);
        var weekEnd = await _familySettings.GetWeekEndForDateAsync(today);

        var pricing = await _pricing.GetWeekPricingAsync(profile.Id, today);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        // This week's frozen snapshot, if last Sunday's reconciliation wrote one.
        var snapshot = await db.ChildWeeklyScreenTimeBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.ChildProfileId == profile.Id && b.WeekStartDate == weekStart, ct);

        // Names for the priced chores.
        var pricedIds = pricing.ChorePrices.Keys.ToList();
        var names = pricedIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.ChoreDefinitions
                .AsNoTracking()
                .Where(c => pricedIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Recent ledger lines, newest first (the "what happened" story).
        entryLimit = Math.Clamp(entryLimit, 1, 100);
        var rawEntries = await db.ScreenTimeEntries
            .AsNoTracking()
            .Where(e => e.ChildProfileId == profile.Id)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(entryLimit)
            .Select(e => new
            {
                e.Id,
                e.WeekStartDate,
                e.Pool,
                e.Kind,
                ChoreName = e.ChoreDefinition != null ? e.ChoreDefinition.Name : null,
                e.Minutes,
                e.Note,
                e.CreatedAt
            })
            .ToListAsync(ct);

        // Enum→string mapping happens client-side; providers don't translate ToString().
        var entries = rawEntries
            .Select(e => new ScreenTimeEntryDto(
                e.Id, e.WeekStartDate, e.Pool.ToString(), e.Kind.ToString(),
                e.ChoreName, e.Minutes, e.Note, e.CreatedAt))
            .ToList();

        return ScreenTimeSummary.Build(
            userId,
            weekStart,
            weekEnd,
            profile.WeekdayScreenTimeHours,
            profile.WeekendScreenTimeHours,
            pricing,
            snapshot,
            names,
            entries,
            profile.WeeklyRoutinePayout,
            profile.MinutesPerImportancePoint);
    }
}
