using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The safety net under work that has not been saved. A crash or a power cut used to take the
/// macro list, and whatever the macro editor was holding, with it: neither reaches disk until the
/// user writes it out. The work is written to a snapshot beside the program as it changes, and the
/// next run offers it back instead of coming up empty.
/// </summary>
public partial class RecoveryTests
{
}
