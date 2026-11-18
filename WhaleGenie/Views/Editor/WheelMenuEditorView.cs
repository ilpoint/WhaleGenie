using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The wheel action manager inside the settings window. The scene list on the left and the wheel on
/// the right are the two halves of one rule: the global scene holds the defaults, and a program
/// scene overrides only the positions it has filled in.
/// </summary>
public sealed class WheelMenuEditorView : UserControl
{
    private const double Diameter = 340;

    private readonly SettingsViewModel _viewModel;
    private readonly Window _owner;
    private readonly ListBox _sceneList;
    private readonly WheelCanvas _canvas;
    private readonly Canvas _labels;
    private readonly TextBlock _centre;
    private readonly TextBlock _problem;
    private readonly MenuIconPicture _icons = new();
    private WheelSlotRef? _hover;

    public WheelMenuEditorView(SettingsViewModel viewModel, Window owner)
    {
        _viewModel = viewModel;
        _owner = owner;
        IsEnabled = viewModel.HasProject;

        _sceneList = new ListBox
        {
            Width = 196,
            Height = 200,
            Classes = { "List" },
            ItemsSource = viewModel.WheelScenes,
            // Refreshing the list clears it and adds the rows again, and a container that is being
            // recycled is asked to build with nothing in it. Reading the row through the template
            // parameter without this guard crashed the settings window on every added scene.
            ItemTemplate = new FuncDataTemplate<WheelSceneRow>((row, _) =>
                new TextBlock
                {
                    Text = row?.Summary ?? string.Empty,
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }),
        };
        _sceneList.SelectionChanged += (_, _) =>
            viewModel.SelectedWheelScene = _sceneList.SelectedItem as WheelSceneRow;

        _canvas = new WheelCanvas { Width = Diameter, Height = Diameter };
        _labels = new Canvas { Width = Diameter, Height = Diameter, IsHitTestVisible = false };
        _centre = new TextBlock
        {
            Width = Diameter * 0.34,
            FontSize = 11,
            Foreground = ThemeBrush.Of("TextSecondary"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var wheel = new Grid
        {
            Width = Diameter,
            Height = Diameter,
            Children = { _canvas, _labels, _centre },
        };
        wheel.PointerPressed += OnWheelPressed;
        wheel.PointerMoved += OnWheelMoved;
        wheel.PointerExited += (_, _) =>
        {
            _hover = null;
            Redraw();
        };
        _problem = new TextBlock
        {
            Classes = { "Hint" },
            Foreground = ThemeBrush.Of("Warning"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Diameter,
        };

        var sceneButtons = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        ActionButton("Wheel.SceneAdd", OnAddScene),
                        ActionButton("Wheel.SceneEdit", OnEditScene),
                        ActionButton("Wheel.SceneRemove", OnRemoveScene),
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        ActionButton("Wheel.SceneUp", OnMoveUp),
                        ActionButton("Wheel.SceneDown", OnMoveDown),
                    },
                },
            },
        };

        var left = new StackPanel
        {
            Spacing = 4,
            Width = 196,
            Children =
            {
                new TextBlock { Classes = { "FieldLabel" }, Text = Strings.Get("Wheel.Scenes") },
                _sceneList,
                sceneButtons,
            },
        };

