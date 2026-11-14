using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// The window a field names: its summary, following the one above, and setting it from a pick.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>What the window field shows at a glance, or the variable name it reads.</summary>
    public string WindowRuleSummary
    {
        get
        {
            var written = Text?.Trim() ?? string.Empty;

            // A field that names a variable says the name. Which one it is comes from the value the
            // field already holds, not from a sign in front of it: a window title and a name are
            // two different shapes, and the shape is what the field keeps.
            if (_written is VariableNode variable)
            {
                return Formula.Speak(variable);
            }

            if (!WindowRule.TryParse(written, out var rule))
            {
                return NotAnswered;
            }

            // 沿用是一次指向，不是一条窗口：说清楚它是什么意思，比显示一串没有标题的括号有用；
            // 知道上面那一条是什么时，顺手把"现在会跟到哪个窗口"也说出来。
            if (rule.Follow)
            {
                var following = Strings.Get("WindowRule.Following");
                return Readable(WindowAboveRule) is { Length: > 0 } above
                    ? $"{following}：{above}"
                    : following;
            }

            var readable = Readable(written);
            return readable.Length > 0 ? readable : NotAnswered;
        }
    }

    /// <summary>
    /// What this field says when nobody has answered it. A window action left blank means the
    /// window in front; a field that has to be answered, as the program actions are, has no such
    /// fallback to offer, so it says it has not been answered.
    /// </summary>
    private string NotAnswered
        => Strings.Get(HasToBeAnswered ? "WindowRule.NotChosen" : "WindowRule.NotSet");

    /// <summary>
    /// True when this field has no answer it could fall back on: it is required, and not only under
    /// some settings of the action. A window action left blank is about the window in front; a
    /// program action left blank is about nothing, and the same goes for the list under it.
    /// </summary>
    public bool HasToBeAnswered => Definition.Required && !Definition.Semantics.Conditional;

    /// <summary>一条规则写成人看的一行；认不出来、或者里面没有窗口时是空串。</summary>
    private static string Readable(string written)
        => WindowRule.TryParse(written, out var rule) && rule.HasTarget
            ? $"{rule.MatchValue} · {Strings.Get($"WindowRule.Short.{rule.How}", rule.How)}"
            : string.Empty;

    /// <summary>
    /// 落点之上最近那一条窗口规则，由编辑器在打开这个表单时交进来：这一格写「沿用上一次」时，
    /// 摘要里说得出它会跟到哪个窗口。
    /// </summary>
    public string WindowAboveRule { get; set; } = string.Empty;

    public bool HasWindowRule => !string.IsNullOrWhiteSpace(Text);

    /// <summary>True when this field says "the same window the steps before this one used".</summary>
    public bool IsFollowingWindow
        => WindowRule.TryParse(Text, out var rule) && rule.Follow;

    /// <summary>Stores a captured rule as the one value this field writes.</summary>
    public void ApplyWindowRule(WindowRule rule)
    {
        SetWritten(Formula.Text(rule.ToJson().ToJsonString()));
        NotifyWindowRule();
    }

    /// <summary>
    /// 改用上一次用的窗口：不写死任何一个窗口，只说"跟前面一样"。前面那一步改了，这一串跟着改，
    /// 不用在每一条上重新绑一遍。
    /// </summary>
    public void FollowWindowRule()
    {
        SetWritten(Formula.Text(WindowRule.FollowPrevious.ToJson().ToJsonString()));
        NotifyWindowRule();
    }

    /// <summary>Uses a variable that holds a window record instead of a literal rule.</summary>
    public void UseWindowVariable(string name)
    {
        // The answer is the name itself as a shape. The preview, the coordinate picker and the
        // runner all read the shape, so there is no line with a sign in front of the name.
        WriteValue(Formula.Name(name.Trim()));
        NotifyWindowRule();
    }

    /// <summary>Forgets the window this field names.</summary>
    public void ClearWindowRule()
    {
        SetWritten(Formula.Nothing);
        NotifyWindowRule();
    }

    private void NotifyWindowRule()
    {
        OnPropertyChanged(nameof(WindowRuleSummary));
        OnPropertyChanged(nameof(HasWindowRule));
        OnPropertyChanged(nameof(IsFollowingWindow));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsIncluded));
    }
}
