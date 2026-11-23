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

/// <summary>A macro's own settings: the input limit it follows and the key that arms it.</summary>

internal sealed partial class HeadlessEditorSession
{
    public bool FollowInputLimit => _model.FollowProgramInputRate;

    public int InputLimit => (int)Math.Round(_model.InputLimitValue);

    public string ToggleKey => _model.ToggleKey;

    public void SetMacroSetting(string field, string value)
    {
        switch (field)
        {
            case "followInputLimit":
                _model.FollowProgramInputRate = bool.TryParse(value, out var follow) && follow;
                break;
            case "inputLimit":
                _model.FollowProgramInputRate = false;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var limit))
                {
                    _model.InputLimitValue = Math.Clamp(limit, 0, 999);
                }

                break;
            case "toggleKey":
                _model.ToggleKey = value.Trim();
                break;
            default:
                throw new InvalidOperationException("unknown macro setting " + field);
        }

        _model.IsDirty = true;
    }

}
