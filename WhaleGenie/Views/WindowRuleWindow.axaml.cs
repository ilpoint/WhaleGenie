using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Views;

/// <summary>
/// Captures the window in front when F1 is pressed, then edits the one rule that finds it again.
/// It stands above everything but does not take the focus until a window has been captured, so the
/// click that chooses the target lands where the person aimed it.
/// </summary>
public partial class WindowRuleWindow : AppWindow
{
    private static readonly HookKey F1 = KeyNames.Resolve("F1") ?? default;
    private static readonly HookKey Escape = KeyNames.Resolve("Esc") ?? default;

    private readonly IWindowDevice _windows;
    private readonly IInputHook? _hookSource;
    private IInputHook? _hook;
    private WindowRule? _rule;
    private bool _done;
    private bool _swallowF1Up;
    private bool _swallowEscapeUp;

    public WindowRuleWindow()
        : this(SharedDevices.Current.Windows, SharedDevices.Current.Hook, null)
    {
    }

    /// <summary>
    /// The picker over just what it reads: the windows on this machine and the key hook that
    /// catches F1. Taking the two rather than the whole device layer says what this window needs,
    /// and lets a stand-in be the two things instead of twenty.
    /// </summary>
    internal WindowRuleWindow(IWindowDevice windows, IInputHook hook, WindowRule? current)
    {
        InitializeComponent();
        _windows = windows;
        _hookSource = hook;
        _rule = current;

        Status.Text = Strings.Get("WindowRule.Waiting");
        HowBox.ItemsSource = WindowHow.Values.Select(value =>
            Strings.Get($"WindowRule.How.{value}", WindowHow.Labels[WindowHow.IndexOf(value)])).ToList();
        HowBox.SelectedIndex = current is null ? 0 : WindowHow.IndexOf(current.How);
        HowBox.SelectionChanged += (_, _) => { if (_rule is { } rule) Show(rule with { How = How() }); };
        MatchBox.TextChanged += (_, _) => { if (_rule is { } rule) UseEditedText(rule); };
        CaseBox.IsCheckedChanged += (_, _) => { if (_rule is { } rule) Show(rule with { CaseSensitive = CaseBox.IsChecked == true }); };
        TrimBox.IsCheckedChanged += (_, _) => { if (_rule is { } rule) Show(rule with { Trim = TrimBox.IsChecked != false }); };
        MultilineBox.IsCheckedChanged += (_, _) => { if (_rule is { } rule) Show(rule with { Multiline = MultilineBox.IsChecked == true }); };

        AgainButton.Click += (_, _) => Capture();
        CancelButton.Click += (_, _) => Finish(null);
        UseButton.Click += (_, _) => Finish(_rule is { } rule ? rule with { How = How() } : null);
        AddHandler(KeyDownEvent, OnLocalKeyDown, RoutingStrategies.Tunnel);

        if (current is not null)
        {
            Show(current);
        }
    }

    internal WindowRule? Chosen { get; private set; }

    public static async Task<WindowRule?> PickAsync(Window owner, IWindowDevice windows,
        IInputHook hook, WindowRule? current = null)
    {
        using var aside = AppWindows.StepAside(owner);
        return await new WindowRuleWindow(windows, hook, current).TakeAsync();
    }

    internal async Task<WindowRule?> TakeAsync()
    {
        await AppWindows.ShowAndWaitAsync(this);
        return Chosen;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MacroTriggerGate.Enter();
        AttachHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _done = true;
        DetachHook();
        MacroTriggerGate.Exit();
    }

    private void Capture()
    {
        WindowInfo? found;
        try
        {
            found = _windows.Foreground();
        }
        catch (Exception)
        {
            found = null;
        }

        if (found is null)
        {
            Status.Text = Strings.Get("WindowRule.None");
            return;
        }

        var process = _windows.ProcessOf(found.Handle);
        var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (string.Equals(process, own, StringComparison.OrdinalIgnoreCase))
        {
            Status.Text = Strings.Get("WindowRule.OwnWindow");
            return;
        }

        _rule = WindowRule.From(found, process, _windows.ClassOf(found.Handle));
        Activate();
        Show(_rule);
        MatchBox.Focus();
        MatchBox.SelectAll();
    }

