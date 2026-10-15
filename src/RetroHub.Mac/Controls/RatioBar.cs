using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.Mac.Controls;

/// <summary>
/// Horizontal bar that fills <see cref="Ratio"/> (0–1) of its width, at least 3 px when above zero.
/// (WPF splits a Grid into two star columns; Avalonia's ColumnDefinitions cannot bind to the item.)
/// </summary>
public sealed class RatioBar : Control
{
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<RatioBar, double>(nameof(Ratio));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<RatioBar, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = AvaloniaProperty.Register<RatioBar, CornerRadius>(nameof(CornerRadius));

    static RatioBar() => AffectsRender<RatioBar>(RatioProperty, FillProperty, CornerRadiusProperty);

    public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public override void Render(DrawingContext context)
    {
        var ratio = Math.Clamp(Ratio, 0, 1);
        if (ratio <= 0 || Fill is null)
            return;
        var width = Math.Max(3, ratio * Bounds.Width);
        context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, 0, width, Bounds.Height), CornerRadius));
    }
}
