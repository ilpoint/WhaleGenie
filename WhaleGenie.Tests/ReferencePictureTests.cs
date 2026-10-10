using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A search may list more than one reference picture and goes with the first that turns up, so the
/// dialog has to build that list, hand it to the engine, and open what was written back into rows.
/// </summary>
public class ReferencePictureTests
{
    private static AddActionViewModel Open(string key)
    {
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("shot"), []);
        viewModel.SelectAction(key);
        return viewModel;
    }

    private static StepParameterViewModel Pictures(AddActionViewModel viewModel)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == "image");

    [Fact]
    public void What_the_dialog_listed_reaches_the_engine()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var pictures = Pictures(viewModel);

            pictures.AddPicture(new ImageRowViewModel { Text = "ok.png" });
            pictures.AddPicture(new ImageRowViewModel { Text = "$shot" });

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var rows = Assert.Single(new[] { saved! }.ToExecutable()).Rows("image");
            Assert.Equal(2, rows.Count);
            Assert.Equal("ok.png", rows[0]["image"]);
            Assert.Equal("$shot", rows[1]["image"]);
        });
    }

    [Fact]
    public void The_macro_file_hands_the_pictures_back_as_rows()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var pictures = Pictures(viewModel);
            pictures.AddPicture(new ImageRowViewModel { Text = "ok.png" });
            pictures.AddPicture(new ImageRowViewModel { Text = "also-ok.png" });

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var written = saved!.ToJson().ToJsonString();
            var back = MacroStep.FromJson((JsonObject)JsonNode.Parse(written)!);
            var rows = back.Parameters.First(parameter => parameter.Name == "image").Rows;

            Assert.Equal(2, rows.Count);
            Assert.Equal("ok.png", rows[0].Text("image"));
            Assert.Equal("also-ok.png", rows[1].Text("image"));
        });
    }

    /// <summary>
    /// A search with an empty list has nothing to look for, so it is not a step that can be saved:
    /// one picture is what it takes.
    /// </summary>
    [Fact]
    public void A_search_is_saved_once_it_names_one_picture()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var pictures = Pictures(viewModel);

            Assert.False(viewModel.CanSave, viewModel.ValidationMessage);

            pictures.AddPicture();
            Assert.False(viewModel.CanSave, viewModel.ValidationMessage);

            pictures.Pictures[0].Text = "ok.png";
            Assert.True(viewModel.CanSave, viewModel.ValidationMessage);
        });
    }

    /// <summary>
    /// The pictures are tried in the order they are listed, so the list can be rearranged — and the
    /// order that was chosen is the order the step is saved with.
    /// </summary>
    [Fact]
    public void A_picture_can_be_moved_up_the_list_and_stays_there()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var pictures = Pictures(viewModel);
            pictures.AddPicture(new ImageRowViewModel { Text = "first.png" });
            pictures.AddPicture(new ImageRowViewModel { Text = "second.png" });
            pictures.AddPicture(new ImageRowViewModel { Text = "third.png" });

            // What the buttons lead to: the last picture is tried before the other two.
            var last = pictures.Pictures[^1];
            pictures.MovePicture(last, -1);
            pictures.MovePicture(last, -1);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var rows = Assert.Single(new[] { saved! }.ToExecutable()).Rows("image");
            Assert.Equal(["third.png", "first.png", "second.png"],
                rows.Select(row => row["image"]));
        });
    }

    /// <summary>
    /// The two ends of the list have nowhere to move to, and say so rather than doing nothing when
    /// the button is pressed.
    /// </summary>
    [Fact]
    public void The_ends_of_the_list_say_they_cannot_move_further()
    {
        Ui.Run(() =>
        {
            var pictures = Pictures(Open("vision.findImage"));
            pictures.AddPicture(new ImageRowViewModel { Text = "one.png" });
            pictures.AddPicture(new ImageRowViewModel { Text = "two.png" });

            Assert.False(pictures.Pictures[0].CanMoveUp);
            Assert.True(pictures.Pictures[0].CanMoveDown);
            Assert.True(pictures.Pictures[1].CanMoveUp);
            Assert.False(pictures.Pictures[1].CanMoveDown);

            // And a row that is removed leaves the one beside it as the new end.
            pictures.RemovePicture(pictures.Pictures[0]);
            Assert.False(pictures.Pictures[0].CanMoveUp);
            Assert.False(pictures.Pictures[0].CanMoveDown);
        });
    }

    [Fact]
    public void The_button_on_a_row_moves_that_row()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("shot"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findImage");
            Dispatcher.UIThread.RunJobs();

            var pictures = Pictures(viewModel);
            pictures.AddPicture(new ImageRowViewModel { Text = "first.png" });
            pictures.AddPicture(new ImageRowViewModel { Text = "second.png" });
            Dispatcher.UIThread.RunJobs();

            var second = pictures.Pictures[1];
            var up = FindButton(window, second, Strings.Get("Add.ImageMoveUp"));
            up.Command!.Execute(up.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["second.png", "first.png"],
                pictures.Pictures.Select(picture => picture.Text));
        });
    }

    [Fact]
    public void The_dialog_adds_and_removes_a_picture()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("shot"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findImage");
            Dispatcher.UIThread.RunJobs();

            var pictures = Pictures(viewModel);
            FindButton(window, pictures, Strings.Get("Add.ImageAdd"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(pictures.Pictures);

            var remove = FindButton(window, null, Strings.Get("Add.ImageRemove"));
            remove.Command!.Execute(remove.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(pictures.Pictures);
        });
    }

    private static Button FindButton(Window window, object? data, string content)
        => window.GetVisualDescendants().OfType<Button>().Single(candidate =>
            candidate.IsEffectivelyVisible
            && Equals(candidate.Content, content)
            && (data is null || Equals(candidate.DataContext, data)));
}
