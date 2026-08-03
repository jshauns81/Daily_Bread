using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// §3.1 — user-authored YAML themes, synced through the family's server so a
/// theme built on the iPad shows up on the Mac. The server stores text and
/// scopes it to the household; it never parses YAML — the client validates,
/// where a broken file is harmless by design (§3.3). Children can write
/// (authorship is the point); overwriting or deleting someone else's theme is
/// author-or-parent.
/// </summary>
[ApiController]
[Route("api/v1/themes")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ThemesController : ControllerBase
{
    private readonly IThemeFileService _themes;
    private readonly ICurrentUserContext _currentUser;

    public ThemesController(IThemeFileService themes, ICurrentUserContext currentUser)
    {
        _themes = themes;
        _currentUser = currentUser;
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
