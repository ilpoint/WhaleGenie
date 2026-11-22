using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// One line of the run log.
/// <summary>One line of the run log, already translated.</summary>
public sealed record RunLineViewModel(string Time, LogLevel Level, string Text)
{
    public string Color => Level switch
    {
        LogLevel.Error => "#E06C75",
        LogLevel.Warn => "#D8B36A",
        LogLevel.Debug => "#808080",
        _ => "#CFCFCF",
    };
}