    private void Show(WindowRule rule)
    {
        _rule = rule;
        HandleText.Text = Strings.Format("WindowRule.Handle", rule.Handle);
        TitleText.Text = rule.Title;
        ProcessText.Text = $"{Strings.Get("WindowRule.Process")}: {rule.Process}";
        ClassText.Text = $"{Strings.Get("WindowRule.Class")}: {rule.ClassName}";
        Status.Text = Strings.Get("WindowRule.Captured");
        RuleFields.IsVisible = true;
        AgainButton.IsVisible = true;
        UseButton.IsVisible = true;
        if (HowBox.SelectedIndex != WindowHow.IndexOf(rule.How))
        {
            HowBox.SelectedIndex = WindowHow.IndexOf(rule.How);
        }

        if (MatchBox.Text != rule.MatchValue)
        {
            MatchBox.Text = rule.MatchValue;
        }

        CaseBox.IsChecked = rule.CaseSensitive;
        TrimBox.IsChecked = rule.Trim;
        MultilineBox.IsChecked = rule.Multiline;
        var textComparison = WindowHow.UsesTextComparison(rule.How);
        ComparisonSwitches.IsVisible = textComparison;
        HandleHint.IsVisible = !textComparison;
        Preview(rule);
    }

    private void Preview(WindowRule rule)
    {
        if (rule.Reading.Match is WindowMatch.Handle && rule.Handle == 0)
        {
            PreviewText.Text = Strings.Get("WindowRule.BadHandle");
            UseButton.IsEnabled = false;
            return;
        }

        UseButton.IsEnabled = true;
        var (match, compare) = rule.Reading;
        try
        {
            var found = _windows.Filter(rule.MatchValue, match, compare, options: rule.Options);
            var lines = new System.Collections.Generic.List<string>
            {
                Strings.Format("WindowRule.Matches", found.Count),
            };
            lines.AddRange(found.Take(8).Select(window => window.Title));
            if (found.Count > 8)
            {
                lines.Add(Strings.Format("WindowRule.More", found.Count - 8));
            }

            PreviewText.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception)
        {
            PreviewText.Text = Strings.Get("WindowRule.BadPattern");
        }
    }

    private void UseEditedText(WindowRule rule)
    {
        var text = MatchBox.Text?.Trim() ?? string.Empty;
        var edited = rule.Reading.Match switch
        {
            WindowMatch.Process => rule with { Process = text },
            WindowMatch.ClassName => rule with { ClassName = text },
            WindowMatch.Handle => long.TryParse(text, out var handle)
                ? rule with { Handle = handle }
                : rule with { Handle = 0 },
            _ => rule with { Title = text },
        };
        _rule = edited;
        Preview(edited);
    }

    private string How()
        => WindowHow.Values[Math.Clamp(HowBox.SelectedIndex, 0, WindowHow.Values.Count - 1)];

    private void Finish(WindowRule? rule)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Chosen = rule;
        Close();
    }

    private void AttachHook()
    {
        if (_hookSource is not { } hook)
        {
            return;
        }

        hook.KeyDown += OnHookKeyDown;
        hook.KeyUp += OnHookKeyUp;
        _hook = hook;
    }

    private void DetachHook()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        hook.KeyDown -= OnHookKeyDown;
        hook.KeyUp -= OnHookKeyUp;
        _hook = null;
    }

    private void OnHookKeyDown(KeyHookEvent e)
    {
        if (e.Key == F1)
        {
            e.Suppress = true;
            var first = !_swallowF1Up;
            _swallowF1Up = true;
            if (first)
            {
                Dispatcher.UIThread.Post(Capture);
            }
        }
        else if (e.Key == Escape)
        {
            e.Suppress = true;
            var first = !_swallowEscapeUp;
            _swallowEscapeUp = true;
            if (first)
            {
                Dispatcher.UIThread.Post(() => Finish(null));
            }
        }
    }

    private void OnHookKeyUp(KeyHookEvent e)
    {
        if (e.Key == F1 && _swallowF1Up)
        {
            e.Suppress = true;
            _swallowF1Up = false;
        }
        else if (e.Key == Escape && _swallowEscapeUp)
        {
            e.Suppress = true;
            _swallowEscapeUp = false;
        }
    }

    private void OnLocalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Finish(null);
        }
    }
}
