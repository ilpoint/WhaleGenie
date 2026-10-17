namespace WhaleGenie.Core.Execution;

/// <summary>
/// Names that appear inside records rather than among an action's own parameters. They are file
/// format, so a reader, a writer and a row editor all spell them from here.
/// </summary>
public static class FieldNames
{
    /// <summary>The engine's own name of a result in a returns row.</summary>
    public const string Key = "key";

    /// <summary>The name a returns row is kept under.</summary>
    public const string Name = "name";

    public const string Left = "left";

    public const string Top = "top";

    public const string Width = "width";

    public const string Height = "height";
}
