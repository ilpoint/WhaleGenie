using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WhaleGenie.Tests;

/// <summary>
/// A style that reaches inside a control's template by part name is a contract with that template,
/// and a template is not ours: it changes when the theme changes. When it does, the selector matches
/// nothing, the style simply never applies, and nothing turns red — the button looks right until the
/// pointer is over it.
///
/// So the contract is written down twice here. The style file names the part; these checks
/// instantiate the host and look for what the selector would land on. A part that disappears fails
/// the check with the selector that expected it, instead of quietly doing nothing.
/// </summary>
public class TemplatePartTests
{
    /// <summary>
    /// Every type-with-classes a template style starts with, and the part it reaches for. Built from
    /// the style files themselves, so a selector added without a probe is the only way to miss one —
    /// and that is what the last check counts.
    /// </summary>
    private static readonly (string Type, string Classes, string Part)[] Reaching =
    [
        ("Button", "", "ContentPresenter"),
        ("Button", "BottomButton", "ContentPresenter"),
        ("Button", "Breakpoint on", "ContentPresenter"),
        ("Button", "Caption Close", "ContentPresenter"),
        ("Button", "Caption Pinned", "ContentPresenter"),
        ("Button", "Chrome", "ContentPresenter"),
        ("Button", "Chrome Icon", "ContentPresenter"),
        ("Button", "Chrome Icon Pinned", "ContentPresenter"),
        ("Button", "Chrome Icon Quiet", "ContentPresenter"),
        ("Button", "Chrome Icon pinned", "ContentPresenter"),
        ("Button", "Chrome Labeled", "ContentPresenter"),
        ("Button", "CopyButton", "ContentPresenter"),
        ("Button", "ExprOption", "ContentPresenter"),
        ("Button", "ExprTab", "ContentPresenter"),
        ("Button", "ExprTab on", "ContentPresenter"),
        ("Button", "Field", "ContentPresenter"),
        ("Button", "FnCard", "ContentPresenter"),
        ("Button", "Ghost", "ContentPresenter"),
        ("Button", "Ghost RemoveButton", "ContentPresenter"),
        ("Button", "Ghost Tiny", "ContentPresenter"),
        ("Button", "Icon", "ContentPresenter"),
        ("Button", "Icon primary", "ContentPresenter"),
        ("Button", "Key", "ContentPresenter"),
        ("Button", "LabeledToolButton", "ContentPresenter"),
        ("Button", "LookThumb", "ContentPresenter"),
        ("Button", "Primary", "ContentPresenter"),
        ("Button", "Rail", "ContentPresenter"),
        ("Button", "RecordButton", "ContentPresenter"),
        ("Button", "RecordButton Recording", "ContentPresenter"),
        ("Button", "Secondary", "ContentPresenter"),
        ("Button", "Secondary ShiftOn", "ContentPresenter"),
        ("Button", "Square", "ContentPresenter"),
        ("Button", "Tab Inline Boxed", "ContentPresenter"),
        ("Button", "Tab Inline Solid", "ContentPresenter"),
        ("Button", "Tab Root", "ContentPresenter"),
        ("Button", "Tab Side", "ContentPresenter"),
        ("Button", "Thumb", "ContentPresenter"),
        ("ToggleButton", "Chip", "ContentPresenter"),
        ("ToggleButton", "Chip Key", "ContentPresenter"),
        ("ListBoxItem", "", "ContentPresenter"),
        ("ScrollViewer", "VarWallScroll", "ScrollBar#PART_VerticalScrollBar"),
    ];

    /// <summary>
    /// Selectors whose template is written in the same window that uses them, so a bare probe has
    /// nothing to find: `RunWindow` builds its own `SplitLine` inside a `ControlTemplate` and then
    /// colours it. They are still contracts, just with a template we own — no theme can rename them.
    /// </summary>
    private static readonly string[] WindowOwnedTemplates =
    [
        "GridSplitter.RunSplit /template/ Border#SplitLine",
        "GridSplitter.SplitVertical /template/ Border#SplitLine",
    ];