        var right = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Classes = { "Hint" },
                    Foreground = ThemeBrush.Of("Warning"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = Diameter,
                    Text = Strings.Get("Wheel.EditorHint"),
                },
                wheel,
                new TextBlock
                {
                    Classes = { "Hint" },
                    MaxWidth = Diameter,
                    TextWrapping = TextWrapping.Wrap,
                    Text = Strings.Get("Wheel.SlotHint"),
                },
                Legend(),
                _problem,
            },
        };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            Children = { left, right },
        };

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.SelectedWheelScene)
                or nameof(SettingsViewModel.WideOuter)
                or nameof(SettingsViewModel.SelectedThirdCapacity)
                or nameof(SettingsViewModel.WheelRevision))
            {
                SyncSelection();
                Redraw();
            }
        };
        DetachedFromVisualTree += (_, _) => _icons.Dispose();
        SyncSelection();
        Redraw();
    }

    private Button ActionButton(string key, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button
        {
            Classes = { "Secondary" },
            Height = 30,
            Content = Strings.Get(key),
        };
        button.Click += click;
        return button;
    }

    private Control Legend()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        panel.Children.Add(LegendItem("Wheel.LegendGlobal", "SurfaceField"));
        panel.Children.Add(LegendItem("Wheel.LegendOverride", "WarningSurface"));
        panel.Children.Add(LegendItem("Wheel.LegendExtended", "SurfaceCard"));
        return panel;
    }

    private static Control LegendItem(string key, string brushKey)
        => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new Border
                {
                    Width = 12,
                    Height = 12,
                    Background = ThemeBrush.Of(brushKey),
                    BorderBrush = ThemeBrush.Of("BorderControl"),
                    BorderThickness = new Thickness(1),
                },
                new TextBlock { Classes = { "Hint" }, Text = Strings.Get(key) },
            },
        };

    private void SyncSelection()
    {
        var wanted = _viewModel.SelectedWheelScene;
        if (wanted is not null && !ReferenceEquals(_sceneList.SelectedItem, wanted))
        {
            _sceneList.SelectedItem = wanted;
        }

        if (_sceneList.SelectedItem is null && _viewModel.WheelScenes.Count > 0)
        {
            _sceneList.SelectedIndex = 0;
        }
    }

    private void Redraw()
    {
        var scene = _viewModel.EditingWheelScene;
        var global = _viewModel.WheelGlobal;
        if (scene is null || global is null)
        {
            _canvas.Draw(Diameter, _viewModel.WideOuter, [], true, 0,
                _viewModel.SelectedThirdCapacity.Value);
            _labels.Children.Clear();
            _centre.Text = string.Empty;
            _problem.Text = string.Empty;
            return;
        }

        var items = new List<WheelPaintSlot>();
        _labels.Children.Clear();
        var third = _viewModel.SelectedThirdCapacity.Value;
        var metrics = WheelGeometry.For(Diameter, _viewModel.WideOuter, third);
        foreach (var ring in new[] { WheelRing.Inner, WheelRing.Outer,
                    WheelRing.Third, WheelRing.Extended })
        {
            var count = WheelShape.CountOf(ring, _viewModel.WideOuter, third);
            for (var index = 0; index < count; index++)
            {
                var reference = new WheelSlotRef(ring, index);
                var own = scene.At(reference);
                var inherited = global.At(reference);
                var ownAction = !own.IsEmpty;
                var inheritedAction = !scene.IsGlobal && !inherited.IsEmpty && !ownAction;
                var name = ownAction ? own.Label
                    : inheritedAction ? inherited.Label
                    : string.Empty;
                var painted = ownAction ? own : inheritedAction ? inherited : null;
                var tone = ring == WheelRing.Extended ? WheelPaintTone.Extended
                    : ownAction ? (scene.IsGlobal
                        ? WheelPaintTone.Global
                        : WheelPaintTone.Overridden)
                    : inheritedAction ? WheelPaintTone.Global
                    : WheelPaintTone.Empty;
                items.Add(new WheelPaintSlot(reference, name, tone, _hover == reference));

                var picture = painted?.Icon is { IsEmpty: false } icon
                    ? _icons.Create(icon, 15, ThemeBrush.Of(
                        inheritedAction ? "TextTertiary" : "TextPrimary") ?? Brushes.Black)
                    : null;
                if (name.Length == 0 && picture is null)
                {
                    continue;
                }

                var narrow = ring == WheelRing.Third
                    || ring == WheelRing.Outer && _viewModel.WideOuter;
                var width = Math.Max(38, Diameter * (narrow ? 0.13 : 0.18));
                var content = new StackPanel
                {
                    Width = width,
                    Spacing = 1,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                if (picture is not null)
                {
                    picture.HorizontalAlignment = HorizontalAlignment.Center;
                    content.Children.Add(picture);
                }

                if (name.Length > 0)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = inheritedAction
                            ? Strings.Format("Wheel.Inherited", name)
                            : name,
                        Width = width,
                        FontSize = 10.5,
                        Foreground = ThemeBrush.Of(
                            inheritedAction ? "TextTertiary" : "TextPrimary"),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                }

                var height = (picture is null ? 0 : 17) + 10.5 * 1.4;
                var (x, y) = WheelGeometry.LabelAt(reference, metrics, Diameter);
                Canvas.SetLeft(content, Diameter / 2 + x - width / 2);
                Canvas.SetTop(content, Diameter / 2 + y - height / 2);
                _labels.Children.Add(content);
            }
        }

        _canvas.Draw(Diameter, _viewModel.WideOuter, items, true, 0, third);
        _centre.Text = scene.Summary;
        _problem.Text = _viewModel.WheelProblem;
    }

    private void OnWheelMoved(object? sender, PointerEventArgs e)
    {
        var next = Hit(e.GetPosition(_canvas));
        if (next == _hover)
        {
            return;
        }

        _hover = next;
        Redraw();
    }

    private async void OnWheelPressed(object? sender, PointerPressedEventArgs e)
    {
        var reference = Hit(e.GetPosition(_canvas));
        if (reference is not { } chosen || _viewModel.EditingWheelScene is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsRightButtonPressed)
        {
            await EditActionAsync(chosen);
            return;
        }

        if (!point.IsLeftButtonPressed)
        {
            return;
        }

        var macros = _viewModel.Project?.Macros.ToList() ?? [];
        if (await MenuMacroPickerWindow.ShowAsync(_owner, macros) is { } macro)
        {
            _viewModel.SetWheelSlot(chosen, macro.Name);
            Redraw();
        }
    }

    private async Task EditActionAsync(WheelSlotRef reference)
    {
        if (_viewModel.WheelSlotForEdit(reference) is not { } slot)
        {
            return;
        }

        var macros = _viewModel.Project?.Macros.ToList() ?? [];
        var edited = await WheelActionWindow.ShowAsync(_owner, slot, macros,
            PositionName(reference), _viewModel.IsWheelSlotInherited(reference),
            _viewModel.Project?.CurrentPath);
        if (edited is not null)
        {
            _viewModel.SetWheelAction(reference, edited);
            Redraw();
        }
    }

    private static string PositionName(WheelSlotRef reference)
    {
        var ring = reference.Ring switch
        {
            WheelRing.Inner => Strings.Get("Wheel.RingInner"),
            WheelRing.Outer => Strings.Get("Wheel.RingOuter"),
            WheelRing.Third => Strings.Get("Wheel.RingThird"),
            _ => Strings.Get("Wheel.RingExtended"),
        };
        return Strings.Format("Wheel.ActionPosition", ring, reference.Index + 1);
    }

    private WheelSlotRef? Hit(Point point) => WheelGeometry.At(
        point.X - Diameter / 2, point.Y - Diameter / 2,
        WheelGeometry.For(Diameter, _viewModel.WideOuter,
            _viewModel.SelectedThirdCapacity.Value));

    private async void OnAddScene(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (await WindowRuleWindow.PickAsync(_owner, SharedDevices.Current.Windows,
                SharedDevices.Current.Hook) is { } rule)
        {
            _viewModel.AddWheelScene(rule);
            Redraw();
        }
    }

    private async void OnEditScene(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel.SelectedWheelScene is not { IsGlobal: false } row)
        {
            return;
        }

        if (await WindowRuleWindow.PickAsync(_owner, SharedDevices.Current.Windows,
                SharedDevices.Current.Hook, row.Scene.Window) is { } rule)
        {
            _viewModel.UpdateWheelScene(row, rule);
            Redraw();
        }
    }

    private void OnRemoveScene(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel.SelectedWheelScene is { IsGlobal: false } row)
        {
            _viewModel.RemoveWheelScene(row);
            Redraw();
        }
    }

    private void OnMoveUp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _viewModel.MoveWheelScene(-1);

    private void OnMoveDown(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _viewModel.MoveWheelScene(1);
}
