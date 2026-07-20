namespace Daily_Bread.Data.Models;

/// <summary>
/// A per-(QOL routine, child) share of the vacuum-fill distribution. Vacuum-fill (QOL) routines —
/// Read / Active / Brain — split each week's applied screen-time loss into explicit target-minute
/// shares that always sum to 100% (replacing the old equal division). A share can be locked to
/// exempt it from the mixer's proportional rebalancing. See MECHANICS_AMENDMENT.md §C.
/// </summary>
public class QolShare
{
    public int Id { get; set; }

    /// <summary>The QOL (vacuum-fill) routine this share is for.</summary>
    public int ChoreDefinitionId { get; set; }
    public ChoreDefinition ChoreDefinition { get; set; } = null!;

    /// <summary>The child this share belongs to.</summary>
    public int ChildProfileId { get; set; }
    public ChildProfile ChildProfile { get; set; } = null!;

    /// <summary>
    /// This routine's share of the applied weekly loss, as a whole percent (0–100). The child's
    /// shares across all QOL routines sum to 100%; a new routine enters at 0%.
    /// </summary>
    public int SharePercent { get; set; }

    /// <summary>
    /// When true, this share is pinned and exempt from proportional rebalancing when another
    /// segment is dragged in the mixer.
    /// </summary>
    public bool IsLocked { get; set; }
}
