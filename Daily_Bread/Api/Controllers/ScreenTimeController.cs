using Daily_Bread.Data;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// The kid's screen-time meter: this week's pools (base / effective / floor /
/// at-risk), the live minute price of every chore, and recent ledger lines.
/// Children see their own; parents may query household members.
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

    public ScreenTimeController(
        IHouseholdGuard guard,
        IChildProfileService childProfiles,
        IScreenTimePricingService pricing,
        IFamilySettingsService familySettings,
        IDateProvider dateProvider,
        IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _guard = guard;
        _childProfiles = childProfiles;
        _pricing = pricing;
        _familySettings = familySettings;
        _dateProvider = dateProvider;
        _contextFactory = contextFactory;
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

        var response = ScreenTimeSummary.Build(
            target.User!.Id,
            weekStart,
            weekEnd,
            profile.WeekdayScreenTimeHours,
            profile.WeekendScreenTimeHours,
            pricing,
            snapshot,
            names,
            entries);

        return Ok(response);
    }
}
