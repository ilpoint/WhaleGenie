using WhaleGenie.Core.Devices;

namespace WhaleGenie.Models;

/// <summary>Checks a binding with the same key vocabulary the global hook uses at runtime.</summary>
public static class HotkeyBinding
{
    public static bool IsSupported(string? binding)
    {
        var text = binding?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return false;
        }

        var keys = KeyNames.ResolveChord(text, out var unknown);
        return unknown is null && keys.Count > 0;
    }
}
