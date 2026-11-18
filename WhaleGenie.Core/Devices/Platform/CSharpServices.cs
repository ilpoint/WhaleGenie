namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The one thing a C# script does not write itself: the way back into the macro. It is compiled
/// together with the script rather than shipped as a file for the same reason the browser picker's
/// script is a string — it has to match the process that reads the other end, so it travels with
/// it. The field is filled in before the script's entry point is called.
/// </summary>
internal static class CSharpServices
{
    /// <summary>
    /// What a script writes <c>__InjectedServices.DynamicObject</c> to reach: the macro's values,
    /// its steps, its log and the result this step hands back. A script that names it as it is
    /// written needs nothing else, because everything below it is answered at the moment it is
    /// asked rather than written into the script.
    /// </summary>
    public const string Source = """
        // The macro this script is running inside, put here before the script starts.
        public static class __InjectedServices
        {
            public static dynamic DynamicObject = null!;
        }
        """;
}
