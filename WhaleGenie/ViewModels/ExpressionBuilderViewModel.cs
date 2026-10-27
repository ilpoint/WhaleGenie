using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

// The language itself, spelled out: this dialog's own Expression property is the text being edited,
// which hides the static class of the same name.
using Language = WhaleGenie.Core.Expressions.Expression;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Backs the expression builder. The dialog has two pages over one formula: the structure page,
/// where the formula is a tree of lines the user fills in by clicking or by dragging, and the page
/// where it is written out as text for whoever would rather type it. The tree is the one the user
/// is meant to reach for — brackets are decided by the shape of the tree rather than by hand — and
/// the text page stays exactly as it was, buttons inserting at the caret.
/// </summary>
public partial class ExpressionBuilderViewModel : ViewModelBase
{
    /// <summary>The names the macro knows, so a half-written formula can be checked here.</summary>
    private readonly IReadOnlyList<string> _names;

    /// <summary>The piece the next click from the palette goes into, and the tree it stands in.</summary>
    private ExpressionItemViewModel _root;

    /// <summary>True while the formula being built has nothing in it at all.</summary>
    private bool _empty;

    /// <summary>True while the text the tree was last made from reads as a formula.</summary>
    private bool _previewReads;

    /// <summary>
    /// Every built-in function, read once from the language's own table. The list on screen is a
    /// narrowing of this one rather than a fresh reading, so switching category or typing in the
    /// filter never rebuilds what is not being looked at.
    /// </summary>
    private readonly IReadOnlyList<ExpressionFunctionChoice> _table;

    public ExpressionBuilderViewModel()
        : this(string.Empty, [])
    {
    }

    public ExpressionBuilderViewModel(string expression, IReadOnlyList<VariableChoice> variables)
    {
        Variables = variables;
        _names = [.. variables.Select(choice => choice.Name)];
        Wall = new VariableWallViewModel(variables);
        Operators = BuildOperators();
        TreeOperators = BuildTreeOperators();
        Values = BuildValues();
        FunctionFilters = BuildFunctionFilters();
        _table = BuildFunctionTable();
        SelectedFunctionFilter = FunctionFilters[0];
        Expression = expression ?? string.Empty;

        _root = ExpressionItemViewModel.Hole(Changed);
        if (Language.TryParse(Expression, out var parsed, out _))
        {
            _root = ExpressionItemViewModel.From(parsed!, Changed);
        }
        else
        {
            // Nothing written yet is not a mistake: it opens as one empty place to fill in. Text
            // that was meant to be a formula and does not read stays where it belongs, on the page
            // it can be typed on.
            IsTreePage = string.IsNullOrWhiteSpace(Expression);
            TextUnreadable = !IsTreePage;
        }

        Selected = _root;
        Redraw();
    }

    /// <summary>Raised with the finished expression, or null when the dialog was dismissed.</summary>
    public event Action<string?>? CloseRequested;

    public string Header => Strings.Get("ExprBuilder.Title");

    public string Help => Strings.Get("ExprBuilder.Help");

    public string OperatorsHeader => Strings.Get("ExprBuilder.Operators");

    public string ExpressionHeader => Strings.Get("ExprBuilder.Expression");

    public string VariablesHeader => Strings.Get("ExprBuilder.Variables");

    public string FunctionsHeader => Strings.Get("ExprBuilder.Functions");

    public string FunctionsFilterHint => Strings.Get("ExprBuilder.FunctionsFilter");

    public string InsertLabel => Strings.Get("ExprBuilder.Insert");

    public string Placeholder => Strings.Get("ExprBuilder.Placeholder");

    public string TreeTab => Strings.Get("ExprBuilder.Tab.Tree");

    public string TextTab => Strings.Get("ExprBuilder.Tab.Text");

    public string TreeHelp => Strings.Get("ExprBuilder.Tree.Help");

    public string TreeCannotRead => Strings.Get("ExprBuilder.Tree.CannotRead");

    public string TreePreviewHeader => Strings.Get("ExprBuilder.Tree.Preview");

    public string ValuesHeader => Strings.Get("ExprBuilder.Values");

    /// <summary>Every operator the expression language reads, each written the way it is inserted.</summary>
    public IReadOnlyList<ExpressionOperator> Operators { get; }

    /// <summary>
    /// The operators the structure page offers. Brackets are not among them: the tree decides where
    /// they go, which is the whole point of building a formula this way.
    /// </summary>
    public IReadOnlyList<TreeOperator> TreeOperators { get; }

    /// <summary>The plain values a place can be filled with in one click.</summary>
    public IReadOnlyList<ValueChoice> Values { get; }

    /// <summary>Every variable the macro can read, with what a person choosing one needs to know.</summary>
    public IReadOnlyList<VariableChoice> Variables { get; }

    /// <summary>
    /// The functions on the wall, which is what the dock above them and the filter beside it leave
    /// of everything the language has. The list is flat: which group a function belongs to is said
    /// by the colour of its card and by the dock the reader pressed, not by a heading that would
    /// stand over a single group.
    /// </summary>
    public ObservableCollection<ExpressionFunctionChoice> Functions { get; } = [];

