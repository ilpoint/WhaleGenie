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

// What kind of editor this is, and which of the form's aids it offers.
public partial class StepParameterViewModel : ViewModelBase
{
    public bool IsText => Definition.Kind
        is ActionParameterKind.Text or ActionParameterKind.Key or ActionParameterKind.Keys
            or ActionParameterKind.Record or ActionParameterKind.Point or ActionParameterKind.Rect;

    /// <summary>
    /// True when the parameter is written in a plain one-line field with nothing beside it. A
    /// selector is left out of that group because it carries the element picker's button, an
    /// element of a page carries the page picker's button, and a field the variables are offered
    /// in has a list of its own over it.
    /// </summary>
    public bool IsPlainText => IsText && !IsKeyField && !IsSelector && !IsBrowserTarget && !OffersVariables;

    /// <summary>True when this parameter names one key chosen on the keyboard drawn on screen.</summary>
    public bool IsKey => Definition.Kind is ActionParameterKind.Key;

    /// <summary>
    /// True when the parameter is a combination of keys rather than one key. The keyboard drawn on
    /// screen answers the same question either way; what differs is what is done with the key it
    /// hands back, and the view asks this to know which.
    /// </summary>
    public bool IsKeys => Definition.Kind is ActionParameterKind.Keys;

    /// <summary>True when a key is written here, whether on its own or joined to others.</summary>
    public bool IsKeyField => IsKey || IsKeys;

    /// <summary>True while this field is waiting for a key from the editor's capture loop.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyCaptureLabel))]
    private bool _isCapturingKey;

    /// <summary>
    /// The capture button's text: what it is about to take, and what to do once it is waiting.
    /// A single key answers the question on its own, so the button says what to press; a
    /// combination keeps taking keys until it is told to stop, so the button says how to stop.
    /// </summary>
    public string KeyCaptureLabel => IsCapturingKey
        ? Strings.Get(IsKeys ? "Add.StopKeyCapture" : "Add.PressKey")
        : Strings.Get(IsKeys ? "Add.SetCombination" : "Editor.SetKey");

    /// <summary>
    /// Adds a key to what is written, which is how a combination gets built: what is there stays
    /// and the new key is joined to it with a plus, the way the engine reads one. A field with
    /// nothing in it yet takes the key on its own, so a combination never starts with a plus.
    /// </summary>
    public void AddKey(string key)
    {
        var held = Text.Trim();
        SetWritten(Formula.Text(held.Length == 0 ? key : held + "+" + key));
    }

    /// <summary>
    /// Writes a key the way this field holds it: a single key replaces what is there, and a
    /// combination joins the new key to it.
    /// </summary>
    public void WriteKey(string key)
    {
        if (IsKeys)
        {
            AddKey(key);
        }
        else
        {
            SetWritten(Formula.Text(key));
        }
    }

    /// <summary>
    /// True when this choice is a control of the virtual controller, so the answer can be given by
    /// clicking the control on a pad drawn on screen as well as by picking it out of the list. The
    /// action and the parameter are both named because the other choices of those actions — how a
    /// button is held, how far a trigger is pulled — are not controls of anything.
    /// </summary>
    public bool IsGamepadPad => (Definition.OwnerKey, Definition.Name) is
        ("gamepad.button", "button") or ("gamepad.stick", "stick") or ("gamepad.trigger", "trigger");

    /// <summary>
    /// Takes one of the choices this parameter offers, which is how the pads write back what was
    /// clicked. A value this parameter has no choice for is left alone rather than stored.
    /// </summary>
    public void Choose(string value)
    {
        if (Choices.FirstOrDefault(choice => choice.Value == value) is { } picked)
        {
            Option = picked;
        }
    }

    /// <summary>
    /// True when the field is offered the variables it may name. A hint that says "written out or
    /// held in a variable" is only honest if the name can be picked instead of remembered, which
    /// is what this drives. A window is one of these: the text it is recognised by is written like
    /// any other piece of text, and a title worked out by an earlier step goes in as a name. An
    /// element of a page is not, because the box it is written in is drawn by the page picker
    /// itself: a list of names standing over that one would be the same field written twice.
    /// </summary>
    public bool OffersVariables => !IsWindowRule && (IsText || IsWindow)
        && !IsSelector
        && !IsBrowserTarget
        && (Definition.ValueForm is ActionValueForm.Run or ActionValueForm.Value);

    /// <summary>
    /// 这一格的左边要不要摆一个「名字」标记：整格按文本读，插入的名字片会在运行时换成变量的值。
    /// 认算式的那种格不摆：它左边的 ƒ 已经说了这一格怎么读，两个标记一起出现只会让人先猜哪个算数。
    /// </summary>
    public bool ShowsNameMark => OffersVariables && !TakesFormula;

