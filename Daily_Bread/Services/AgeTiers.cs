namespace Daily_Bread.Services;

/// <summary>
/// Age tiers drive age-appropriate voice in the app: a Teen (13+) is never
/// spoken to like a little kid. Two tiers only — Younger and Teen. Age is
/// computed from a birthdate against the family's "today" (the server owns the
/// calendar, so the tier never drifts on a device). No birthdate → Younger,
/// the gentlest default.
///
/// Pure and dependency-free on purpose: the tier decision is easy to unit-test
/// without a database.
/// </summary>
public static class AgeTiers
{
    public const string Younger = "younger";
    public const string Teen = "teen";

    /// <summary>The age (in whole years) at which the Teen voice begins.</summary>
    public const int TeenAge = 13;

    /// <summary>Whole years old on <paramref name="today"/>, or null if unknown or in the future.</summary>
    public static int? AgeInYears(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is not { } born)
        {
            return null;
        }

        var age = today.Year - born.Year;
        // Not had this year's birthday yet? Step back one.
        if (today < born.AddYears(age))
        {
            age--;
        }

        return age < 0 ? null : age;
    }

    /// <summary>The tier string ("younger" | "teen") for a birthdate as of today.</summary>
    public static string Tier(DateOnly? birthDate, DateOnly today)
    {
        var age = AgeInYears(birthDate, today);
        return age is >= TeenAge ? Teen : Younger;
    }
}
