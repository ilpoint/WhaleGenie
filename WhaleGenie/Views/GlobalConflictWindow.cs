using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// Asks what to do about global variables that the file and the project both declare.
/// </summary>
/// <remarks>
/// Silence is the one answer not offered: a project that quietly keeps its own value while a macro
/// arrives expecting a different one is a run that does the wrong thing for reasons nobody can see.
/// Renaming is the first answer because it is the only one that keeps both meanings; it rewrites the
/// arriving macros and leaves the project's own alone. A script body is not rewritten, so a macro
/// whose script mentions the name is named here.
/// </remarks>
public sealed class GlobalConflictWindow : AppWindow
{
    private readonly IReadOnlyList<GlobalDecision> _decisions;
    private readonly IReadOnlyList<MacroItem> _macros;

    private GlobalConflictWindow(IReadOnlyList<GlobalDecision> decisions,
        IReadOnlyList<MacroItem> macros)
    {
        _decisions = decisions;
        _macros = macros;

        Width = 560;
        Height = 460;
        MinWidth = 460;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = Strings.Get("GlobalConflict.Title");
        ThemeBrush.Assign(this, BackgroundProperty, "SurfaceWindow");

        var heading = new TextBlock
        {
            Text = Strings.Get("GlobalConflict.Title"),
            Classes = { "Title" },
        };

        var hint = new TextBlock
        {
            Text = Strings.Get("GlobalConflict.Hint"),
            Classes = { "Hint" },
        };

        var rows = new StackPanel { Spacing = 10 };
        foreach (var decision in decisions)
        {
            rows.Children.Add(Decision(decision));
        }

        var go = new Button
        {
            Content = Strings.Get("GlobalConflict.Do"),
            Height = 32,
            MinWidth = 84,
            Padding = new Thickness(11, 0),
        };
        go.Click += (_, _) => Close(true);

        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            Height = 32,
            MinWidth = 84,
            Padding = new Thickness(11, 0),
        };
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancel, go },
        };

        var scroll = new ScrollViewer { Content = rows };
        Grid.SetRow(hint, 1);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(buttons, 3);

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                Children = { heading, hint, scroll, buttons },
            },
        };
    }

    /// <summary>True when the person went on, which is what the import waits for.</summary>
    public static async Task<bool> ShowAsync(Window owner, IReadOnlyList<GlobalDecision> decisions,
        IReadOnlyList<MacroItem> macros)
    {
        // Only the names both sides declare are a question; a variable the project does not have is
        // simply added, and asking about it would teach the person to click through this window.
        var conflicts = decisions.Where(decision => decision.HasConflict).ToList();
        if (conflicts.Count == 0)
        {
            return true;
        }

        var window = new GlobalConflictWindow(conflicts, macros);
        return await window.ShowDialogOver<bool>(owner);
    }

    private Control Decision(GlobalDecision decision)
    {
        var name = decision.Arriving.Name.Trim();
        var existing = decision.Existing!;

        var title = new TextBlock
        {
            Text = $"{name}  ·  {existing.Type} / {decision.Arriving.Type}",
            Classes = { "Body" },
            FontWeight = FontWeight.SemiBold,
        };

        var options = new ComboBox
        {
            ItemsSource = new[]
            {
                Strings.Get("GlobalConflict.Rename"),
                Strings.Get("GlobalConflict.UseProject"),
                Strings.Get("GlobalConflict.Overwrite"),
            },
            SelectedIndex = decision.Choice switch
            {
                GlobalChoice.UseProject => 1,
                GlobalChoice.Overwrite => 2,
                _ => 0,
            },
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        options.SelectionChanged += (_, _) => decision.Choice = options.SelectedIndex switch
        {
            1 => GlobalChoice.UseProject,
            2 => GlobalChoice.Overwrite,
            _ => GlobalChoice.Rename,
        };

        var panel = new StackPanel { Spacing = 4, Children = { title, options } };

        var scripts = VariableRename.ScriptUsers(_macros, name);
        if (scripts.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Strings.Format("GlobalConflict.Scripts", string.Join("、", scripts)),
                Classes = { "Caption" },
                Foreground = ThemeBrush.Of("Warning"),
            });
        }

        return panel;
    }



    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