    [Fact]
    public void Every_style_that_reaches_into_a_template_lands_on_one_part()
    {
        Ui.Run(() =>
        {
            var wrong = new List<string>();

            foreach (var (type, classes, part) in Reaching)
            {
                var host = Host(type);
                foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    host.Classes.Add(name);
                }

                var window = Realize(host);

                var found = Parts(host, part);
                if (found != 1)
                {
                    wrong.Add($"{type}.{classes} /template/ {part} → {found}");
                }

                window.Close();
            }

            Assert.True(
                wrong.Count == 0,
                "these template selectors no longer land on exactly one part: "
                + string.Join(", ", wrong));
        });
    }

    /// <summary>
    /// The parts the code itself reaches for, by name. These are not in a style selector, so nothing
    /// above would notice them going away: `NumberBox.Narrow` returns silently when the spinner is
    /// missing, and the only symptom is a number field that got wider.
    /// </summary>
    [Fact]
    public void The_parts_the_code_reaches_for_are_there()
    {
        Ui.Run(() =>
        {
            var missing = new List<string>();

            Check(() => new TextBox(), "Border#PART_BorderElement", missing);
            Check(() => new NumericUpDown(), "Border#PART_BorderElement", missing);
            Check(() => new NumericUpDown(), "Panel#PART_SpinnerPanel", missing);
            Check(() => new NumericUpDown(), "Button#PART_IncreaseButton", missing);
            Check(() => new NumericUpDown(), "Button#PART_DecreaseButton", missing);
            Check(() => new ScrollViewer { Content = new TextBlock { Text = "x" } },
                "ScrollBar#PART_VerticalScrollBar", missing);
            Check(() => new ScrollViewer { Content = new TextBlock { Text = "x" } },
                "ScrollBar#PART_HorizontalScrollBar", missing);

            Assert.True(
                missing.Count == 0,
                "these parts the code reaches for are gone: " + string.Join(", ", missing));

            static void Check(Func<Control> make, string spec, List<string> missing)
            {
                var host = make();
                var window = Realize(host);

                if (Parts(host, spec) != 1)
                {
                    missing.Add($"{host.GetType().Name} {spec}");
                }

                window.Close();
            }
        });
    }

    /// <summary>
    /// Puts the control on a window and builds every template under it. Without the second step the
    /// inner parts are missing for a reason that has nothing to do with the theme: a `ButtonSpinner`
    /// in a `NumericUpDown` has no template until something asks for it — which is exactly what
    /// `NumberBox.Narrow` has to do by hand.
    /// </summary>
    private static Window Realize(Control host)
    {
        var window = new Window { Width = 420, Height = 320, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var templated in host.GetVisualDescendants().OfType<TemplatedControl>().ToList())
        {
            templated.ApplyTemplate();
        }

        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// The list above is a copy of what the style files say, and a copy drifts. This reads the files
    /// and compares: a selector that reaches into a template with a type-and-class head must have a
    /// line above, otherwise a downgraded selector could go unchecked the day it is added.
    /// </summary>
    [Fact]
    public void The_list_above_covers_every_template_selector_in_the_style_files()
    {
        var uncovered = Selectors()
            .Select(selector => selector.Split(" /template/ ", StringSplitOptions.None))
            .Where(halves => halves.Length == 2)
            .Select(halves => Head(halves[0]) is { } head
                ? Key(head.Type, head.Classes, halves[1].Trim())
                : null)
            .Where(key => key is not null)
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !WindowOwnedTemplates.Contains(key, StringComparer.Ordinal))
            .Where(key => !Reaching
                .Select(entry => Key(entry.Type, entry.Classes, entry.Part))
                .Contains(key, StringComparer.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            uncovered.Count == 0,
            "these template selectors have no probe above: " + string.Join(" | ", uncovered));
    }

    /// <summary>One spelling for a reach, whichever side it came from.</summary>
    private static string Key(string type, string classes, string part)
    {
        var names = classes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return (names.Count == 0 ? type : type + "." + string.Join('.', names))
            + " /template/ " + part;
    }

    /// <summary>
    /// The type and classes a selector starts with — the last name in the head, since
    /// `ListBox.List ListBoxItem:selected` is about the item. Pseudo-classes are dropped: the
    /// template is the same one.
    /// </summary>
    private static (string Type, string Classes)? Head(string selector)
    {
        var last = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        var parts = last.Split(':')[0].Split('.');
        if (parts[0].Length == 0)
        {
            return null;
        }

        return (parts[0], string.Join(' ', parts.Skip(1)));
    }

    private static IEnumerable<string> Selectors()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes"), "*.axaml")
            .Concat(Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot(), "WhaleGenie", "Views"), "*.axaml"));

        foreach (var file in files)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"Selector=""([^""]+)"""))
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    /// <summary>
    /// How many parts of that description the host's template has. `Type#Name` counts by name,
    /// a bare type counts every descendant of it.
    /// </summary>
    private static int Parts(Control host, string spec)
    {
        var pieces = spec.Split('#');
        var typeName = pieces[0];
        var name = pieces.Length > 1 ? pieces[1] : null;

        // A named part is counted by name alone: the button in a ButtonSpinner is a RepeatButton,
        // and which type hosts it is the template's business, not this contract's.
        return host.GetVisualDescendants()
            .Count(visual => name is null
                ? visual.GetType().Name == typeName
                : visual.Name == name);
    }

    private static Control Host(string type) => type switch
    {
        "Button" => new Button { Content = "x" },
        "ToggleButton" => new ToggleButton { Content = "x" },
        "ListBoxItem" => new ListBoxItem { Content = "x" },
        "ScrollViewer" => new ScrollViewer { Content = new TextBlock { Text = "x" } },
        "GridSplitter" => new GridSplitter(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "no probe for this host"),
    };

    private static string RepositoryRoot([CallerFilePath] string here = "")
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "WhaleGenie", "Themes")))
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            Assert.Fail($"the repository root was not found above {here}");
        }

        return folder!.FullName;
    }
}
