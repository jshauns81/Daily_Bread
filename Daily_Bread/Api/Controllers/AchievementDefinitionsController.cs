using System.Text.Json;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using ServiceAchievementDto = Daily_Bread.Services.AchievementDto;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Parent authoring of achievement definitions: list (including inactive),
/// create, edit, and toggle-active over the existing management service. The
/// unlock condition rides the wire as typed params and is (de)serialized to the
/// stored JSON by AchievementConditionJson, so clients never hand-author it.
///
/// Achievements are global (no per-household field), so editing is gated to
/// Parent/Admin — AND to callers who actually belong to a household (2026-08-04
/// audit): a parent-role account with no family must not be able to read or
/// rewrite every family's badge definitions, reward amounts included. The real
/// multi-tenant fix is a HouseholdId on Achievement; until then this controller
/// fails closed for the family-less.
/// </summary>
[ApiController]
[Route("api/v1/achievements/definitions")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Parent,Admin")]
public class AchievementDefinitionsController : ControllerBase
{
    private readonly IAchievementManagementService _mgmt;
    private readonly ICurrentUserContext _currentUser;

    public AchievementDefinitionsController(IAchievementManagementService mgmt, ICurrentUserContext currentUser)
    {
        _mgmt = mgmt;
        _currentUser = currentUser;
    }

    /// <summary>Null when the caller may author definitions; otherwise the refusal.</summary>
    private async Task<ActionResult?> RequireHouseholdAsync()
    {
        await _currentUser.InitializeAsync();
        return _currentUser.HouseholdId == null
            ? Forbid(JwtBearerDefaults.AuthenticationScheme)
            : null;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AchievementDefinitionDto>>> List()
    {
        // Same fail-closed shape as the family list: no household, no data.
        await _currentUser.InitializeAsync();
        if (_currentUser.HouseholdId == null)
        {
            return Ok(new List<AchievementDefinitionDto>());
        }

        var all = await _mgmt.GetAllAchievementsAsync(includeInactive: true);
        return Ok(all.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AchievementDefinitionDto>> Create([FromBody] AchievementDefinitionWriteDto body)
    {
        if (await RequireHouseholdAsync() is { } refused)
        {
            return refused;
        }

        var (dto, error) = ToServiceDto(body, id: 0);
        if (error != null)
        {
            return BadRequest(new ApiError("InvalidAchievement", error));
        }

        var result = await _mgmt.CreateAchievementAsync(dto!);
        if (!result.Success || result.Data == null)
        {
            return BadRequest(new ApiError("CreateFailed", result.ErrorMessage ?? "Could not create the achievement."));
        }

        return Ok(ToDto(result.Data));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AchievementDefinitionWriteDto body)
    {
        if (await RequireHouseholdAsync() is { } refused)
        {
            return refused;
        }

        var (dto, error) = ToServiceDto(body, id);
        if (error != null)
        {
            return BadRequest(new ApiError("InvalidAchievement", error));
        }

        var result = await _mgmt.UpdateAchievementAsync(dto!);
        if (!result.Success)
        {
            return BadRequest(new ApiError("UpdateFailed", result.ErrorMessage ?? "Could not update the achievement."));
        }

        return NoContent();
    }

    [HttpPost("{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        if (await RequireHouseholdAsync() is { } refused)
        {
            return refused;
        }

        var result = await _mgmt.ToggleActiveAsync(id);
        if (!result.Success)
        {
            return BadRequest(new ApiError("ToggleFailed", result.ErrorMessage ?? "Could not toggle the achievement."));
        }

        return NoContent();
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    private static (ServiceAchievementDto?, string?) ToServiceDto(AchievementDefinitionWriteDto b, int id)
    {
        if (!Enum.TryParse<AchievementCategory>(b.Category, ignoreCase: true, out var category))
        {
            return (null, $"Unknown category '{b.Category}'.");
        }
        if (!Enum.TryParse<AchievementRarity>(b.Rarity, ignoreCase: true, out var rarity))
        {
            return (null, $"Unknown rarity '{b.Rarity}'.");
        }
        if (!Enum.TryParse<UnlockConditionType>(b.UnlockConditionType, ignoreCase: true, out var condType))
        {
            return (null, $"Unknown unlock condition '{b.UnlockConditionType}'.");
        }

        RewardClaimType? rewardType = null;
        if (!string.IsNullOrWhiteSpace(b.RewardType))
        {
            if (!Enum.TryParse<RewardClaimType>(b.RewardType, ignoreCase: true, out var rt))
            {
                return (null, $"Unknown reward type '{b.RewardType}'.");
            }
            rewardType = rt;
        }

        var condParams = new AchievementConditionParams(
            Count: b.Count,
            Days: b.Days,
            Weeks: b.Weeks,
            Amount: b.ConditionAmount > 0 ? b.ConditionAmount : null,
            ChoreId: b.ChoreId,
            BeforeHour: b.BeforeHour,
            DayType: b.DayType);

        var dto = new ServiceAchievementDto
        {
            Id = id,
            Name = b.Name,
            Description = b.Description,
            HiddenHint = b.HiddenHint,
            Icon = b.Icon,
            LockedIcon = b.LockedIcon,
            Category = category,
            Rarity = rarity,
            Points = b.Points,
            SortOrder = b.SortOrder,
            IsHidden = b.IsHidden,
            IsLegendary = b.IsLegendary,
            IsVisibleBeforeUnlock = b.IsVisibleBeforeUnlock,
            IsActive = b.IsActive,
            UnlockConditionType = condType,
            UnlockConditionValue = AchievementConditionJson.Build(condType, condParams),
            ProgressTarget = b.ProgressTarget,
            RewardType = rewardType,
            RewardCashAmount = rewardType == RewardClaimType.Cash ? b.RewardCashAmount : null,
            RewardItemLabel = rewardType == RewardClaimType.Item ? b.RewardItemLabel : null,
            RewardItemEstValue = rewardType == RewardClaimType.Item && b.RewardItemEstValue > 0
                ? b.RewardItemEstValue
                : null
        };

        return (dto, null);
    }

    private static AchievementDefinitionDto ToDto(Achievement a)
    {
        var cp = AchievementConditionJson.Parse(a.UnlockConditionType, a.UnlockConditionValue);
        var (rewardType, cash, itemLabel, itemEst) = ParseReward(a);

        return new AchievementDefinitionDto(
            a.Id,
            a.Code,
            a.Name,
            a.Description,
            a.HiddenHint,
            a.Icon,
            a.LockedIcon,
            a.Category.ToString(),
            a.Rarity.ToString(),
            a.Points,
            a.SortOrder,
            a.IsHidden,
            a.IsLegendary,
            a.IsVisibleBeforeUnlock,
            a.IsActive,
            a.UnlockConditionType.ToString(),
            cp.Count,
            cp.Days,
            cp.Weeks,
            cp.Amount ?? 0m,
            cp.ChoreId,
            cp.BeforeHour,
            cp.DayType,
            a.ProgressTarget,
            rewardType,
            cash,
            itemLabel,
            itemEst);
    }

    /// <summary>Reads the tangible reward back out of BonusType/BonusValue.</summary>
    private static (string? Type, decimal Cash, string? ItemLabel, decimal ItemEst) ParseReward(Achievement a)
    {
        if (a.BonusType != AchievementBonusType.TangibleReward || string.IsNullOrWhiteSpace(a.BonusValue))
        {
            return (null, 0m, null, 0m);
        }

        try
        {
            using var doc = JsonDocument.Parse(a.BonusValue);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "cash")
            {
                var amount = root.TryGetProperty("amount", out var am) && am.TryGetDecimal(out var m) ? m : 0m;
                return ("Cash", amount, null, 0m);
            }
            if (type == "item")
            {
                var label = root.TryGetProperty("label", out var l) ? l.GetString() : null;
                var est = root.TryGetProperty("est_value", out var e) && e.TryGetDecimal(out var ev) ? ev : 0m;
                return ("Item", 0m, label, est);
            }
        }
        catch (JsonException)
        {
            // Fall through to "no reward".
        }

        return (null, 0m, null, 0m);
    }
}
