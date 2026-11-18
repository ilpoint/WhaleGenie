using System;
using System.Collections.Generic;
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
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// 选这个步骤要用哪个窗口，以及用哪一项认出它。两条路并存：左边是此刻开着的窗口，点一条即可；
/// 或者点一下目标窗口再按 F1，把它抓下来。窗口一直压在最上面，但打开时不抢焦点，所以那一次
/// 点击会落在使用者瞄准的窗口上。
/// </summary>
public partial class WindowRuleWindow : AppWindow
{
    private static readonly HookKey F1 = KeyNames.Resolve("F1") ?? default;
    private static readonly HookKey Escape = KeyNames.Resolve("Esc") ?? default;

    private readonly IWindowDevice _windows;
    private readonly IScreenDevice _screen;
    private readonly IInputHook? _hookSource;
    private IInputHook? _hook;
    private WindowRule? _rule;
    private bool _done;
    private bool _picking;
    private bool _swallowF1Up;
    private bool _swallowEscapeUp;

    /// <summary>把列表里的选择写回去时置上，免得"程序选中"被当成"人选中了它"。</summary>
    private bool _restoring;

    public WindowRuleWindow()
        : this(SharedDevices.Current.Windows, SharedDevices.Current.Hook, null)
    {
    }

    /// <summary>
    /// The picker over just what it reads: the windows on this machine and the key hook that
    /// catches F1. Taking the two rather than the whole device layer says what this window needs,
    /// and lets a stand-in be the two things instead of twenty.
    /// </summary>
    internal WindowRuleWindow(IWindowDevice windows, IInputHook hook, WindowRule? current,
        IScreenDevice? screen = null)
    {
        InitializeComponent();
        _windows = windows;
        _screen = screen ?? SharedDevices.Current.Screen;
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

        SearchBox.TextChanged += (_, _) => FillWindows();
        RefreshButton.Click += (_, _) => FillWindows();
        WindowList.SelectionChanged += OnWindowChosen;
        PickButton.Click += (_, _) => _ = PickWindowAsync();
        CancelButton.Click += (_, _) => Finish(null);
        UseButton.Click += (_, _) => Finish(_rule is { } rule ? rule with { How = How() } : null);
        AddHandler(KeyDownEvent, OnLocalKeyDown, RoutingStrategies.Tunnel);

        FillWindows();

        // 空着的那一格也是"一条规则"，但它没有窗口：打开时仍旧是"还没选"的样子，不能一进来就
        // 说"已选好窗口"。下面那格"现在会认到"也要先把话摆上，否则它是一块空板。
        if (current is { HasTarget: true })
        {
            Show(current);
        }
        else
        {
            Preview(WindowRule.Empty);
        }
    }

    internal WindowRule? Chosen { get; private set; }

    /// <summary>
    /// How the window to use is taken from the desk. Left alone it is the picker; a check hands in
    /// something that answers without a desktop, so what is checked is this window handing over and
    /// taking back, not the pointing itself — that has checks of its own.
    /// </summary>
    internal Func<Task<WindowInfo?>>? Picking { get; set; }

