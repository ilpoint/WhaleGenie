using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The list of windows a step recognises with the rule it is written with right now. It is there to
/// be trusted instead of running the macro, so what it says has to be what a run would do: the same
/// three parts, the same three ways of comparing, and the same answer for a text left empty.
/// </summary>
public partial class WindowPreviewTests
{
}
