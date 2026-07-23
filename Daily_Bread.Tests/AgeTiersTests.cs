using System;
using Daily_Bread.Services;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// The pure age-tier decision (AgeTiers): two tiers, boundary at 13, computed
/// against the family's "today". No database.
/// </summary>
public class AgeTiersTests
{
    private static readonly DateOnly Today = new(2026, 7, 23);

    [Fact]
    public void No_Birthdate_Is_Younger()
    {
        Assert.Null(AgeTiers.AgeInYears(null, Today));
        Assert.Equal(AgeTiers.Younger, AgeTiers.Tier(null, Today));
    }

    [Fact]
    public void Twelve_Is_Younger_Thirteen_Is_Teen()
    {
        var twelve = new DateOnly(2014, 1, 1);   // turned 12 in Jan 2026
        var thirteen = new DateOnly(2013, 1, 1);  // turned 13 in Jan 2026
        Assert.Equal(12, AgeTiers.AgeInYears(twelve, Today));
        Assert.Equal(AgeTiers.Younger, AgeTiers.Tier(twelve, Today));
        Assert.Equal(13, AgeTiers.AgeInYears(thirteen, Today));
        Assert.Equal(AgeTiers.Teen, AgeTiers.Tier(thirteen, Today));
    }

    [Fact]
    public void Birthday_Not_Yet_Reached_This_Year_Counts_A_Year_Less()
    {
        // Born 2013-08-01: on 2026-07-23 they are still 12 (birthday next month).
        var born = new DateOnly(2013, 8, 1);
        Assert.Equal(12, AgeTiers.AgeInYears(born, Today));
        Assert.Equal(AgeTiers.Younger, AgeTiers.Tier(born, Today));
    }

    [Fact]
    public void On_The_Thirteenth_Birthday_They_Are_A_Teen()
    {
        var born = new DateOnly(2013, 7, 23); // exactly 13 today
        Assert.Equal(13, AgeTiers.AgeInYears(born, Today));
        Assert.Equal(AgeTiers.Teen, AgeTiers.Tier(born, Today));
    }

    [Fact]
    public void A_Fourteen_Year_Old_Is_A_Teen()
    {
        var born = new DateOnly(2012, 3, 10); // Victor: an "old" 14
        Assert.Equal(14, AgeTiers.AgeInYears(born, Today));
        Assert.Equal(AgeTiers.Teen, AgeTiers.Tier(born, Today));
    }

    [Fact]
    public void A_Future_Birthdate_Reads_As_Younger()
    {
        var future = new DateOnly(2030, 1, 1);
        Assert.Null(AgeTiers.AgeInYears(future, Today));
        Assert.Equal(AgeTiers.Younger, AgeTiers.Tier(future, Today));
    }
}
