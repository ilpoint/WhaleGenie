using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// Which code an action is answered by, and which results that code can write — read out of the
/// runner's own source rather than out of a list somebody maintains.
///
/// The question this answers is the one a whole-folder search cannot: not "does the text <c>"hit"</c>
/// appear anywhere in the engine" but "does the path <c>vision.findImage</c> takes write the result
/// the catalogue promises it will". A result that no reachable path writes is a box the dialog offers
/// and no run fills, and that is worth finding while the macro is being written rather than after it
/// has run.
///
/// The reading is deliberately generous about *how* a key is written and strict about *where*: a key
/// counts if it appears as a string, as a <c>FieldNames</c> constant, or as a default on a key
/// parameter of a helper the path calls. What it does not do is look outside the path.
/// </summary>
internal static class RunnerLedger
{
    /// <summary>One action's way through the engine: the method it lands in, and the keys it can write.</summary>
    internal sealed record Path(string Action, string? Handler, IReadOnlySet<string> Keys)
    {
        /// <summary>How the path reads in a failure message.</summary>
        public override string ToString()
            => Handler is null ? $"{Action} (handled in its dispatch)" : $"{Action} -> {Handler}";
    }

    /// <summary>
    /// Reads the sources into one path per action key. A path is the dispatch branch for that key
    /// plus every runner method it calls, two calls deep: a family's dispatch answers several
    /// actions with one method, and that method is what a key has to be written in.
    /// </summary>
    internal static IReadOnlyDictionary<string, Path> Build(IEnumerable<string> sources,
        IReadOnlyDictionary<string, string> constants)
    {
        var texts = sources.ToList();
        var methods = Methods(texts);

        // Only a switch over the step type routes actions. Every other switch in the engine is about
        // a value — a match mode, a list direction — and its cases are words, not action keys.
        var ledger = new Dictionary<string, Path>(StringComparer.Ordinal);
        var dispatches = methods
            .Where(method => method.Key.StartsWith("Dispatch", StringComparison.Ordinal))
            .SelectMany(method => method.Value.Select(body => body.Body));

        foreach (var dispatch in dispatches)
        {
            foreach (var (action, segment) in Branches(dispatch))
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                Collect(segment, constants, keys);

                // The method a branch hands the step to is not always the one that does the work:
                // a family first passes the step through a door (`Gate`) and only then into the
                // method it was written for. Naming the method that actually writes a result keeps
                // the failure message pointing at the code somebody has to change.
                string? handler = null;
                foreach (var called in CalledWithStep(segment))
                {
                    var written = new HashSet<string>(StringComparer.Ordinal);
                    Collects(called, constants, written, methods, depth: 2);
                    keys.UnionWith(written);
                    handler ??= written.Count > 0 ? called : null;
                }

                handler ??= CalledWithStep(segment).FirstOrDefault(methods.ContainsKey);
                ledger[action] = new Path(action, handler, keys);
            }
        }

