using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The run-and-debug window: it debugs one macro and manages the variables the project can use, and
/// which of the two it is showing follows from how it was opened. Opened from the button on its own
/// there is nothing to run, so the run controls are not drawn and the line that says where a debug
/// run starts from stands in their place.
/// </summary>
public partial class RunDebugTests
{
}
