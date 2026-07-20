namespace Daily_Bread.Services;

/// <summary>
/// A single QOL (vacuum-fill) routine's share as seen by the rebalancer: a whole-percent share and
/// whether it is locked (pinned). Pure value type so the algorithm has no EF/database dependency.
/// See MECHANICS_AMENDMENT.md §C.
/// </summary>
public readonly record struct QolShareValue(int ChoreDefinitionId, int SharePercent, bool IsLocked);

/// <summary>
/// Pure, deterministic rebalancing algorithm for the QOL share mixer (MECHANICS_AMENDMENT.md §C).
/// The child's shares across all QOL routines always sum to 100%. Dragging one segment redistributes
/// the opposite delta across the other UNLOCKED segments proportionally to their current shares,
/// clamps at 0, snaps to multiples of 5, and repairs any rounding drift so the set still totals 100.
/// </summary>
public static class QolRebalancer
{
    private const int Snap = 5;

    /// <summary>
    /// Rebalances the share set after the segment identified by <paramref name="changedChoreId"/> is
    /// set to <paramref name="newPercent"/>.
    /// <para>
    /// Rules: the requested percent is clamped to 0..100 and snapped to a multiple of 5, then bounded so
    /// it never exceeds what the unlocked pool can give up (locked segments are fixed). The opposite delta
    /// is spread across unlocked segments (j ≠ i) as <c>Δj = −Δ × (share_j ÷ Σ unlocked shares excluding i)</c>,
    /// each clamped at 0 and snapped to 5. Any remaining rounding difference is pushed onto the largest
    /// unlocked segment(s) so the set sums to exactly 100.
    /// </para>
    /// <para>
    /// If there is no unlocked segment other than the changed one to absorb the delta, the change is
    /// BLOCKED and the input list is returned UNCHANGED (same contents). Callers should treat an unchanged
    /// result to a genuine request as a no-op / blocked drag. An unknown <paramref name="changedChoreId"/>
    /// is likewise a no-op.
    /// </para>
    /// </summary>
    public static IReadOnlyList<QolShareValue> Rebalance(
        IReadOnlyList<QolShareValue> current, int changedChoreId, int newPercent)
    {
        ArgumentNullException.ThrowIfNull(current);

        var i = -1;
        for (var k = 0; k < current.Count; k++)
        {
            if (current[k].ChoreDefinitionId == changedChoreId)
            {
                i = k;
                break;
            }
        }

        // Unknown segment → nothing to change.
        if (i < 0)
        {
            return current;
        }

        // Locked segments other than i are fixed; unlocked ones absorb the delta.
        var lockedOthers = 0;
        var unlockedIdx = new List<int>();
        for (var k = 0; k < current.Count; k++)
        {
            if (k == i)
            {
                continue;
            }

            if (current[k].IsLocked)
            {
                lockedOthers += current[k].SharePercent;
            }
            else
            {
                unlockedIdx.Add(k);
            }
        }

        // No unlocked segment can absorb the change → blocked; return input unchanged.
        if (unlockedIdx.Count == 0)
        {
            return current;
        }

        // Snap/clamp the request, then bound it: segment i can rise at most until every unlocked
        // absorber sits at 0 (locked segments hold their share).
        var target = SnapTo5(Math.Clamp(newPercent, 0, 100));
        target = Math.Clamp(target, 0, 100 - lockedOthers);

        var delta = target - current[i].SharePercent;

        var shares = new int[current.Count];
        for (var k = 0; k < current.Count; k++)
        {
            shares[k] = current[k].SharePercent;
        }
        shares[i] = target;

        var sumUnlocked = 0;
        foreach (var k in unlockedIdx)
        {
            sumUnlocked += current[k].SharePercent;
        }

        foreach (var k in unlockedIdx)
        {
            // Distribute proportionally to current share; when every absorber is at 0, split evenly.
            var portion = sumUnlocked > 0
                ? (double)current[k].SharePercent / sumUnlocked
                : 1.0 / unlockedIdx.Count;
            var raw = current[k].SharePercent - (delta * portion);
            shares[k] = SnapTo5(Math.Clamp(raw, 0, 100));
        }

        FixSumTo100(shares, unlockedIdx);

        var result = new QolShareValue[current.Count];
        for (var k = 0; k < current.Count; k++)
        {
            result[k] = current[k] with { SharePercent = shares[k] };
        }
        return result;
    }

    /// <summary>Rounds a value to the nearest multiple of 5 (ties away from zero).</summary>
    private static int SnapTo5(double value) =>
        (int)Math.Round(value / Snap, MidpointRounding.AwayFromZero) * Snap;

    /// <summary>
    /// Repairs rounding drift so the set totals exactly 100. Because every entry is a multiple of 5,
    /// the shortfall/surplus is too; it is applied in ±5 steps onto the largest eligible unlocked
    /// segment (re-evaluated each step), cascading to the next when a segment would fall below 0.
    /// </summary>
    private static void FixSumTo100(int[] shares, List<int> unlockedIdx)
    {
        var diff = 100 - shares.Sum();
        if (diff == 0)
        {
            return;
        }

        var step = diff > 0 ? Snap : -Snap;
        var guard = 0;
        while (diff != 0 && guard++ < 1000)
        {
            var pick = -1;
            var best = int.MinValue;
            foreach (var k in unlockedIdx)
            {
                if (step < 0 && shares[k] + step < 0)
                {
                    continue;
                }

                if (shares[k] > best)
                {
                    best = shares[k];
                    pick = k;
                }
            }

            if (pick < 0)
            {
                break; // Nothing can absorb the remainder (should not happen given the target bound).
            }

            shares[pick] += step;
            diff -= step;
        }
    }
}
