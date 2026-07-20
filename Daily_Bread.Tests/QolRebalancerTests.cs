using Daily_Bread.Services;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Exhaustively pins the pure QOL mixer algorithm (MECHANICS_AMENDMENT.md §C): proportional
/// redistribution, locks, snap-to-5, the all-others-locked block, a new routine entering at 0%,
/// and the invariant that the set always totals 100.
/// </summary>
public sealed class QolRebalancerTests
{
    private static QolShareValue S(int id, int pct, bool locked = false) => new(id, pct, locked);

    [Fact]
    public void Change_Redistributes_Opposite_Delta_Proportionally_Across_Unlocked()
    {
        var current = new[] { S(1, 40), S(2, 40), S(3, 20) };

        var result = QolRebalancer.Rebalance(current, 1, 60);

        Assert.Equal(60, Pct(result, 1));
        Assert.Equal(100, result.Sum(s => s.SharePercent));
        // Both absorbers shrink; the larger one (2) gives up more than the smaller (3).
        Assert.True(Pct(result, 2) < 40);
        Assert.True(Pct(result, 3) < 20);
        Assert.True(Pct(result, 2) > Pct(result, 3));
    }

    [Fact]
    public void Locked_Segment_Is_Never_Touched()
    {
        var current = new[] { S(1, 50), S(2, 30, locked: true), S(3, 20) };

        var result = QolRebalancer.Rebalance(current, 1, 70);

        Assert.Equal(70, Pct(result, 1));
        Assert.Equal(30, Pct(result, 2)); // locked: unchanged
        Assert.Equal(0, Pct(result, 3));  // sole unlocked absorber takes the whole delta
        Assert.Equal(100, result.Sum(s => s.SharePercent));
    }

    [Fact]
    public void Results_Snap_To_Multiples_Of_Five()
    {
        var current = new[] { S(1, 33), S(2, 33), S(3, 34) };

        var result = QolRebalancer.Rebalance(current, 1, 48);

        Assert.All(result, s => Assert.Equal(0, s.SharePercent % 5));
        Assert.Equal(100, result.Sum(s => s.SharePercent));
    }

    [Fact]
    public void All_Others_Locked_Blocks_The_Change()
    {
        var current = new[] { S(1, 50), S(2, 50, locked: true) };

        var result = QolRebalancer.Rebalance(current, 1, 70);

        // Blocked → input returned unchanged.
        Assert.Equal(50, Pct(result, 1));
        Assert.Equal(50, Pct(result, 2));
    }

    [Fact]
    public void New_Routine_Entering_At_Zero_Then_Raised_Rebalances_And_Sums_100()
    {
        // A freshly added routine (id 3) enters at 0%.
        var current = new[] { S(1, 60), S(2, 40), S(3, 0) };

        var result = QolRebalancer.Rebalance(current, 3, 30);

        Assert.Equal(30, Pct(result, 3));
        Assert.Equal(100, result.Sum(s => s.SharePercent));
        Assert.True(Pct(result, 1) < 60 && Pct(result, 2) < 40);
    }

    [Fact]
    public void Request_Above_What_Unlocked_Pool_Can_Give_Is_Capped()
    {
        // Locked segment holds 60; segment 1 can rise at most to 40 (absorber 3 hits 0).
        var current = new[] { S(1, 20), S(2, 60, locked: true), S(3, 20) };

        var result = QolRebalancer.Rebalance(current, 1, 95);

        Assert.Equal(40, Pct(result, 1));
        Assert.Equal(60, Pct(result, 2));
        Assert.Equal(0, Pct(result, 3));
        Assert.Equal(100, result.Sum(s => s.SharePercent));
    }

    [Fact]
    public void Unknown_Segment_Is_A_No_Op()
    {
        var current = new[] { S(1, 50), S(2, 50) };

        var result = QolRebalancer.Rebalance(current, 999, 30);

        Assert.Equal(50, Pct(result, 1));
        Assert.Equal(50, Pct(result, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(85)]
    [InlineData(100)]
    public void Sum_Stays_100_And_Values_Snapped_Across_A_Range(int newPercent)
    {
        var configs = new[]
        {
            new[] { S(1, 50), S(2, 30), S(3, 20) },
            new[] { S(1, 40), S(2, 40), S(3, 20) },
            new[] { S(1, 10), S(2, 20, locked: true), S(3, 70) },
            new[] { S(1, 25), S(2, 25), S(3, 25), S(4, 25) },
        };

        foreach (var config in configs)
        {
            var result = QolRebalancer.Rebalance(config, 1, newPercent);

            Assert.Equal(100, result.Sum(s => s.SharePercent));
            Assert.All(result, s =>
            {
                Assert.InRange(s.SharePercent, 0, 100);
                Assert.Equal(0, s.SharePercent % 5);
            });
        }
    }

    private static int Pct(IReadOnlyList<QolShareValue> shares, int choreId) =>
        shares.First(s => s.ChoreDefinitionId == choreId).SharePercent;
}
