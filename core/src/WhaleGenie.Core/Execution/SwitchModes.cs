namespace WhaleGenie.Core.Execution;

/// <summary>How the value of a switch is held up against one of its cases.</summary>
internal enum SwitchComparison
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Regex,
}

/// <summary>
/// The word a switch step writes for the way it compares, and the way that word means. Kept apart
/// from the walk that runs the branches: which word means which comparison is a fact about the
/// language the macro file is written in, and a word nobody knows is the plain equality a switch had
/// before the other ways existed.
/// </summary>
internal static class SwitchModes
{
    public static SwitchComparison Of(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "notequals" => SwitchComparison.NotEquals,
        "contains" => SwitchComparison.Contains,
        "notcontains" => SwitchComparison.NotContains,
        "startswith" => SwitchComparison.StartsWith,
        "endswith" => SwitchComparison.EndsWith,
        "greaterthan" => SwitchComparison.GreaterThan,
        "greaterorequal" => SwitchComparison.GreaterOrEqual,
        "lessthan" => SwitchComparison.LessThan,
        "lessorequal" => SwitchComparison.LessOrEqual,
        "regex" => SwitchComparison.Regex,
        _ => SwitchComparison.Equals,
    };
}
