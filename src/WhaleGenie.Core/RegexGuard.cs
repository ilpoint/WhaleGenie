using System;
using System.Text.RegularExpressions;

namespace WhaleGenie.Core;

/// <summary>
/// A pattern the machine was still working on when its time ran out. The pattern is kept so the
/// step can say which one was too expensive without turning the failure into a bug in the engine.
/// </summary>
public sealed class RegexTimeoutException : Exception
{
    public RegexTimeoutException(string pattern, TimeSpan timeout)
        : base($"The pattern did not finish within {timeout.TotalSeconds:0.###} seconds.")
    {
        Pattern = pattern;
        Timeout = timeout;
    }

    public string Pattern { get; }

    public TimeSpan Timeout { get; }
}

/// <summary>
/// Every regular expression a macro writes runs through here. The clock is the same one everywhere,
/// because a pattern that can stall one field can stall any of them, and a run that cannot be stopped
/// is worse than one that reports the pattern it could not finish.
/// </summary>
public static class RegexGuard
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static Regex Create(string pattern, RegexOptions options = RegexOptions.None)
        => new(pattern, options, Timeout);

    public static bool IsMatch(string text, string pattern, RegexOptions options = RegexOptions.None)
        => Guarded(pattern, () => Create(pattern, options).IsMatch(text));

    public static Match Match(string text, string pattern, RegexOptions options = RegexOptions.None)
        => Guarded(pattern, () => Create(pattern, options).Match(text));

    public static string Replace(string text, string pattern, string replacement,
        RegexOptions options = RegexOptions.None)
        => Guarded(pattern, () => Create(pattern, options).Replace(text, replacement));

    private static T Guarded<T>(string pattern, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RegexTimeoutException(pattern, Timeout);
        }
    }
}
