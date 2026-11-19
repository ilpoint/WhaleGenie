using System;
using System.Collections.Generic;
using System.Linq;
using SharpHook.Data;
using WhaleGenie.Core.Devices;
using Xunit;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// The engine keeps its own list of the keys the hook can report, so that a contract of the engine's
/// names a key without making the whole program depend on the library that reads the keyboard. This
/// is what keeps that list honest: the copy is only worth having while it says exactly what the
/// library says, and a key added, removed or renumbered by an upgrade has to be noticed here rather
/// than pressed wrongly later.
/// </summary>
/// <remarks>
/// The library documents the mapping as an article on its site, but documentation is not something a
/// program can read: its public surface is the enum, the event data and a few providers for
/// auto-repeat rates. So copying the enum is the only way to keep the boundary, and what this check
/// avoids is copying it <em>silently</em>.
///
/// The values are compared as well as the names, although the library says only the names are stable:
/// a value that moved means this copy now reports a different key than the hook reports, which is the
/// failure worth catching. Nothing is persisted as a number, so a renumbering costs a re-copy.
/// </remarks>
public sealed class KeyCodeParityTests
{
    [Fact]
    public void The_engine_key_codes_are_the_ones_the_hook_library_reports()
    {
        var library = Enum.GetValues<KeyCode>()
            .ToDictionary(code => Spelled(code.ToString()), code => (int)code, StringComparer.Ordinal);
        var engine = Enum.GetValues<HookCode>()
            .ToDictionary(code => code.ToString(), code => (int)code, StringComparer.Ordinal);

        var missing = library.Keys.Except(engine.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var extra = engine.Keys.Except(library.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.True(missing.Any() is false,
            $"the library reports keys the engine does not name: {string.Join(", ", missing)}");
        Assert.True(extra.Any() is false,
            $"the engine names keys the library does not report: {string.Join(", ", extra)}");

        foreach (var (name, code) in library.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Assert.Equal(code, engine[name]);
        }
    }

    /// <summary>
    /// Every key the engine knows has a name a macro can write, and that name comes back as the same
    /// key. The side is asked for because that is what a trigger binding stores: the two shift keys
    /// are two keys, and a name that folded them together would fire the wrong one.
    /// </summary>
    [Fact]
    public void Every_key_the_engine_names_resolves_back_to_that_key()
    {
        foreach (var code in Enum.GetValues<HookCode>())
        {
            if (code is HookCode.Undefined)
            {
                continue;
            }

            var name = KeyNames.Name(KeyTable.Key(code), sided: true);
            Assert.True(name.Length > 0, $"{code} has no name a macro can write");

            var resolved = KeyNames.Resolve(name);
            Assert.True(resolved is not null, $"the name \"{name}\" of {code} does not resolve");
            Assert.True(resolved!.Value.Value == (int)code,
                $"the name \"{name}\" of {code} resolves to {(HookCode)resolved.Value.Value}");
        }
    }

    /// <summary>
    /// The library spells a key "Vc" plus its name, and the one name that cannot be written as
    /// itself — a digit — is spelled <c>Digit0</c> in the engine's list.
    /// </summary>
    private static string Spelled(string libraryName)
    {
        var bare = libraryName.StartsWith("Vc", StringComparison.Ordinal) ? libraryName[2..] : libraryName;
        return bare.Length == 1 && char.IsAsciiDigit(bare[0]) ? "Digit" + bare : bare;
    }
}
