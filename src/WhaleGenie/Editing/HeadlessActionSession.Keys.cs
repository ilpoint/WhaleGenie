using System;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

// The keyboard drawn on the page writes through here; the keys themselves are the engine's names.
internal sealed partial class HeadlessActionSession
{
    public EditorKeyPadSnapshot KeyPad(string mode) => EditorKeyPad.Build(mode);

    public EditorActionFormSnapshot ApplyPadKey(string name, string key, int index, string mode)
    {
        var field = Field(name);
        if (string.Equals(mode, "text", StringComparison.Ordinal))
        {
            field.WriteValue(Formula.Text(field.CurrentText + key));
            return Snapshot();
        }

        if (field.IsKeySequence && index >= 0 && index < field.KeyRows.Count)
        {
            field.KeyRows[index].AddKey(key);
        }
        else
        {
            field.WriteKey(key);
        }

        return Snapshot();
    }
}
