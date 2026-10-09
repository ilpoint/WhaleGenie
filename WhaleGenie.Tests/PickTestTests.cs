using System;
using System.Collections.Generic;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The "test" button next to a picker. Picking leaves one question open — "did that take the thing
/// I meant?" — and the button answers it by putting a frame around what the selector finds, without
/// the macro having to be run to find out.
/// </summary>
public class PickTestTests
{
    [Fact]
    public void The_control_a_selector_names_is_looked_up_where_it_sits()
    {
        var found = ElementFlashWindow.Locate(
            new ElementDevice(found: true), "Button[automationId='saveButton']", "Notepad");

        Assert.NotNull(found);
        Assert.Equal(new ScreenPoint(1000, 500), found!.Location);
        Assert.Equal(new ScreenSize(80, 24), found.Size);
    }

    [Fact]
    public void A_selector_that_is_not_on_screen_answers_with_nothing_to_flash()
    {
        Assert.Null(ElementFlashWindow.Locate(
            new ElementDevice(found: false), "Button[name='Save']", string.Empty));
    }

    [Fact]
    public void Nothing_is_looked_up_before_a_selector_is_written()
    {
        // A field nobody has filled in is not a selector that matches everything: it is nothing to
        // look for, and the dialog says so rather than flashing something unrelated.
        Assert.Null(ElementFlashWindow.Locate(new ElementDevice(found: true), "   ", string.Empty));
    }

    [Fact]
    public void Both_pickers_carry_a_test_button()
    {
        var markup = File.ReadAllText(Path.Combine(
            Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

        // One next to the control picker and one next to the page picker: the two select the same
        // kind of thing by different means, and both leave the same question open.
        Assert.Contains("Click=\"OnTestElement\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnTestBrowserElement\"", markup, StringComparison.Ordinal);
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }

    /// <summary>
    /// A desktop that holds one control when it is told to and nothing when it is not, which is the
    /// two answers a check can get.
    /// </summary>
    private sealed class ElementDevice(bool found) : IUiDevice
    {
        private static readonly UiElementInfo Save = new("Save", "saveButton", "Button",
            "ButtonClass", new ScreenPoint(1000, 500), new ScreenSize(80, 24), "Notepad");

        public bool Exists(UiQuery query, int timeoutMs) => found;

        public bool Click(UiQuery query, string button) => found;

        public bool FocusWindow(string title) => found;

        public string? GetText(UiQuery query) => found ? Save.Name : null;

        public bool SetText(UiQuery query, string text, bool clearFirst) => found;

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit) => found ? [Save] : [];

        public bool Select(UiQuery query, string text, int itemIndex) => found;

        public bool SetChecked(UiQuery query, bool? state) => found;

        public bool SetExpanded(UiQuery query, string action) => found;

        public bool ScrollIntoView(UiQuery query) => found;

        public UiTable ReadTable(UiQuery query, int limit) => UiTable.Empty;
    }
}
