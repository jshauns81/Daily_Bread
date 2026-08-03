using System.Text.RegularExpressions;
using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

public interface IThemeFileService
{
    Task<List<ThemeFile>> ListAsync(Guid householdId);
    Task<ServiceResult<ThemeFile>> UpsertAsync(Guid householdId, string slug, string yaml, string authorUserId, bool authorIsParent);
    Task<ServiceResult> DeleteAsync(Guid householdId, string slug, string requestingUserId, bool requesterIsParent);
}

/// <summary>
/// §3.1 — themes live on the family's server. The service stores YAML verbatim
/// and never parses it: the client is where validation lives, and a broken file
/// is harmless there by design (§3.3). Children author themes on purpose —
/// that's the whole point (THEME_OWNERSHIP.md) — so writes are household-scoped
/// but not parent-gated. Overwriting someone ELSE'S theme is parent-or-author.
/// </summary>
public partial class ThemeFileService : IThemeFileService
{
    /// <summary>Slugs are meta.id: short, lowercase, url/file-safe.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SlugPattern();

    /// <summary>A theme is ~40 lines; 32KB is an order of magnitude of headroom.</summary>
    public const int MaxYamlBytes = 32 * 1024;

    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

    public ThemeFileService(IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<ThemeFile>> ListAsync(Guid householdId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ThemeFiles
            .Where(t => t.HouseholdId == householdId)
            .OrderBy(t => t.Slug)
            .ToListAsync();
    }

    public async Task<ServiceResult<ThemeFile>> UpsertAsync(
        Guid householdId, string slug, string yaml, string authorUserId, bool authorIsParent)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(normalized))
        {
            return ServiceResult<ThemeFile>.Fail(
                "Theme ids are lowercase letters, digits and dashes (like harbor-night).");
        }

        if (string.IsNullOrWhiteSpace(yaml))
        {
            return ServiceResult<ThemeFile>.Fail("The theme file is empty.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(yaml) > MaxYamlBytes)
        {
            return ServiceResult<ThemeFile>.Fail("Theme files are small — this one is far too big.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        var existing = await context.ThemeFiles
            .FirstOrDefaultAsync(t => t.HouseholdId == householdId && t.Slug == normalized);

        if (existing == null)
        {
            var created = new ThemeFile
            {
                HouseholdId = householdId,
                Slug = normalized,
                Yaml = yaml,
                AuthorUserId = authorUserId
            };
            context.ThemeFiles.Add(created);
            await context.SaveChangesAsync();
            return ServiceResult<ThemeFile>.Ok(created);
        }

        // A kid's theme is HIS — a sibling can't quietly overwrite it. The
        // author can, and a parent can.
        if (!authorIsParent && existing.AuthorUserId != authorUserId)
        {
            return ServiceResult<ThemeFile>.Fail("That theme id belongs to someone else — pick your own id.");
        }

        existing.Yaml = yaml;
        existing.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return ServiceResult<ThemeFile>.Ok(existing);
    }

    public async Task<ServiceResult> DeleteAsync(
        Guid householdId, string slug, string requestingUserId, bool requesterIsParent)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var normalized = slug.Trim().ToLowerInvariant();
        var existing = await context.ThemeFiles
            .FirstOrDefaultAsync(t => t.HouseholdId == householdId && t.Slug == normalized);
        if (existing == null)
        {
            return ServiceResult.Fail("Theme not found.");
        }

        if (!requesterIsParent && existing.AuthorUserId != requestingUserId)
        {
            return ServiceResult.Fail("Only the author or a parent can delete this theme.");
        }

        context.ThemeFiles.Remove(existing);
        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }
}
