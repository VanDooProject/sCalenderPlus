namespace SCalenderPlus.Core.Recurrence;

/// <summary>
/// Days the rule enumerations of one request may examine together (data-model.md §9 "Caps"): each expansion stops
/// after <see cref="RuleDates.MaxExaminedDays"/> on its own, but a window query expands up to 5,000 series, and
/// <c>COUNT</c> rules cannot jump to the window — so without a shared budget one request could examine billions of
/// days. Once <see cref="IsExhausted"/>, enumerations stop and the result is marked truncated. Not thread-safe (one
/// request).
/// </summary>
public sealed class ExpansionBudget(long days)
{
    /// <summary>Days examined per window query at most: ≈ 12,500 13-month windows of daily series.</summary>
    public const long PerWindowQuery = 5_000_000;

    private long _remaining = days;

    /// <summary>No days are left: expansions using this budget stop (and report truncation).</summary>
    public bool IsExhausted => _remaining <= 0;

    /// <summary>Records <paramref name="examined"/> days; false once the budget is used up.</summary>
    internal bool Spend(int examined)
    {
        _remaining -= examined;
        return _remaining > 0;
    }
}
