using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One line of a step's returns page: a result the action can hand over, the box that says whether
/// the macro keeps it, and the name it goes by. Each result is named on its own, so a step that
/// reports a position and a count can be read back as either without the two sharing a name.
/// </summary>
public partial class ReturnsRowViewModel : ViewModelBase
{
    /// <summary>The name the dialog suggested last, so a name the user typed can be told apart.</summary>
    private string _suggested = string.Empty;

    /// <summary>The names this row has to steer clear of, refreshed as the dialog's picture changes.</summary>
    private HashSet<string> _taken;

    public ReturnsRowViewModel(ActionReturn result, IEnumerable<string>? known = null)
    {
        Result = result;
        _taken = new HashSet<string>(known ?? [], StringComparer.OrdinalIgnoreCase);
        Ticked = result.Primary;
    }

    /// <summary>The result this line is about, as the catalogue declares it.</summary>
    public ActionReturn Result { get; }

    public string Key => Result.Key;

    /// <summary>What the result is, in the interface language, which is also part of its name.</summary>
    public string Label => Result.LocalLabel;

    public string Hint => Result.LocalHint;

    public bool HasHint => Hint.Length > 0;

    /// <summary>
    /// 这一项长什么样，一句话说得完的那种：一个开关写 <c>true | false</c>，一条记录写
    /// <c>{x, y, left, top, …}</c>。名字旁边留着一大片空，说的正是「它到底装的是什么」，
    /// 而写成示例比再写一句说明省地方：字段名就是 <c>field($变量, "x")</c> 里要写的那个词。
    /// </summary>
    public string Structure => Shape(Result);

    /// <summary>
    /// 完整的结构：每个字段的名字与类型，加上取其中一项的写法。行里只放得下前几个字段，
    /// 有十一个字段的命中记录要在这里才看得全。
    /// </summary>
    public string StructureTip
    {
        get
        {
            // A table's fields are the columns it was read with, so there are none to list here;
            // what the reader needs instead is how to take one cell out of a row.
            if (Result.Table)
            {
                return Strings.Get("Add.ReturnShapeTipTable");
            }

            if (Result.Fields.Count == 0)
            {
                return Strings.Format("Add.ReturnShapeTipBare", Shape(Result));
            }

            // `field` 在 C# 14 里是属性访问器的上下文关键字，这里不能拿它当参数名。
            var fields = string.Join(", ",
                Result.Fields.Select(part => $"{part.Name}: {ActionReturn.TypeNameOf(part.Type)}"));
            return $"{fields}\n{Strings.Format("Add.ReturnShapeTip", Result.Fields.Count)}";
        }
    }

    /// <summary>How many field names a row shows before it says there are more.</summary>
    private const int FieldsShown = 4;

    /// <summary>
    /// The shape of a result as it is written on the line: a value for a plain one, and the first
    /// few field names for a record or a list, with an ellipsis standing for the rest.
    /// </summary>
    private static string Shape(ActionReturn result) => result.Type switch
    {
        ActionReturnType.Flag => Strings.Get("Add.ReturnShapeFlag"),
        ActionReturnType.Number => "0",
        ActionReturnType.Image => Strings.Get("Add.ReturnShapeImage"),
        ActionReturnType.Text => Strings.Get("Add.ReturnShapeText"),
        ActionReturnType.List when result.Table
            => $"[{{{Strings.Get("Add.ReturnShapeColumn")}: {Strings.Get("Add.ReturnShapeText")}}}, …]",
        ActionReturnType.List => result.Fields.Count == 0
            ? $"[{Strings.Get("Add.ReturnShapeText")}, …]"
            : $"[{{{Fields(result)}}}, …]",
        _ => $"{{{Fields(result)}}}",
    };

    /// <summary>The first few field names of a record, with an ellipsis when there are more.</summary>
    private static string Fields(ActionReturn result)
    {
        var shown = result.Fields.Take(FieldsShown).Select(field => field.Name);
        var names = string.Join(", ", shown);
        return result.Fields.Count > FieldsShown ? names + ", …" : names;
    }

    /// <summary>True for the result this action nearly always hands over, which starts ticked.</summary>
    public bool IsPrimary => Result.Primary;

    /// <summary>True while this result is one the step hands over.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameTaken))]
    [NotifyPropertyChangedFor(nameof(NameWarning))]
    [NotifyPropertyChangedFor(nameof(ShowsName))]
    public partial bool Ticked { get; set; }

    /// <summary>The name the result is handed over under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameTaken))]
    [NotifyPropertyChangedFor(nameof(NameWarning))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>True once the user has typed a name of their own into this line.</summary>
    public bool IsNameChanged { get; private set; }

    /// <summary>True while the line is worth showing a name for.</summary>
    public bool ShowsName => Ticked || Name.Length > 0;

    /// <summary>
    /// True when the name this result goes by is already in use somewhere the macro can see, which
    /// is how two steps end up writing into one variable.
    /// </summary>
    public bool IsNameTaken => Ticked
        && Name.Trim().Length > 0
        && _taken.Contains(Name.Trim());

    /// <summary>What the line under the name says when it is already taken.</summary>
    public string NameWarning => IsNameTaken
        ? Strings.Format("Add.NameTaken", Name.Trim())
        : string.Empty;

    /// <summary>
    /// Tells this line which names are spoken for, which is every variable the macro already has
    /// and the results of the lines above it.
    /// </summary>
    public void Knows(IEnumerable<string> taken)
    {
        _taken = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(IsNameTaken));
        OnPropertyChanged(nameof(NameWarning));
    }

    /// <summary>
    /// Writes the suggested name, unless the user has already chosen one. Called again whenever the
    /// note changes, so the name follows the note for as long as nobody has touched it.
    /// </summary>
    public void Suggest(string name)
    {
        if (IsNameChanged || name.Length == 0 || Name == name)
        {
            return;
        }

        // Written down before the text moves: the change handler tells a name the dialog chose from
        // one the user typed by comparing the two.
        _suggested = name;
        Name = name;
    }

    /// <summary>Gives the result a free name of its own, next to the one that was taken.</summary>
    public void MakeNameUnique()
    {
        var wanted = Name.Trim();
        if (wanted.Length == 0)
        {
            return;
        }

        var free = VariableNames.Free(wanted, _taken);
        if (free == wanted)
        {
            return;
        }

        IsNameChanged = true;
        _suggested = free;
        Name = free;
    }

    /// <summary>Loads the name an existing step handed this result over under.</summary>
    public void Load(string? name)
    {
        var stored = (name ?? string.Empty).Trim();
        Ticked = true;
        IsNameChanged = true;
        _suggested = stored;
        Name = stored;
    }

    /// <summary>Unticks a result an existing step did not hand over.</summary>
    public void Skip()
    {
        Ticked = false;
        IsNameChanged = false;
        _suggested = string.Empty;
        Name = string.Empty;
    }

    /// <summary>The row this result is written as, or nothing while it is not handed over.</summary>
    public StepParameterRow? ToRow() => Ticked && Name.Trim().Length > 0
        ? new StepParameterRow
        {
            Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [FieldNames.Key] = Key,
                [FieldNames.Name] = Name.Trim(),
            },
        }
        : null;

    partial void OnNameChanged(string value)
    {
        if (!string.Equals(value, _suggested, StringComparison.Ordinal))
        {
            IsNameChanged = true;
        }
    }
}