    public static async Task<WindowRule?> PickAsync(Window owner, IWindowDevice windows,
        IInputHook hook, WindowRule? current = null)
    {
        using var aside = AppWindows.StepAside(owner);
        await AppWindows.SettleAsync();
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

    /// <summary>
    /// 列表里此刻开着的窗口，按搜索框筛过。鲸灵自己的窗口不算：宏要做的事都在别的窗口里，
    /// 而它排在最前面，正是最容易误选的那个。
    /// </summary>
    private void FillWindows()
    {
        var wanted = SearchBox.Text?.Trim() ?? string.Empty;
        var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

        IReadOnlyList<WindowEntry> open;
        try
        {
            open = WindowPickerViewModel.From(_windows);
        }
        catch (Exception)
        {
            open = [];
        }

        var shown = open
            .Where(entry => !string.Equals(entry.Process, own, StringComparison.OrdinalIgnoreCase)
                            && (wanted.Length == 0
                                || entry.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                                || entry.Process.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                                || entry.Class.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _restoring = true;
        WindowList.ItemsSource = shown;
        // 重新筛选一遍之后，当前这条规则指着的那个窗口仍旧是选中的那一条。
        WindowList.SelectedItem = shown.FirstOrDefault(entry => _rule?.Handle == entry.Handle);
        _restoring = false;

        EmptyText.IsVisible = shown.Count == 0;
    }

    /// <summary>在列表里点了一条：整条规则连程序名与窗口类一起记下，改天标题变了还能找回来。</summary>
    private void OnWindowChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_restoring || WindowList.SelectedItem is not WindowEntry entry)
        {
            return;
        }

        Show(new WindowRule(Title: entry.Title, Process: entry.Process, ClassName: entry.Class,
            Handle: entry.Handle) with
        { How = How() });
    }

    /// <summary>
    /// 到桌面上指一扇窗口：这个窗口连同鲸灵自己的都先让开，指针底下的窗口被框出来，点一下取走。
    /// 以前这里取的是"前台窗口"，而这个按钮一按下去，前台就是鲸灵自己——那一步从来取不到东西。
    /// </summary>
    private async Task PickWindowAsync()
    {
        if (_picking || _done)
        {
            return;
        }

        _picking = true;

        // 让位期间这个窗口的钩子不该还在听：拾取器按 Esc 时，两处都会以为那是给自己的。
        DetachHook();

        WindowInfo? found = null;
        try
        {
            using (AppWindows.StepAside(this))
            {
                await AppWindows.SettleAsync();
                found = Picking is { } pick
                    ? await pick()
                    : await WindowPickWindow.PickAsync(
                        _windows, _screen, _hookSource ?? SharedDevices.Current.Hook);
            }
        }
        catch (Exception)
        {
            found = null;
        }
        finally
        {
            _picking = false;

            // 让位期间 F1 的抬起没人接：留着这个标记，下一次 F1 就会被当成"已经按过"而不再响应。
            _swallowF1Up = false;
            _swallowEscapeUp = false;
            AttachHook();
        }

        if (found is null)
        {
            // 取消不是失败：话还是留在状态栏，窗口这一格原样不动。
            Status.Text = Strings.Get("WindowRule.None");
            return;
        }

        var process = string.Empty;
        var className = string.Empty;
        try
        {
            process = _windows.ProcessOf(found.Handle);
            className = _windows.ClassOf(found.Handle);
        }
        catch (Exception)
        {
            // 名字拿不到不妨碍按标题认它；那两项留空。
        }

        _rule = WindowRule.From(found, process, className);
        FillWindows();
        Show(_rule);
        Activate();
        MatchBox.Focus();
        MatchBox.SelectAll();
    }

    private void OnPickWindow(object? sender, RoutedEventArgs e) => _ = PickWindowAsync();

    private void Show(WindowRule rule)
    {
        _rule = rule;

        // 认的是这个窗口，说的是它的名字：标题在上，程序名与窗口类在下，空的那几项不摆出来。
        var name = rule.Title.Length > 0 ? rule.Title : rule.MatchValue;
        ChosenText.Text = name;
        ChosenText.IsVisible = name.Length > 0;

        var facts = string.Join(" · ",
            new[] { rule.Process, rule.ClassName }.Where(part => part.Length > 0));
        FactsText.Text = facts;
        FactsText.IsVisible = facts.Length > 0;

        Status.Text = Strings.Get("WindowRule.Captured");
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

        if (!_restoring)
        {
            _restoring = true;
            WindowList.SelectedItem = WindowList.ItemsSource?.Cast<WindowEntry>()
                .FirstOrDefault(entry => entry.Handle == rule.Handle);
            _restoring = false;
        }

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

        var (match, compare) = rule.Reading;
        try
        {
            var found = rule.HasTarget
                ? _windows.Filter(rule.MatchValue, match, compare, options: rule.Options)
                : [];

            // 一条空规则会认到"全部窗口"，那不是这一步的意思：还没选窗口时就说还没选。
            var lines = new List<string>
            {
                !rule.HasTarget ? Strings.Get("WindowRule.Preview.Waiting")
                : found.Count > 0 ? Strings.Format("WindowRule.Matches", found.Count)
                : Strings.Get("WindowRule.NoneMatch"),
            };
            lines.AddRange(found.Take(8).Select(window => window.Title));
            if (found.Count > 8)
            {
                lines.Add(Strings.Format("WindowRule.More", found.Count - 8));
            }

            PreviewText.Text = string.Join(Environment.NewLine, lines);
            UseButton.IsEnabled = rule.HasTarget;
        }
        catch (Exception)
        {
            PreviewText.Text = Strings.Get("WindowRule.BadPattern");
            UseButton.IsEnabled = false;
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
                Dispatcher.UIThread.Post(() => _ = PickWindowAsync());
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
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;

        // 拾取时这个窗口藏起来了，焦点却可能还留在它的某个框里：那一下 Esc 是给拾取器的，
        // 不能顺手把这个窗口也关掉——关掉之后取完/取消就回不来了。
        if (_picking)
        {
            return;
        }

        Finish(null);
    }
}