    /// <summary>
    /// Which group of functions the dock is showing, all of them first: nothing is hidden until
    /// the reader asks for one group, so the wall opens on everything there is.
    /// </summary>
    public IReadOnlyList<ExpressionFunctionFilter> FunctionFilters { get; }

    /// <summary>The wall of variable buttons, which drops the one that was picked in at the caret.</summary>
    public VariableWallViewModel Wall { get; }

    /// <summary>
    /// The formula as the structure page draws it: one line for each piece, moved across by how deep
    /// it sits. The lines are the pieces themselves, so a line can be picked and dropped on.
    /// </summary>
    public ObservableCollection<ExpressionItemViewModel> Rows { get; } = [];

    /// <summary>The expression being written. The text page keeps the caret so buttons insert where it is.</summary>
    [ObservableProperty]
    public partial string Expression { get; set; } = string.Empty;

    /// <summary>What the line under the text page shows: the result, why it fails, or nothing yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowOk))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowOk))]
    public partial bool HasError { get; set; }

    /// <summary>True while the line under the text page is a problem worth flagging in red.</summary>
    public bool ShowError => HasError && Message.Length > 0;

    /// <summary>True while the line under the text page is worth showing in green.</summary>
    public bool ShowOk => !HasError && Message.Length > 0;

    /// <summary>True while the structure page is the one on show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextPage))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(ShowsSaying))]
    public partial bool IsTreePage { get; set; } = true;

    /// <summary>True while the page the formula is written out on is the one on show.</summary>
    public bool IsTextPage => !IsTreePage;

    /// <summary>
    /// True while the text page holds something that was meant to be a formula and does not read.
    /// The structure page then has no tree to show, and says so rather than showing a stale one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial bool TextUnreadable { get; set; }

    /// <summary>The piece the next click from the palette goes into.</summary>
    [ObservableProperty]
    public partial ExpressionItemViewModel? Selected { get; set; }

    /// <summary>The piece something being dragged is over, which is the one it would land on.</summary>
    [ObservableProperty]
    public partial ExpressionItemViewModel? DropTarget { get; set; }

    /// <summary>The formula the tree comes to, written out, with a place marker for anything unfilled.</summary>
    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    /// <summary>
    /// The formula said in plain Chinese, with the heading in front of it: what a reader who does not
    /// read formulas checks their own logic against before running anything. It is a Chinese reading
    /// of a Chinese word list, so an English interface has no line here at all.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSaying))]
    public partial string Saying { get; set; } = string.Empty;

    /// <summary>True while there is a sentence to read and the page it belongs to is the one on show.</summary>
    public bool ShowsSaying => IsTreePage && Saying.Length > 0;

    /// <summary>What the line under the tree says: what is missing, or what the formula comes to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTreeNote))]
    [NotifyPropertyChangedFor(nameof(ShowTreeProblem))]
    [NotifyPropertyChangedFor(nameof(ShowTreeOk))]
    public partial string TreeMessage { get; set; } = string.Empty;

    /// <summary>What kind of thing the line under the tree is saying.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTreeNote))]
    [NotifyPropertyChangedFor(nameof(ShowTreeProblem))]
    [NotifyPropertyChangedFor(nameof(ShowTreeOk))]
    public partial TreeMessageKind TreeMessageKind { get; set; }

    /// <summary>True while the line under the tree is a remark rather than a result.</summary>
    public bool ShowTreeNote => TreeMessageKind == TreeMessageKind.Note && TreeMessage.Length > 0;

    /// <summary>True while the line under the tree is a problem worth flagging in red.</summary>
    public bool ShowTreeProblem => TreeMessageKind == TreeMessageKind.Problem
        && TreeMessage.Length > 0;

    /// <summary>True while the line under the tree is worth showing in green.</summary>
    public bool ShowTreeOk => TreeMessageKind == TreeMessageKind.Ok && TreeMessage.Length > 0;

    /// <summary>What is picked right now, said the way the formula would be written.</summary>
    public string Picked => Selected is { } item
        ? Strings.Format("ExprBuilder.Tree.Picked", Language.Render(item.ToNode()))
        : string.Empty;

    /// <summary>What is typed into the filter above the function list, empty to show them all.</summary>
    [ObservableProperty]
    public partial string FunctionFilter { get; set; } = string.Empty;

    /// <summary>Which group of the dock is pressed. One of them always is, all of them to start with.</summary>
    [ObservableProperty]
    public partial ExpressionFunctionFilter? SelectedFunctionFilter { get; set; }

    /// <summary>
    /// True while the two narrowings together leave nothing on the wall. The list is flat, so an
    /// empty one says nothing at all about why: this is what makes it say something.
    /// </summary>
    public bool HasNoFunctionMatch => Functions.Count == 0;

    /// <summary>
    /// Whether the dialog can be closed with the formula it is showing. The structure page holds
    /// back a formula with places still to fill in — handing one over would write a step that
    /// cannot run — while the text page leaves it to the user, the way it always has.
    /// </summary>
    public bool CanConfirm => !IsTreePage || (!TextUnreadable && (_empty || _previewReads));

    partial void OnExpressionChanged(string value) => Recheck();

    partial void OnFunctionFilterChanged(string value) => Refill();

