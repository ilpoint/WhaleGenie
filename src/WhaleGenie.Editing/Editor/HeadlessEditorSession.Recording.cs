using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>Recording: what a capture run writes and the options it starts from.</summary>

internal sealed partial class HeadlessEditorSession
{
    public EditorRecordingSnapshot Recording => new(
        _model.PositionCaptureIndex,
        _model.PositionCaptureOptions,
        _model.RecordMouseMovement,
        _model.RecordMouseButtons,
        _model.RecordKeyboardKeys,
        _model.RecordGamepadButtons,
        _model.RecordDelays,
        (int)(_model.IgnoreDelaysBelowMs ?? 0),
        _model.RecordIgnoreInjected,
        _model.DelayScale,
        _model.DelayScaleDisplay,
        _model.HasDelayScale);

    public void SetRecordOption(string field, string value)
    {
        switch (field)
        {
            case "positionCapture":
                _model.PositionCaptureIndex = int.TryParse(value, out var index) ? index : 0;
                break;
            case "mouseMovement":
                _model.RecordMouseMovement = Flag(value);
                break;
            case "mouseButtons":
                _model.RecordMouseButtons = Flag(value);
                break;
            case "keyboardKeys":
                _model.RecordKeyboardKeys = Flag(value);
                break;
            case "gamepadButtons":
                _model.RecordGamepadButtons = Flag(value);
                break;
            case "delays":
                _model.RecordDelays = Flag(value);
                break;
            case "ignoreDelaysBelowMs":
                _model.IgnoreDelaysBelowMs = decimal.TryParse(value,
                    System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var milliseconds) ? milliseconds : 0;
                break;
            case "ignoreInjected":
                _model.RecordIgnoreInjected = Flag(value);
                break;
            default:
                throw new InvalidOperationException("unknown recording option " + field);
        }

        _model.IsDirty = true;
    }

    public void SetDelayScale(double factor) => _model.SetDelayScale(factor);

    public bool RecordIgnoreInjected => _model.RecordIgnoreInjected;

    public bool RecordGamepadButtons => _model.RecordGamepadButtons;

    public RecordingOptions RecordingChoices() => new()
    {
        MouseMovement = _model.RecordMouseMovement,
        MouseButtons = _model.RecordMouseButtons,
        KeyboardKeys = _model.RecordKeyboardKeys,
        GamepadButtons = _model.RecordGamepadButtons,
        Delays = _model.RecordDelays,
        IgnoreDelaysBelowMs = (int)(_model.IgnoreDelaysBelowMs ?? 0),
        RelativeMovement = _model.PositionCapture is MousePositionMode.SavePositionDifferences,
    };

    private static bool Flag(string value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

}
