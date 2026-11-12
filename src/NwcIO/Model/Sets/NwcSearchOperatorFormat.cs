namespace NwcIO.Model.Sets;

/// <summary>
/// Shared conversion between <see cref="NwcSearchOperator"/> and the operator strings
/// used by the search-set and appearance-profiler XML serializers, so every operator
/// round-trips through both formats with the same spelling.
/// </summary>
internal static class NwcSearchOperatorFormat
{
    /// <summary>Returns the canonical operator spelling used when writing conditions.</summary>
    public static string Format(NwcSearchOperator op) => op switch
    {
        NwcSearchOperator.Equals => "equals",
        NwcSearchOperator.NotEquals => "not_equals",
        NwcSearchOperator.Contains => "contains",
        NwcSearchOperator.StartsWith => "starts_with",
        NwcSearchOperator.EndsWith => "ends_with",
        NwcSearchOperator.Wildcard => "wildcard",
        NwcSearchOperator.GreaterThan => "greater_than",
        NwcSearchOperator.LessThan => "less_than",
        NwcSearchOperator.GreaterThanOrEqual => "greater_or_equal",
        NwcSearchOperator.LessThanOrEqual => "less_or_equal",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown search operator."),
    };

    /// <summary>
    /// Parses an operator string written by either serializer. Accepts the canonical
    /// spellings above plus the alternate <c>greater_equal</c>/<c>less_equal</c>,
    /// compact and symbol aliases. A missing or empty string means
    /// <see cref="NwcSearchOperator.Equals"/>; any other value throws instead of
    /// silently degrading to equality.
    /// </summary>
    public static NwcSearchOperator Parse(string? opStr) => opStr?.ToLowerInvariant() switch
    {
        null or "" or "equals" => NwcSearchOperator.Equals,
        "not_equals" or "notequals" or "!=" => NwcSearchOperator.NotEquals,
        "contains" => NwcSearchOperator.Contains,
        "starts_with" or "startswith" => NwcSearchOperator.StartsWith,
        "ends_with" or "endswith" => NwcSearchOperator.EndsWith,
        "wildcard" => NwcSearchOperator.Wildcard,
        "greater_than" or "greaterthan" or ">" => NwcSearchOperator.GreaterThan,
        "less_than" or "lessthan" or "<" => NwcSearchOperator.LessThan,
        "greater_or_equal" or "greater_equal" or ">=" => NwcSearchOperator.GreaterThanOrEqual,
        "less_or_equal" or "less_equal" or "<=" => NwcSearchOperator.LessThanOrEqual,
        _ => throw new FormatException($"Unknown search condition operator '{opStr}'."),
    };
}