    partial void OnSelectedFunctionFilterChanged(ExpressionFunctionFilter? value) => Refill();

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(IsTreePage && !_empty ? Preview : Expression);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>Puts the page the formula is written out on in front of the user.</summary>
    [RelayCommand]
    private void ShowText()
    {
        if (!IsTreePage)
        {
            return;
        }

        // The tree and the text are the same formula, so a finished tree is written into the box
        // and the user carries on from there. A formula with places still to fill in would only put
        // markers in the box, so the box keeps what it had.
        if (_root.Holes() == 0 && !_empty)
        {
            Expression = Preview;
        }

        TextUnreadable = false;
        IsTreePage = false;
        Recheck();
    }

    /// <summary>
    /// Puts the structure page in front of the user, making the tree out of what is written if it
    /// reads. Text that does not read leaves the tree as it was and is reported instead, because
    /// there is nothing true to show for it.
    /// </summary>
    [RelayCommand]
    private void ShowTree()
    {
        if (IsTreePage)
        {
            return;
        }

        if (Language.TryParse(Expression, out var parsed, out _))
        {
            Adopt(ExpressionItemViewModel.From(parsed!, Changed));
            TextUnreadable = false;
        }
        else if (string.IsNullOrWhiteSpace(Expression))
        {
            Adopt(ExpressionItemViewModel.Hole(Changed));
            TextUnreadable = false;
        }
        else
        {
            TextUnreadable = true;
        }

        IsTreePage = true;
        Refresh();
    }

    /// <summary>Picks the piece the next click from the palette goes into.</summary>
    public void Select(ExpressionItemViewModel? item)
    {
        if (item is null || ReferenceEquals(Selected, item))
        {
            return;
        }

        if (Selected is { } was)
        {
            was.IsSelected = false;
        }

        Selected = item;
        item.IsSelected = true;
        OnPropertyChanged(nameof(Picked));
    }

    /// <summary>
    /// Says which piece something being dragged is over, which is the one it would land on. The
    /// line lights up rather than the whole tree, because where a piece lands is the whole question.
    /// </summary>
    public void MarkDrop(ExpressionItemViewModel? item)
    {
        if (ReferenceEquals(DropTarget, item))
        {
            return;
        }

        if (DropTarget is { } was)
        {
            was.IsDropTarget = false;
        }

        DropTarget = item;
        if (item is not null)
        {
            item.IsDropTarget = true;
        }
    }

    /// <summary>Puts a variable the macro holds where the picked piece is.</summary>
    public void ChooseVariable(string name)
    {
        if (TextUnreadable)
        {
            return;
        }

        Fill(ExpressionItemViewModel.Variable(name, Changed));
    }

    /// <summary>Puts one of the words that stand for a value where the picked piece is.</summary>
    public void ChooseConstant(string word)
    {
        if (TextUnreadable)
        {
            return;
        }

        Fill(ExpressionItemViewModel.Constant(word, Changed));
    }

    /// <summary>
    /// Fills the picked piece with a plain value. A number or a piece of text is typed in place, so
    /// the button only settles how what is typed there is read.
    /// </summary>
    public void ChooseValue(ValueChoice choice)
    {
        if (TextUnreadable)
        {
            return;
        }

        var item = Selected ?? _root;
        switch (choice.Kind)
        {
            case ValueChoiceKind.Number when item.IsTyped:
                item.ReadAsText = false;
                break;
            case ValueChoiceKind.Text when item.IsTyped:
                item.ReadAsText = true;
                break;
            case ValueChoiceKind.Number:
                Fill(ExpressionItemViewModel.Typed(string.Empty, false, Changed));
                break;
            case ValueChoiceKind.Text:
                Fill(ExpressionItemViewModel.Typed(string.Empty, true, Changed));
                break;
            default:
                Fill(ExpressionItemViewModel.Constant(choice.Word, Changed));
                break;
        }

        Refresh();
    }

    /// <summary>
    /// Wraps the picked piece in an operator: what was picked becomes the left operand and a place
    /// to fill appears on the right, which is the piece of a formula a user is about to write next.
    /// </summary>
    public void ChooseOperator(TreeOperator choice)
    {
        if (TextUnreadable)
        {
            return;
        }

        var target = Selected ?? _root;
        switch (choice.Kind)
        {
            case TreeOperatorKind.Binary:
                var right = ExpressionItemViewModel.Hole(Changed);
                var sum = ExpressionItemViewModel.Binary(choice.Operation, Changed);
                sum.Children.Add(target);
                sum.Children.Add(right);
                Replace(target, sum);
                target.Parent = sum;
                right.Parent = sum;
                Select(right);
                break;

            case TreeOperatorKind.Unary:
                var operand = ExpressionItemViewModel.Unary(choice.Operation, Changed);
                operand.Children.Add(target);
                Replace(target, operand);
                target.Parent = operand;
                Select(target);
                break;

            default:
                var list = ExpressionItemViewModel.List(Changed);
                list.Children.Add(target);
                Replace(target, list);
                target.Parent = list;
                Select(target);
                break;
        }

        Redraw();
    }

