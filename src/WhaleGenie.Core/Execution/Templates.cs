using System.Text.RegularExpressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Text a macro wrote with names in it, such as <c>{{user}}</c>, filled in from the variables of the
/// run. A script handed to a page and a script handed to a command line are the same promise, so the
/// filling is written once.
/// </summary>
internal static class Templates
{
    /// <summary>
    /// The text with every <c>{{name}}</c> replaced by what that name holds. A name the run does not
    /// have is left as it stands: the page or the script then says what it could not find, rather
    /// than being handed an empty hole nobody can see.
    /// </summary>
    public static string Fill(string text, VariableStore variables)
        => Regex.Replace(text, @"\{\{(.+?)\}\}", match =>
        {
            var name = match.Groups[1].Value.Trim();
            return variables.TryGet(name, out var value) ? value.AsText() : match.Value;
        });
}