        return ledger;
    }

    /// <summary>
    /// Catalogue results that the path for their action never writes, each said as
    /// <c>action -> handler: result "key"</c>. This is the list the test asserts is empty; a new
    /// action that forgets a result lands here with its own key, not as a count.
    /// </summary>
    internal static IReadOnlyList<string> Missing(
        IEnumerable<(string Action, IReadOnlyList<string> Results)> catalogue,
        IReadOnlyDictionary<string, Path> ledger)
    {
        var missing = new List<string>();
        foreach (var (action, results) in catalogue)
        {
            if (!ledger.TryGetValue(action, out var path))
            {
                continue;
            }

            missing.AddRange(results
                .Where(result => !path.Keys.Contains(result))
                .Select(result => $"{path}: declares result \"{result}\", which that path never writes"));
        }

        return missing;
    }

    /// <summary>
    /// Actions the catalogue has and the engine never routes: a step of that kind would be refused
    /// as unsupported, so the two lists disagreeing is worth saying once here rather than at a run.
    /// </summary>
    internal static IReadOnlyList<string> Unrouted(
        IEnumerable<(string Action, bool Hidden)> catalogue,
        IReadOnlyDictionary<string, Path> ledger)
        // A hidden action is not dispatched on its own: `control.case` is only ever a branch inside a
        // switch, which its parent step reads. Everything else has to have a branch of its own.
        => [.. catalogue
            .Where(action => !action.Hidden && !ledger.ContainsKey(action.Action))
            .Select(action => $"{action.Action}: the catalogue has it and no dispatch answers it")];

    /// <summary>
    /// Branches the engine routes that the catalogue does not have. A typo in a case label, or a
    /// case left behind by a removed action, is a step nobody can write and code nobody can reach.
    /// </summary>
    internal static IReadOnlyList<string> Stray(
        IReadOnlyCollection<string> actionKeys, IReadOnlyDictionary<string, Path> ledger)
        => [.. ledger.Keys
            .Where(key => !actionKeys.Contains(key))
            .Select(key => $"{key}: the engine routes it and the catalogue does not have it")];

    private static void Collects(string method, IReadOnlyDictionary<string, string> constants,
        HashSet<string> keys, IReadOnlyDictionary<string, IReadOnlyList<Method>> methods, int depth)
    {
        if (depth < 0 || !methods.TryGetValue(method, out var bodies))
        {
            return;
        }

        foreach (var body in bodies)
        {
            Collect(body.Body, constants, keys);
            foreach (var called in CalledWithStep(body.Body))
            {
                Collects(called, constants, keys, methods, depth - 1);
            }
        }
    }

    /// <summary>
    /// Every key-shaped string in a stretch of code: a quoted string, or a <c>FieldNames</c> constant
    /// written where a key would go. Generous on purpose — the question is whether the path *can*
    /// write the key, and a path that mentions it is the path that writes it.
    /// </summary>
    private static void Collect(string code, IReadOnlyDictionary<string, string> constants,
        HashSet<string> keys)
    {
        foreach (Match quoted in Regex.Matches(code, "\\\"([^\\\"\\n]*)\\\""))
        {
            keys.Add(quoted.Groups[1].Value);
        }

        foreach (Match named in Regex.Matches(code, "\\bFieldNames\\.([A-Za-z0-9_]+)\\b"))
        {
            if (constants.TryGetValue(named.Groups[1].Value, out var value))
            {
                keys.Add(value);
            }
        }
    }

    /// <summary>One method of the runner: its whole text, signature included.</summary>
    private sealed record Method(string Body);

    /// <summary>
    /// Every method the sources declare, by name, with the text from its signature through its
    /// closing brace. The signature is part of it because a key can be a parameter default, which is
    /// how the shared point helpers name the results they write.
    ///
    /// One name can have several bodies: the runner overloads some methods, and which overload a
    /// call means is not something source text answers. All of them are kept and all of them count —
    /// the question is what this name can write, and an overload that writes a key is part of that.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<Method>> Methods(IReadOnlyList<string> sources)
    {
        var methods = new Dictionary<string, List<Method>>(StringComparer.Ordinal);
        foreach (var text in sources)
        {
            foreach (Match declared in Regex.Matches(text,
                "(private|public|internal|protected)[^;{}]*?\\b([A-Z][A-Za-z0-9_]*)\\s*\\("))
            {
                var open = text.IndexOf('{', declared.Index + declared.Length);
                if (open < 0 || !TryBody(text, open, out var close))
                {
                    continue;
                }

                var name = declared.Groups[2].Value;
                if (!methods.TryGetValue(name, out var bodies))
                {
                    methods[name] = bodies = [];
                }

                bodies.Add(new Method(text[declared.Index..(close + 1)]));
            }
        }

        return methods.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Method>)pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>The index of the brace that closes the one at <paramref name="open"/>.</summary>
    private static bool TryBody(string text, int open, out int close)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    close = index;
                    return true;
                }
            }
        }

        close = -1;
        return false;
    }

    /// <summary>
    /// The branches of a switch over the step type: each case label and the code up to the next one.
    /// A case whose body runs into the next label (an empty case used as a fall-through) comes back
    /// with the short segment, which is what its path actually is.
    /// </summary>
    private static IReadOnlyList<(string Action, string Segment)> Branches(string body)
    {
        var branches = new List<(string Action, string Segment)>();
        var labels = Regex.Matches(body, "case \\\"([a-zA-Z0-9_.]+)\\\":");
        for (var index = 0; index < labels.Count; index++)
        {
            var at = labels[index].Index + labels[index].Length;
            var next = index + 1 < labels.Count ? labels[index + 1].Index : body.Length;
            branches.Add((labels[index].Groups[1].Value, body[at..next]));
        }

        return branches;
    }

    /// <summary>The runner's own methods a stretch of code calls with the step as the first argument.</summary>
    private static IReadOnlyList<string> CalledWithStep(string code)
        => [.. Regex.Matches(code, "\\b([A-Z][A-Za-z0-9_]*)\\s*\\(\\s*step\\b")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];
}
