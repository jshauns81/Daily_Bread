using Daily_Bread.Services;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// The pure per-pool loss math (MECHANICS_AMENDMENT_II.md — Calculation): cap, then proportional
/// half-credit repair. No database — the worked examples from the amendment, verbatim.
/// </summary>
public class ReconciliationMathTests
{
    // Weekend pool used by the amendment's worked examples: 20h × 20% × 60 = 240.
    private const int WeekendCap = 240;
    private const int WeekdayCap = 720;

    [Fact]
    public void Uncapped_No_Repair_Loses_The_Raw_Sum()
    {
        Assert.Equal(60, ReconciliationMath.FinalPoolLoss(rawLoss: 60, poolCap: WeekendCap, repairedValue: 0));
        Assert.Equal(0, ReconciliationMath.RepairCredit(rawLoss: 60, poolCap: WeekendCap, repairedValue: 0));
    }

    [Fact]
    public void Uncapped_Repair_Gives_Exact_Half_Credit_Of_What_Was_Repaired()
    {
        // Miss two importance-5 chores (30 each), repair one (30): credit = 60·0.5·(30/60) = 15 → 45.
        Assert.Equal(45, ReconciliationMath.FinalPoolLoss(rawLoss: 60, poolCap: WeekendCap, repairedValue: 30));
        Assert.Equal(15, ReconciliationMath.RepairCredit(rawLoss: 60, poolCap: WeekendCap, repairedValue: 30));
    }

    [Fact]
    public void Uncapped_Repair_All_Halves_The_Loss()
    {
        // Repair everything uncapped → exactly half back.
        Assert.Equal(30, ReconciliationMath.FinalPoolLoss(rawLoss: 60, poolCap: WeekendCap, repairedValue: 60));
    }

    [Fact]
    public void Capped_No_Repair_Loses_Exactly_The_Cap()
    {
        // Blown week: ten importance-10 chores (60 each) = 600 raw, capped at 240.
        Assert.Equal(240, ReconciliationMath.FinalPoolLoss(rawLoss: 600, poolCap: WeekendCap, repairedValue: 0));
    }

    [Fact]
    public void Capped_Repair_All_Halves_The_Cap_Never_Zero()
    {
        // Repair all ten: credit = 240·0.5·(600/600) = 120 → 120. Half the cap, never erased.
        Assert.Equal(120, ReconciliationMath.FinalPoolLoss(rawLoss: 600, poolCap: WeekendCap, repairedValue: 600));
        Assert.Equal(120, ReconciliationMath.RepairCredit(rawLoss: 600, poolCap: WeekendCap, repairedValue: 600));
    }

    [Fact]
    public void Capped_Repair_Half_Restores_Proportionately()
    {
        // Repair five of ten: credit = 240·0.5·(300/600) = 60 → 180.
        Assert.Equal(180, ReconciliationMath.FinalPoolLoss(rawLoss: 600, poolCap: WeekendCap, repairedValue: 300));
    }

    [Fact]
    public void No_Misses_Is_No_Loss()
    {
        Assert.Equal(0, ReconciliationMath.FinalPoolLoss(rawLoss: 0, poolCap: WeekdayCap, repairedValue: 0));
        Assert.Equal(0, ReconciliationMath.RepairCredit(rawLoss: 0, poolCap: WeekdayCap, repairedValue: 0));
    }

    [Fact]
    public void RepairedValue_Cannot_Exceed_RawLoss()
    {
        // A repairedValue larger than rawLoss (never expected) clamps — credit can't exceed half.
        Assert.Equal(30, ReconciliationMath.FinalPoolLoss(rawLoss: 60, poolCap: WeekdayCap, repairedValue: 999));
    }

    [Fact]
    public void Entry_And_Snapshot_Always_Reconcile()
    {
        // finalLoss + repairCredit == appliedLoss, for capped and uncapped alike.
        foreach (var (raw, cap, repaired) in new[] { (60, 720, 30), (600, 240, 300), (600, 240, 600), (17, 240, 5) })
        {
            var applied = ReconciliationMath.AppliedLoss(raw, cap);
            var final = ReconciliationMath.FinalPoolLoss(raw, cap, repaired);
            var credit = ReconciliationMath.RepairCredit(raw, cap, repaired);
            Assert.Equal(applied, final + credit);
        }
    }
}
