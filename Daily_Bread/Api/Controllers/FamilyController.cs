using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Family-level feature switches. Anyone signed in can read them (the app
/// needs to know what to show); only parents can change them.
/// </summary>
[ApiController]
[Route("api/v1/family")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class FamilyController : ControllerBase
{
    private readonly IFamilySettingsService _settingsService;

    public FamilyController(IFamilySettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet("features")]
    public async Task<ActionResult<FamilyFeaturesDto>> Features(CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync();
        return Ok(new FamilyFeaturesDto(
            settings.EnableGoals,
            settings.EnableConfetti,
            settings.EnableStreaks));
    }

    /// <summary>Parents flip the switches. Only the feature toggles — nothing else.</summary>
    [HttpPut("features")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Parent,Admin")]
    public async Task<ActionResult<FamilyFeaturesDto>> UpdateFeatures(
        [FromBody] FamilyFeaturesDto request,
        CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync();
        settings.EnableGoals = request.EnableGoals;
        settings.EnableConfetti = request.EnableConfetti;
        settings.EnableStreaks = request.EnableStreaks;

        var result = await _settingsService.UpdateSettingsAsync(settings);
        if (!result.Success)
        {
            return BadRequest(new ApiError("UpdateFailed", result.ErrorMessage ?? "Could not update features."));
        }

        return Ok(new FamilyFeaturesDto(
            settings.EnableGoals,
            settings.EnableConfetti,
            settings.EnableStreaks));
    }
}
