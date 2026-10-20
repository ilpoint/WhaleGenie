using Avalonia;
using Avalonia.Media;
using Lucide.Avalonia;

namespace WhaleGenie.Views;

/// <summary>
/// The interface's icon control. Existing screens bind semantic icons through <see cref="Data"/>,
/// while the control owns the mapping to Lucide's renderer and stroke properties.
/// </summary>
public sealed class IconView : LucideIcon
{
    public static readonly StyledProperty<LucideIconKind?> DataProperty =
        AvaloniaProperty.Register<IconView, LucideIconKind?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<IconView, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(StrokeThickness), 1.5);

    /// <summary>Kept for existing layouts; Lucide already draws on its own fixed canvas.</summary>
    public static readonly StyledProperty<bool> FitToBoundsProperty =
        AvaloniaProperty.Register<IconView, bool>(nameof(FitToBounds));

    public IconView()
    {
        Focusable = false;
    }

    public LucideIconKind? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool FitToBounds
    {
        get => GetValue(FitToBoundsProperty);
        set => SetValue(FitToBoundsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty)
        {
            Kind = Data;
        }
        else if (change.Property == StrokeProperty)
        {
            Foreground = Stroke;
        }
        else if (change.Property == StrokeThicknessProperty)
        {
            StrokeWidth = StrokeThickness;
        }
    }
}
