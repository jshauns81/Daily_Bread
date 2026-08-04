using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// §3.1 — user-authored YAML themes, synced through the family's server so a
/// theme built on the iPad shows up on the Mac. The server stores text and
/// never parses YAML — the client validates, where a broken file is harmless
/// by design (§3.3). Themes are USER-bound (decided 2026-08-03): the list a
/// device syncs is the caller's own themes, not the household pool — my
/// themes follow ME across devices and don't appear in a sibling's picker.
/// Rows are still household-scoped underneath (slugs unique per family), and
/// legacy rows with no author remain visible to everyone rather than
/// vanishing. Overwriting or deleting someone else's theme stays
/// author-or-parent.
/// </summary>
[ApiController]
[Route("api/v1/themes")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ThemesController : ControllerBase
{
    /// <summary>§3.5 — the hidden achievement a kid finds by authoring a theme.</summary>
    private const string ThemeAuthorAchievement = "MAKE_IT_YOUR_OWN";

    private readonly IThemeFileService _themes;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAchievementService _achievements;

    public ThemesController(
        IThemeFileService themes,
        ICurrentUserContext currentUser,
        IAchievementService achievements)
    {
        _themes = themes;
        _currentUser = currentUser;
        _achievements = achievements;
    }

    private bool CallerIsParent => User.IsInRole("Parent") || User.IsInRole("Admin");

    public sealed record ThemeFileDto(string Id, string Yaml, string? AuthorUserId, DateTime UpdatedAt);
    public sealed record ThemePutRequest(string Yaml);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ThemeFileDto>>> List()
    {
        await _currentUser.InitializeAsync();
        if (_currentUser.HouseholdId is not Guid household)
        {
            return Ok(new List<ThemeFileDto>());
        }

        var themes = await _themes.ListAsync(household);
        return Ok(themes
            .Where(t => t.AuthorUserId == null || t.AuthorUserId == _currentUser.UserId)
            .Select(t => new ThemeFileDto(t.Slug, t.Yaml, t.AuthorUserId, t.UpdatedAt))
            .ToList());
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ThemeFileDto>> Put(string id, [FromBody] ThemePutRequest request)
    {
        await _currentUser.InitializeAsync();
        if (_currentUser.HouseholdId is not Guid household)
        {
            return NotFound(new ApiError("NoHousehold", "Your account has no household."));
        }

        var result = await _themes.UpsertAsync(
            household, id, request.Yaml ?? "", _currentUser.UserId, CallerIsParent);
        if (!result.Success)
        {
            return BadRequest(new ApiError("InvalidTheme", result.ErrorMessage ?? "Could not save the theme."));
        }

        // §3.5 — a child authoring their own theme is the moment worth marking.
        // AwardAchievementAsync is idempotent (already-earned returns null), so
        // every later save is a no-op. A parent saving a theme earns nothing:
        // the achievement is about a kid discovering he can author the app.
        if (!CallerIsParent)
        {
            await _achievements.AwardAchievementAsync(
                _currentUser.UserId, ThemeAuthorAchievement, notes: result.Data!.Slug);
        }

        var t = result.Data!;
        return Ok(new ThemeFileDto(t.Slug, t.Yaml, t.AuthorUserId, t.UpdatedAt));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<DeleteResponse>> Delete(string id)
    {
        await _currentUser.InitializeAsync();
        if (_currentUser.HouseholdId is not Guid household)
        {
            return NotFound(new ApiError("NotFound", "Theme not found."));
        }

        var result = await _themes.DeleteAsync(household, id, _currentUser.UserId, CallerIsParent);
        if (!result.Success)
        {
            return NotFound(new ApiError("NotFound", result.ErrorMessage ?? "Theme not found."));
        }

        return Ok(new DeleteResponse(true));
    }
}
