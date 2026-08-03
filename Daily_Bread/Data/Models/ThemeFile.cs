namespace Daily_Bread.Data.Models;

/// <summary>
/// One user-authored YAML theme (§3.1) — themes live on the family's server so
/// a theme built on the iPad shows up on the Mac. The server stores the text
/// verbatim and stays out of the parsing business: validation is the client's
/// job, and a broken file can't hurt anything there by design (§3.3).
/// </summary>
public class ThemeFile
{
    public int Id { get; set; }

    /// <summary>Owning household — themes never cross families.</summary>
    public required Guid HouseholdId { get; set; }

    /// <summary>The theme's meta.id — unique within the household.</summary>
    public required string Slug { get; set; }

    /// <summary>The YAML text, verbatim.</summary>
    public required string Yaml { get; set; }

    /// <summary>Who authored it — children author themes by design.</summary>
    public string? AuthorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