    /// <summary>
    /// Makes the picked piece the first argument of a function, with a place to fill for each of
    /// the others: a function is chosen to hold what is already there, not to replace it.
    /// </summary>
    public void ChooseFunction(ExpressionFunctionChoice choice)
    {
        if (TextUnreadable)
        {
            return;
        }

        Wrap(choice.Function, Selected ?? _root);
    }

    /// <summary>Adds an argument or an item under a piece that takes more of them.</summary>
    public void AddChild(ExpressionItemViewModel parent)
    {
        if (TextUnreadable || !parent.CanAddChild)
        {
            return;
        }

        var child = ExpressionItemViewModel.Hole(Changed);
        child.Parent = parent;
        parent.Children.Add(child);
        Select(child);
        Redraw();
    }

    /// <summary>Takes the last argument or item away again.</summary>
    public void DropChild(ExpressionItemViewModel parent)
    {
        if (TextUnreadable || !parent.CanDropChild)
        {
            return;
        }

        parent.Children.RemoveAt(parent.Children.Count - 1);
        Select(parent);
        Redraw();
    }

    /// <summary>
    /// Takes away what is picked: an argument or an item goes altogether when the piece above it
    /// can do without it, and anything else becomes a place to fill in again.
    /// </summary>
    public void Clear(ExpressionItemViewModel item)
    {
        if (TextUnreadable)
        {
            return;
        }

        var parent = item.Parent;
        if (parent is { Kind: ExpressionItemKind.List }
            || (parent is { Kind: ExpressionItemKind.Call } && parent.Children.Count > parent.MinArguments))
        {
            parent.Children.Remove(item);
            item.Parent = null;
            Select(parent);
            Redraw();
            return;
        }

        var hole = ExpressionItemViewModel.Hole(Changed);
        Replace(item, hole);
        Select(hole);
        Redraw();
    }

    /// <summary>
    /// Lands something that was dragged onto a piece: what is dropped replaces what it lands on,
    /// except for a function, which holds the piece it lands on as its first argument the same way
    /// clicking one does. A piece dragged into its own inside is left alone, which is a formula that
    /// cannot be built rather than one that reads oddly.
    /// </summary>
    public void Drop(ExpressionItemViewModel target, string payload)
    {
        if (TextUnreadable)
        {
            return;
        }

        if (!TreeDropped(target, payload))
        {
            return;
        }

        Redraw();
    }

    /// <summary>
    /// Whether something being dragged would land on this piece at all, which is what the line
    /// under the pointer lights up on: a line that would quietly do nothing is worse lit up than
    /// left alone.
    /// </summary>
    public bool CanLand(ExpressionItemViewModel? target, string? payload)
    {
        if (TextUnreadable || target is null || Payload(payload) is not { } dragging)
        {
            return false;
        }

        switch (dragging.Kind)
        {
            case "var":
            case "const":
                return true;

            case "fn":
                return Function(dragging.Value) is not null;

            case "move":
                return At(dragging.Value) is { } moved && !IsInside(target, moved);

            default:
                return false;
        }
    }

    /// <summary>
    /// What a piece being dragged is called for the length of the drag: where it sits in the tree.
    /// The piece itself cannot travel — the drag is handed over as plain text — so the tree is asked
    /// for it again when it lands.
    /// </summary>
    public string PayloadOf(ExpressionItemViewModel item)
    {
        var steps = new List<int>();
        for (var at = item; at.Parent is { } parent; at = parent)
        {
            steps.Insert(0, parent.Children.IndexOf(at));
        }

        return steps.Count == 0 ? "move:-" : "move:" + string.Join(".", steps);
    }

    /// <summary>
    /// Says what the expression comes to, the same way the field under it does, so the user does
    /// not have to close the dialog to find out whether it reads.
    /// </summary>
    private void Recheck()
    {
        var (message, failed) = NumberField.Check(Expression, _names);
        Message = message;
        HasError = failed;
    }

    /// <summary>
    /// Works the tree out again for everything that can be told from it: what it comes to, what is
    /// still missing, and what the word under the tree has to say about it.
    /// </summary>
    private void Refresh()
    {
        var node = _root.ToNode();
        Preview = Language.Render(node);
        var holes = _root.Holes();
        _empty = _root is { Kind: ExpressionItemKind.Hole, Children.Count: 0 };
        _previewReads = !_empty && Language.TryParse(Preview, out _, out _);

        // The sentence is said under the formula it was read from, and it is said about the tree
        // rather than about what the tree writes out: a place still to fill has a way of being said,
        // which is what makes the line worth having while a formula is being built.
        Saying = ExpressionText.InChinese && !TextUnreadable && !_empty
            ? Strings.Get("Expr.Say.Header")
                + ExpressionPhrase.Say(node, VariableCatalog.Spoken)
            : string.Empty;

        if (TextUnreadable)
        {
            TreeMessage = string.Empty;
            TreeMessageKind = TreeMessageKind.None;
        }
        else if (_empty)
        {
            TreeMessage = Strings.Get("ExprBuilder.Tree.Nothing");
            TreeMessageKind = TreeMessageKind.Note;
        }
        else if (holes > 0)
        {
            TreeMessage = Strings.Format("ExprBuilder.Tree.ToFill", holes);
            TreeMessageKind = TreeMessageKind.Note;
        }
        else if (!Language.TryParse(Preview, out _, out var error))
        {
            TreeMessage = ExpressionText.Describe(error!);
            TreeMessageKind = TreeMessageKind.Problem;
        }
        else
        {
            var (message, failed) = NumberField.Check(Preview, _names);
            TreeMessage = message;
            TreeMessageKind = failed ? TreeMessageKind.Problem : TreeMessageKind.Ok;
        }

        // A finished tree and the page it is written out on are the same formula, so the box keeps
        // up with it. A half-built one has nothing to say to the box, and text that does not read
        // is the box's own business: whatever is on that page is what the user wrote there.
        if (!TextUnreadable && holes == 0 && !_empty)
        {
            Expression = Preview;
        }
        else if (!TextUnreadable && _empty)
        {
            Expression = string.Empty;
        }

        OnPropertyChanged(nameof(CanConfirm));
    }

