using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>The project global editor works on drafts and commits only when Save succeeds.</summary>
[RestoreSharedState]
public partial class GlobalVariablesTests
{
}
