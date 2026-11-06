using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Lucide.Avalonia;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One line of the action dialog's parameter list. The two halves of a screen position share a
/// line, so x and y sit side by side instead of leaving two half-empty rows. A line may also carry
/// one field that belongs beside the others rather than under them, such as the switch that takes a
/// mouse step's place from the pointer: it is read together with the field it stands next to.
/// </summary>
public sealed class ParameterRowViewModel : ViewModelBase
{
    /// <summary>True while one side of a two-way link is writing, so the other does not write back.</summary>
    private bool _syncing;

    public ParameterRowViewModel(StepParameterViewModel first, StepParameterViewModel? second = null,
        StepParameterViewModel? third = null, StepParameterViewModel? fourth = null)
    {
        First = first;
        Second = second;
        Third = third;
        Fourth = fourth;

        // Whether a line is drawn is a question about the step's settings rather than about the
        // line, so it is asked again whenever one of the fields on it has something new to say:
        // without this the form goes on showing a field the step no longer asks about until it is
        // built again, which for a step being written is never.
        foreach (var field in new[] { first, second, third, fourth })
        {
            if (field is not null)
            {
                field.PropertyChanged += OnFieldChanged;
            }
        }

        // 一个矩形的四个数压成一行时，每格用的是"短名 + 框 + ƒ"那种样子：四个完整字段并排，
        // 每个底下还带一段说明，就还是原来那两行。
        QuadCells = fourth is null
            ? []
            : [Cell(first), Cell(second!), Cell(third!), Cell(fourth)];
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(IsApplicable));
        OnPropertyChanged(nameof(IsDefault));
    }

    public StepParameterViewModel First { get; }

    public StepParameterViewModel? Second { get; }

    /// <summary>
    /// The third field of the line: the one that belongs beside the others on an ordinary line, or
    /// the width on the line the four numbers of a rectangle share.
    /// </summary>
    public StepParameterViewModel? Third { get; }

    /// <summary>The fourth number of a rectangle, which only that line has.</summary>
    public StepParameterViewModel? Fourth { get; }

    /// <summary>True when this line is the four numbers of one rectangle, pressed onto one line.</summary>
    public bool IsQuad => Fourth is not null;

    /// <summary>The four numbers of a rectangle as they are edited on that line, in reading order.</summary>
    public IReadOnlyList<NumberCellViewModel> QuadCells { get; }

    /// <summary>The block this line belongs to, empty for a line that answers no block of its own.</summary>
    public string GroupKey { get; init; } = string.Empty;

    /// <summary>Which block of the page this line is in, counted from one; zero when it is in none.</summary>
    public int GroupIndex { get; init; }

    /// <summary>True on the first line of a block, which is where the block says what it asks.</summary>
    public bool IsGroupStart { get; init; }

    /// <summary>
    /// What this block asks, in the interface language. Read on every ask so a language switch
    /// relabels the block along with the fields under it.
    /// </summary>
    public string GroupTitle
        => GroupKey.Length == 0 ? string.Empty : Strings.Get("Add.Group." + GroupKey);

    /// <summary>
    /// 这一块在问什么，一句话：放在标题旁那枚信息图标里，而不是铺在字段上面。没有写说明的块
    /// 不显示图标——空的图标比没有更让人分心。
    /// </summary>
    public string GroupHint
    {
        get
        {
            if (GroupKey.Length == 0)
            {
                return string.Empty;
            }

            var key = "Add.Group." + GroupKey + ".hint";
            var text = Strings.Get(key);
            return string.Equals(text, key, StringComparison.Ordinal) ? string.Empty : text;
        }
    }

    /// <summary>
    /// 这一块的图标：一眼说是"在哪儿找"还是"怎么点"。没有对应图标时为空，那一块头上仍旧
    /// 摆序号——序号说不清问的是什么，但总比空着一个圈强。
    /// </summary>
    public LucideIconKind? GroupIcon => AppIcons.Block(GroupKey);

    /// <summary>True when this block says what it asks with an icon rather than a number.</summary>
    public bool HasGroupIcon => GroupIcon is not null;

    public bool HasSecond => Second is not null;

    public bool HasThird => Third is not null;

    /// <summary>True when the first field stands on its own, which a line of four does not.</summary>
    public bool ShowsFirst => !IsQuad;

    /// <summary>True when the second field is drawn beside the first rather than under it.</summary>
    public bool ShowsSecond => !IsQuad && HasSecond;

    /// <summary>True when the third field is the one that belongs beside the line.</summary>
    public bool ShowsThird => !IsQuad && HasThird;

    /// <summary>
    /// False while what this line asks about is not a question for the way the step is set up.
    /// Such a line is not drawn at all, rather than being drawn with nothing behind it.
    /// </summary>
    public bool IsApplicable => First.IsApplicable
        && (Second?.IsApplicable ?? true)
        && (Third?.IsApplicable ?? true)
        && (Fourth?.IsApplicable ?? true);

    /// <summary>
    /// True while both halves of the line still hold what they started with, which is what the
    /// mark on a page's tab is read from: a page holding a changed value is worth pointing at.
    /// </summary>
    public bool IsDefault => First.IsDefault && (Second?.IsDefault ?? true)
        && (Third?.IsDefault ?? true) && (Fourth?.IsDefault ?? true);

    /// <summary>
    /// True when this line carries the button that picks a whole screen region. A field that holds
    /// one rectangle has that button on the rectangle itself, so the line under it does not repeat
    /// it; a search that may look in several places has no single row to hang it on, and the button
    /// that adds a picked place belongs under the list.
    /// </summary>
    public bool IsRegionAnchor => First.IsRegionAnchor;

    /// <summary>
    /// What the region button says. A drag fills two points rather than one rectangle, so its
    /// button names the direction instead of repeating the rectangle wording used elsewhere.
    /// </summary>
    public string RegionPickerLabel => First.Definition.OwnerKey == "input.mouseDrag"
        ? Strings.Get("Add.PickRegionDrag")
        : Strings.Get("Add.PickRegion");

    /// <summary>What the region button does, worded for the same two fields.</summary>
    public string RegionPickerHint => First.Definition.OwnerKey == "input.mouseDrag"
        ? Strings.Get("Add.PickRegionDragHint")
        : Strings.Get("Add.PickRegionHint");

    /// <summary>True when this line carries the point picker.</summary>
    public bool IsPointAnchor => First.IsPointAnchor;

    /// <summary>True when this line carries the window-bounds picker.</summary>
    public bool IsWindowBoundsAnchor => First.IsWindowBoundsAnchor;

    /// <summary>True when this line carries the button that picks a click point on the picture.</summary>
    public bool ShowsOffsetPick => First.IsOffsetAnchor;

    /// <summary>
    /// True when this line carries the list of a pattern's colours and the button that picks them.
    /// </summary>

    /// <summary>True when the line says a position can be taken from the pointer with Alt + X.</summary>
    public bool ShowsPositionHint => HasSecond && First.IsCoordinate;

    /// <summary>
    /// Half the line beside another editor, the whole line when it is on its own. The line is four
    /// columns wide, so those are two and four.
    /// </summary>
    public int FirstSpan => HasSecond ? 2 : 4;

    /// <summary>
    /// 把这一格接回它真正存放的地方：框里写什么，步骤的参数就写什么；反过来也一样。两边都要
    /// 防回写，否则一次输入会来回改成两次。
    /// </summary>
    private NumberCellViewModel Cell(StepParameterViewModel field)
    {
        var cell = new NumberCellViewModel(field.Label, field.Hint, field.Placeholder,
            field.QuadShortLabel);
        cell.Prepare(field.Variables);
        cell.Write(field.CurrentText);

        cell.PropertyChanged += (_, args) =>
        {
            if (_syncing || args.PropertyName != nameof(NumberCellViewModel.Text))
            {
                return;
            }

            _syncing = true;
            try
            {
                // 数字字段的值存在它自己的数里，不只看那行字：写进去之后要把数也定下来，
                // 否则框里有字、步骤却还是空着。
                field.WriteValue(cell.Text);
                if (field.IsNumber && !field.UseFormula
                    && decimal.TryParse(cell.Text, NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var number))
                {
                    field.NumberValue = number;
                }
            }
            finally
            {
                _syncing = false;
            }
        };

        field.PropertyChanged += (_, args) =>
        {
            if (_syncing || args.PropertyName is not (nameof(StepParameterViewModel.Text)
                    or nameof(StepParameterViewModel.NumberValue)
                    or nameof(StepParameterViewModel.UseFormula)))
            {
                return;
            }

            _syncing = true;
            try
            {
                cell.Write(field.CurrentText);
            }
            finally
            {
                _syncing = false;
            }
        };

        return cell;
    }
}