    private void Fill(ExpressionItemViewModel item)
    {
        var target = Selected ?? _root;
        Replace(target, item);
        Select(item);
        Redraw();
    }

    /// <summary>Lands a dragged payload, reporting whether the tree changed.</summary>
    private bool TreeDropped(ExpressionItemViewModel target, string payload)
    {
        switch (Payload(payload))
        {
            case ("var", var name):
                var variable = ExpressionItemViewModel.Variable(name, Changed);
                Replace(target, variable);
                Select(variable);
                return true;

            case ("const", var word):
                var value = ExpressionItemViewModel.Constant(word, Changed);
                Replace(target, value);
                Select(value);
                return true;

            case ("fn", var name):
                if (Function(name) is not { } function)
                {
                    return false;
                }

                Wrap(function, target);
                return true;

            case ("move", var path):
                var moved = At(path);
                if (moved is null || IsInside(target, moved))
                {
                    return false;
                }

                Detach(moved);
                Replace(target, moved);
                Select(moved);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The function a name stands for, if the language has one.</summary>
    private ExpressionFunction? Function(string name)
        => Language.Functions.FirstOrDefault(
            entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a payload: what is being dragged and which one it is.</summary>
    private static (string Kind, string Value)? Payload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        var colon = payload.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == payload.Length - 1)
        {
            return null;
        }

        var kind = payload[..colon];
        var value = payload[(colon + 1)..];
        return kind is "var" or "const" or "fn" or "move" ? (kind, value) : null;
    }

    /// <summary>The piece a path of child numbers leads to, or nothing when it leads nowhere.</summary>
    private ExpressionItemViewModel? At(string path)
    {
        if (path == "-")
        {
            return _root;
        }

        var at = _root;
        foreach (var step in path.Split('.'))
        {
            if (!int.TryParse(step, out var index) || index < 0 || index >= at.Children.Count)
            {
                return null;
            }

            at = at.Children[index];
        }

        return at;
    }

    /// <summary>True when <paramref name="inner"/> is the piece itself or something inside it.</summary>
    private static bool IsInside(ExpressionItemViewModel inner, ExpressionItemViewModel outer)
    {
        for (var at = inner; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, outer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Makes the picked piece the first argument of a function, with a place to fill for each of
    /// the ones after it. A function that takes nothing replaces what was picked, since there is
    /// nowhere for it to go.
    /// </summary>
    private void Wrap(ExpressionFunction function, ExpressionItemViewModel target)
    {
        var call = ExpressionItemViewModel.Call(function, Changed);
        var wanted = function.MaxArguments == 0 ? 0 : Math.Max(function.MinArguments, 1);
        if (wanted == 0)
        {
            Replace(target, call);
            Select(call);
            Redraw();
            return;
        }

        call.Children.Add(target);
        for (var index = 1; index < wanted; index++)
        {
            var hole = ExpressionItemViewModel.Hole(Changed);
            hole.Parent = call;
            call.Children.Add(hole);
        }

        // The piece is put where it stands before it is told what it now stands in: where it came
        // from is what the piece of tree above it has to be swapped for.
        Replace(target, call);
        target.Parent = call;
        Select(call.Children.Count > 1 ? call.Children[1] : target);
        Redraw();
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> where <paramref name="target"/> stands, which is how a
    /// piece grows: the tree above it keeps its shape and only this one place changes. The piece
    /// being replaced has to still stand where it stood when this is called — where it came from is
    /// what has to be swapped — so a piece that now holds it is told so afterwards.
    /// </summary>
    private void Replace(ExpressionItemViewModel target, ExpressionItemViewModel replacement)
    {
        var parent = target.Parent;
        replacement.Parent = parent;
        if (parent is null)
        {
            _root = replacement;
            return;
        }

        parent.Children[parent.Children.IndexOf(target)] = replacement;
    }

    /// <summary>
    /// Takes a piece out of the tree it stood in, for it to be put somewhere else. What it leaves
    /// behind is a place to fill in, unless the piece above it does without it: a list or a
    /// function with an argument to spare simply closes up.
    /// </summary>
    private void Detach(ExpressionItemViewModel item)
    {
        if (item.Parent is not { } parent)
        {
            return;
        }

        item.Parent = null;
        if (parent.Kind == ExpressionItemKind.List
            || (parent.Kind == ExpressionItemKind.Call && parent.Children.Count > parent.MinArguments))
        {
            parent.Children.Remove(item);
            return;
        }

        var hole = ExpressionItemViewModel.Hole(Changed);
        hole.Parent = parent;
        parent.Children[parent.Children.IndexOf(item)] = hole;
    }

    /// <summary>Takes on a tree that was read from the text or built fresh.</summary>
    private void Adopt(ExpressionItemViewModel root)
    {
        _root = root;
        Selected = root;
        Redraw();
    }

    /// <summary>
    /// Draws the tree again, one line to a piece, and says what it comes to: every change to the
    /// shape of a formula goes through here, so the lines, the formula under them and what the page
    /// has to say about it are never two answers from different moments.
    /// </summary>
    private void Redraw()
    {
        foreach (var row in Rows)
        {
            row.IsDropTarget = false;
        }

        Rows.Clear();
        if (Selected is null || !Holds(_root, Selected))
        {
            Selected = FirstHole(_root) ?? _root;
        }

        Walk(_root, 0);
        OnPropertyChanged(nameof(Picked));
        Refresh();
    }

    private void Walk(ExpressionItemViewModel node, int depth)
    {
        node.Depth = depth;
        node.IsSelected = ReferenceEquals(node, Selected);
        node.IsDropTarget = ReferenceEquals(node, DropTarget);
        Rows.Add(node);

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var (label, hint) = ArgumentName(node, index);
            child.Label = label;
            child.LabelHint = hint;

            // Which argument this line is decides how it is edited: a yes or no is ticked, a
            // comparison is picked from its words, everything else is typed.
            child.Stands(node.Kind == ExpressionItemKind.Call && node.Parameters.Count > 0
                ? node.Parameters[Math.Min(index, node.Parameters.Count - 1)]
                : null);

            Walk(child, depth + 1);
        }
    }

    /// <summary>
    /// What the argument at a position is called, in brackets so that the lines under a function
    /// read as the places it has rather than as more of the formula. A function that takes more than
    /// its signature names repeats the last name, and one that takes none has no lines to name.
    /// </summary>
    private static (string Label, string Hint) ArgumentName(ExpressionItemViewModel parent, int index)
    {
        if (parent.Kind != ExpressionItemKind.Call || parent.Parameters.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var parameter = parent.Parameters[Math.Min(index, parent.Parameters.Count - 1)];
        return ("[" + ExpressionText.ParameterLabel(parent.Name, parameter) + "]",
            ExpressionText.ParameterHint(parent.Name, parameter));
    }

    /// <summary>True while a piece is still part of the tree the page is drawing.</summary>
    private static bool Holds(ExpressionItemViewModel root, ExpressionItemViewModel item)
    {
        for (var at = item; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first place with nothing in it, which is where the user carries on from.</summary>
    private static ExpressionItemViewModel? FirstHole(ExpressionItemViewModel node)
    {
        if (node.Kind == ExpressionItemKind.Hole)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (FirstHole(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Everything the tree changed by itself reports back through here.</summary>
    private void Changed() => Refresh();

    /// <summary>
    /// The operators, in the order the language reads them: arithmetic first, then comparison,
    /// then the words that join conditions. The text inserted is spaced out so two clicks make a
    /// formula that still reads after it is written.
    /// </summary>
    private static IReadOnlyList<ExpressionOperator> BuildOperators() =>
    [
        new("+", " + ", "Expr.Op.Add"),
        new("−", " - ", "Expr.Op.Subtract"),
        new("×", " * ", "Expr.Op.Multiply"),
        new("÷", " / ", "Expr.Op.Divide"),
        new("%", " % ", "Expr.Op.Remainder"),
        new("=", " == ", "Expr.Op.Equals"),
        new("≠", " != ", "Expr.Op.NotEquals"),
        new(">", " > ", "Expr.Op.Greater"),
        new("≥", " >= ", "Expr.Op.GreaterOrEqual"),
        new("<", " < ", "Expr.Op.Less"),
        new("≤", " <= ", "Expr.Op.LessOrEqual"),
        new("and", " and ", "Expr.Op.And"),
        new("or", " or ", "Expr.Op.Or"),
        new("not", "not ", "Expr.Op.Not"),
        new("( )", "()", "Expr.Op.Group"),
        new("[ ]", "[]", "Expr.Op.List"),
        new("\"{ }\"", "\"\"", "Expr.Op.Text"),
    ];

    /// <summary>
    /// The operators the structure page offers, which is the same set without the brackets and
    /// without the quotes: there the tree decides where brackets go, a piece of text is a value to
    /// fill in, and a list is a piece of its own.
    /// </summary>
    private static IReadOnlyList<TreeOperator> BuildTreeOperators() =>
    [
        new("+", "+", TreeOperatorKind.Binary, "Expr.Op.Add"),
        new("−", "-", TreeOperatorKind.Binary, "Expr.Op.Subtract"),
        new("×", "*", TreeOperatorKind.Binary, "Expr.Op.Multiply"),
        new("÷", "/", TreeOperatorKind.Binary, "Expr.Op.Divide"),
        new("%", "%", TreeOperatorKind.Binary, "Expr.Op.Remainder"),
        new("=", "==", TreeOperatorKind.Binary, "Expr.Op.Equals"),
        new("≠", "!=", TreeOperatorKind.Binary, "Expr.Op.NotEquals"),
        new(">", ">", TreeOperatorKind.Binary, "Expr.Op.Greater"),
        new("≥", ">=", TreeOperatorKind.Binary, "Expr.Op.GreaterOrEqual"),
        new("<", "<", TreeOperatorKind.Binary, "Expr.Op.Less"),
        new("≤", "<=", TreeOperatorKind.Binary, "Expr.Op.LessOrEqual"),
        new("and", "and", TreeOperatorKind.Binary, "Expr.Op.And"),
        new("or", "or", TreeOperatorKind.Binary, "Expr.Op.Or"),
        new("not", "not", TreeOperatorKind.Unary, "Expr.Op.Not"),
        new("−x", "-", TreeOperatorKind.Unary, "ExprBuilder.Op.Negate"),
        new(Strings.Get("ExprBuilder.Tree.List"), string.Empty, TreeOperatorKind.List,
            "ExprBuilder.Op.List"),
    ];

    /// <summary>The plain values the structure page offers beside the variables.</summary>
    private static IReadOnlyList<ValueChoice> BuildValues() =>
    [
        new(ValueChoiceKind.Number, "ExprBuilder.Value.Number", "ExprBuilder.Value.NumberHint"),
        new(ValueChoiceKind.Text, "ExprBuilder.Value.Text", "ExprBuilder.Value.TextHint"),
        new(ValueChoiceKind.Constant, "ExprBuilder.Value.True", "ExprBuilder.Value.TrueHint", "true"),
        new(ValueChoiceKind.Constant, "ExprBuilder.Value.False", "ExprBuilder.Value.FalseHint", "false"),
        new(ValueChoiceKind.Constant, "ExprBuilder.Value.Null", "ExprBuilder.Value.NullHint", "null"),
    ];

    /// <summary>
    /// Every built-in function, in the order the table is written, which is the order the manual
    /// lists them in as well.
    /// </summary>
    private static IReadOnlyList<ExpressionFunctionChoice> BuildFunctionTable()
        => [.. Language.Functions.Select(function => new ExpressionFunctionChoice(function))];

    /// <summary>
    /// The dock's buttons: every group the language files its functions under, in the order the
    /// table first mentions them, with all of them in front of the rest. The names are the very
    /// ones the manual's function table is divided by, so a reader who met a group there finds the
    /// same word here.
    /// </summary>
    private static IReadOnlyList<ExpressionFunctionFilter> BuildFunctionFilters()
    {
        var filters = new List<ExpressionFunctionFilter>
        {
            new(string.Empty, "ExprBuilder.FunctionsAll"),
        };

        foreach (var function in Language.Functions)
        {
            if (!filters.Any(filter => filter.Group == function.Group))
            {
                filters.Add(new ExpressionFunctionFilter(function.Group,
                    $"Expr.Group.{function.Group}"));
            }
        }

        return filters;
    }

    /// <summary>
    /// Draws the wall again from the two questions that narrow it: which group the dock is on, and
    /// what was typed in the filter. The filter reads the function's name in either language — a
    /// reader who knows it as 截取文本 finds it by that — and the way it is written as well, so a
    /// piece of the call works too. Nothing typed and no group chosen is the whole table, which is
    /// what the dialog opens with.
    /// </summary>
    private void Refill()
    {
        var group = SelectedFunctionFilter?.Group ?? string.Empty;
        var wanted = FunctionFilter.Trim();
        Functions.Clear();
        foreach (var function in _table.Where(function =>
            (group.Length == 0 || function.Group == group)
            && (wanted.Length == 0
                || function.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || function.Signature.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || function.Heading.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || function.ChineseSignature.Contains(wanted, StringComparison.OrdinalIgnoreCase))))
        {
            Functions.Add(function);
        }

        OnPropertyChanged(nameof(HasNoFunctionMatch));
    }
}

/// <summary>What the line under the tree is saying, which decides how it is shown.</summary>
public enum TreeMessageKind
{
    /// <summary>Nothing to say.</summary>
    None,

    /// <summary>A remark about what is still to be filled in, or about what was not written yet.</summary>
    Note,

    /// <summary>Something that stops the formula being handed over.</summary>
    Problem,

    /// <summary>What the formula comes to, worth showing in green.</summary>
    Ok,
}

/// <summary>One operator the structure page offers: what the button says and what it does to the picked piece.</summary>
public sealed class TreeOperator(
    string token, string operation, TreeOperatorKind kind, string hintKey)
{
    /// <summary>The short sign shown on the button, such as <c>+</c> or <c>and</c>.</summary>
    public string Token { get; } = token;

    /// <summary>How the operator is written in the formula itself.</summary>
    public string Operation { get; } = operation;

    /// <summary>Whether it takes one operand, two, or turns the picked piece into a list.</summary>
    public TreeOperatorKind Kind { get; } = kind;

    /// <summary>One plain line saying what pressing it does.</summary>
    public string Hint { get; } = Strings.Get(hintKey);
}

/// <summary>What pressing an operator on the structure page does.</summary>
public enum TreeOperatorKind
{
    /// <summary>Wraps the picked piece and leaves a place to fill beside it.</summary>
    Binary,

    /// <summary>Stands in front of the picked piece.</summary>
    Unary,

    /// <summary>Makes the picked piece the first item of a list.</summary>
    List,
}

/// <summary>What kind of a thing a plain value is, which is how a place gets filled with one.</summary>
public enum ValueChoiceKind
{
    /// <summary>A number, typed in the place itself.</summary>
    Number,

    /// <summary>A piece of text, typed in the place itself.</summary>
    Text,

    /// <summary>One of the words that stand for a value on its own.</summary>
    Constant,
}

/// <summary>One plain value the structure page offers beside the variables.</summary>
public sealed class ValueChoice(ValueChoiceKind kind, string labelKey, string hintKey, string word = "")
{
    /// <summary>How the value is read once it is in a place.</summary>
    public ValueChoiceKind Kind { get; } = kind;

    /// <summary>What the button says.</summary>
    public string Label { get; } = Strings.Get(labelKey);

    /// <summary>One plain line saying what pressing it does.</summary>
    public string Hint { get; } = Strings.Get(hintKey);

    /// <summary>The word itself, for the ones that are written rather than typed.</summary>
    public string Word { get; } = word;
}

/// <summary>
/// One button of the dock above the function wall: the group it shows, and what the button says.
/// An empty group is all of them, which is what the dock stands on when the dialog opens.
/// </summary>
public sealed class ExpressionFunctionFilter(string group, string labelKey)
{
    /// <summary>The key every function of this group shares, such as <c>text</c>, or empty for all.</summary>
    public string Group { get; } = group;

    /// <summary>What the button says, in the reader's own language.</summary>
    public string Label { get; } = Strings.Get(labelKey);
}

/// <summary>One argument of a function, named and explained the way the bubble shows it.</summary>
public sealed record FunctionArgument(string Label, string Hint);

/// <summary>
/// One function card: what it is called and the text one click puts in the formula. The card says
/// the name in the reader's own language with the name the code calls it by beside it — a reader
/// who has met either one recognises the other — and is painted in the colour of the group it
/// belongs to, which is what tells the groups apart now that no heading does. How the call is
/// written and what each argument is for are in the bubble that comes up when the pointer rests on
/// the card: written on the card itself they would be a paragraph per function on a wall of them.
/// </summary>
public sealed class ExpressionFunctionChoice
{
    public ExpressionFunctionChoice(ExpressionFunction function)
    {
        Function = function;
        Name = function.Name;
        Signature = function.Signature;
        Insert = function.Name + "(";
        Heading = ExpressionText.Heading(function);
        ChineseSignature = ExpressionText.ChineseSignature(function);
        // How the call is written, in whichever language the reader is reading: the Chinese form is
        // a reading of the call rather than a copy of it, so an English interface keeps the one the
        // code itself is written with.
        WrittenAs = ExpressionText.InChinese ? ChineseSignature : Signature;
        Arguments =
        [
            .. function.Parameters.Select(parameter => new FunctionArgument(
                ExpressionText.ParameterLabel(function.Name, parameter),
                ExpressionText.ParameterSaid(function.Name, parameter))),
        ];
        // The Chinese line lives in the text table and the English one beside the function in the
        // engine, the way the built-in variables are written: one sentence per language, no copies.
        Hint = Strings.Get($"Expr.Fn.{function.Name}", function.Description);
    }

    /// <summary>The function itself, which is what the structure page builds its lines from.</summary>
    public ExpressionFunction Function { get; }

    /// <summary>The name the function is called by.</summary>
    public string Name { get; }

    /// <summary>Which group of the table it belongs to, which is the colour its card is painted in.</summary>
    public string Group => Function.Group;

    /// <summary>How it is written, with the arguments it takes.</summary>
    public string Signature { get; }

    /// <summary>What the button says: the name in the reader's own language, the code name beside it.</summary>
    public string Heading { get; }

    /// <summary>The same call, with every argument named in the reader's own language.</summary>
    public string ChineseSignature { get; }

    /// <summary>How the call is written, said in the reader's own language.</summary>
    public string WrittenAs { get; }

    /// <summary>
    /// What each argument is for, one line each, in the order the call takes them: this is what the
    /// bubble under the pointer is for, and what a card alone cannot say.
    /// </summary>
    public IReadOnlyList<FunctionArgument> Arguments { get; }

    /// <summary>What one click leaves in the expression, with the caret after the bracket.</summary>
    public string Insert { get; }

    /// <summary>One line saying what the function does, shown when the pointer rests on it.</summary>
    public string Hint { get; }
}

/// <summary>One operator button on the expression builder: what it looks like, what it inserts, and what it means.</summary>
public sealed class ExpressionOperator
{
    public ExpressionOperator(string token, string insert, string hintKey)
    {
        Token = token;
        Insert = insert;
        Hint = Strings.Get(hintKey);
    }

    /// <summary>The short sign shown on the button, such as <c>+</c> or <c>and</c>.</summary>
    public string Token { get; }

    /// <summary>The text dropped into the expression, spaced so it reads after insertion.</summary>
    public string Insert { get; }

    /// <summary>One plain line saying what the operator does.</summary>
    public string Hint { get; }
}