    /// <summary>
    /// True when the names of variables are offered beside the page picker rather than over the
    /// box. Picking and testing take the place the list of names would stand in, and what a name
    /// does here is become one more line for the step to try instead of standing for the whole
    /// value.
    /// </summary>
    public bool OffersPageVariables => IsBrowserTarget
        && (Definition.ValueForm is ActionValueForm.Run or ActionValueForm.Value);

    /// <summary>
    /// True when the picker beside this field may also open the expression editor, which it does for
    /// the fields whose whole value is read as one. Two kinds are left out: a field that matches a
    /// window by text, because that text is filled in with names rather than worked out — and an
    /// element of a page is one of those, since a formula written into a selector would be taken as
    /// part of the selector — and a field that names a variable to write, where a formula would
    /// become the name of the variable itself.
    /// </summary>
    public bool OffersBuilder => (OffersVariables || IsVariable)
        && !OffersFormula
        && !IsWindow && !IsBrowserTarget && !Definition.NamesVariable;

    /// <summary>
    /// True when the whole value is read as one, so the expression editor may build it: what it
    /// makes there is what the engine reads. Text that is only filled in stays out, because a
    /// formula written into it would be taken literally. It holds for a value written in a text
    /// box and for one picked from a variable list alike — an operand of a calculation is the
    /// second kind, and being able to build it is the difference between this and the editor.
    /// </summary>
    public bool OffersFormula => Definition.AcceptsFormula && !IsWindowRule;

    /// <summary>
    /// True when this field has a value the one window can edit, whatever its reading. A long field
    /// is one of them: a note is text, a message is a run and a file body is a value, and all three
    /// are typed in the same window.
    /// </summary>
    public bool OffersValueWindow
        => Definition.ValueForm is ActionValueForm.Text or ActionValueForm.Run
            or ActionValueForm.Value;

    /// <summary>
    /// True when the whole value written here is read as one rather than as text with the names in
    /// it replaced, so an expression may be written in the box. A number counts: the numbered box
    /// and the expression box are two ways of writing the same kind of value, and the engine works out
    /// whichever one is there. This is not the same question as <see cref="OffersFormula"/>, which
    /// asks whether the expression editor is the thing to offer beside this particular editor.
    /// </summary>
    public bool TakesFormula => Definition.Kind is ActionParameterKind.Number
        ? Definition.Unit != ActionUnit.Percent
        : Definition.AcceptsFormula || Definition.Kind is ActionParameterKind.Expression;

    /// <summary>
    /// 这一格要不要摆一个「表达式」按钮。百分比不摆：容差、占比这类是拧出来的量，拧到几就是几，
    /// 摆一个能把人引去写算式的地方，只会让一行里多一个按钮。数字与算式仍然可以手打，
    /// 老宏里存着的算式也照旧算。
    /// </summary>
    public bool OffersExpressionBuilder => Definition.Kind is not ActionParameterKind.Number
        || Definition.Unit != ActionUnit.Percent;

    /// <summary>
    /// True when this parameter is a UI Automation selector, which the element picker can take
    /// off the screen instead of it being written out by hand. The control a step measures from is
    /// one of these as well: it names a control the same way, so it is picked the same way.
    /// </summary>
    public bool IsSelector => Definition.Picker is ActionPickerKind.Element;

    /// <summary>
    /// True when this parameter names an element inside a page a browser action drives. It is
    /// picked off the page itself — by pointing at it in the browser — rather than off the Windows
    /// desktop the way a UI Automation selector is, so it carries a button of its own.
    /// </summary>
    public bool IsBrowserTarget => Definition.Picker is ActionPickerKind.Browser;

    /// <summary>True when this parameter is a picture the action looks for on screen.</summary>
    public bool IsImage => Definition.Kind is ActionParameterKind.Image;

    /// <summary>
    /// The pictures this step looks for, when it looks for more than one: one row each, tried in
    /// the order they are listed.
    /// </summary>
    public ObservableCollection<ImageRowViewModel> Pictures { get; } = [];

    /// <summary>True when this parameter holds several pictures rather than one.</summary>
    public bool IsImageList => Definition.Kind is ActionParameterKind.Images;

    /// <summary>True while the list holds at least one picture.</summary>
    public bool HasPictures => Pictures.Count > 0;

    /// <summary>
    /// The ways of naming the element a pick off the page answered with, steadiest first, so the
    /// second way of finding it can be written down by pointing at it rather than by spelling a
    /// selector out. Empty until a pick has happened, which is what the row of buttons hangs on.
    /// </summary>
    public ObservableCollection<BrowserWayViewModel> Candidates { get; } = [];

    /// <summary>True while a pick off the page has left ways of naming the element to choose from.</summary>
    public bool HasCandidates => Candidates.Count > 0;
}
