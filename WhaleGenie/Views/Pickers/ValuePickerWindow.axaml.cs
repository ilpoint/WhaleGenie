using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The one window that fills a cell. It lays out what a formula is made of on one page — the signs,
/// the names, the functions — because composing means reaching for the next piece, and a page that
/// puts two of the three away is a page that has to be left and come back to.
/// </summary>
public partial class ValuePickerWindow : AppWindow
{
    private readonly ValuePickerViewModel _viewModel;
}
