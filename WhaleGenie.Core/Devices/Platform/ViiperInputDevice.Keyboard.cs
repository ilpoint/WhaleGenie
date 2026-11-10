using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;


namespace WhaleGenie.Core.Devices.Platform;

// Keys: a press, a chord, and typing text one character at a time.
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
    public void KeyPress(string key, int holdMs)
    {
        lock (_gate)
        {
            var code = Code(key);
            Note(code);
            Apply(code, down: true);
            SendKeyboard();
            Sleep(holdMs > 0 ? holdMs : LeastHoldMs);
            Apply(code, down: false);
            SendKeyboard();
        }
    }

    public void KeyDown(string key) => Hold(key, down: true);

    public void KeyUp(string key) => Hold(key, down: false);

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
    {
        if (keys.Count == 0)
        {
            throw new DeviceActionException(RunKeys.EmptyChord);
        }

        lock (_gate)
        {
            // Every key is looked up before any is pressed, so a chord with a name that means
            // nothing fails without half of it being down already.
            var codes = new List<KeyCode>(keys.Count);
            foreach (var key in keys)
            {
                codes.Add(Code(key));
            }

            foreach (var code in codes)
            {
                Note(code);
                Apply(code, down: true);
                SendKeyboard();
                Sleep(ChordGapMs);
            }

            Sleep(holdMs > 0 ? holdMs : LeastHoldMs);

            for (var index = codes.Count - 1; index >= 0; index--)
            {
                Apply(codes[index], down: false);
                SendKeyboard();
            }
        }
    }

    public void TypeText(string text, int intervalMs)
    {
        if (text.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var character in text)
            {
                if (ViiperKeys.Typing(character) is not { } wanted)
                {
                    throw new DeviceActionException(RunKeys.NoDriverTyping, character.ToString());
                }

                // The gate speaks characters here, because a character is what the recorder and
                // the triggers compare typed text against.
                WhaleGenieInputGate.Note(character.ToString());

                var modifiers = wanted.Shift
                    ? (byte)(_modifiers | LibViiper.ModifierLeftShift)
                    : _modifiers;
                SendKeyboard(modifiers, [(byte)wanted.Usage]);
                Sleep(TypingHoldMs);
                SendKeyboard(_modifiers, []);
                Sleep(intervalMs);
            }
        }
    }
}
