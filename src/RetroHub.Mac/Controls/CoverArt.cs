using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RetroHub.Mac.Controls;

/// <summary>
/// Uncropped cover in any frame: the whole image shows (N64, GBA, PS2… boxes have different shapes) and the
/// leftover space is filled with the cover itself, blurred and darkened. If the cover's shape is already close to
/// the frame's, it fills everything.
/// </summary>
public sealed class CoverArt : Panel
{
    /// <summary>Aspect-ratio difference up to which the cover is cropped to fill (thin bars would look bad).</summary>
    private const double FillTolerance = 0.06;

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<CoverArt, IImage?>(nameof(Source));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<CoverArt, CornerRadius>(nameof(CornerRadius));

    private readonly Image _backdrop = new() { Stretch = Stretch.UniformToFill };
    private readonly Rectangle _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)) };
    private readonly Image _image = new();
    private readonly Border _clip = new() { ClipToBounds = true };

    static CoverArt()
    {
        AffectsArrange<CoverArt>(SourceProperty);
    }

    public CoverArt()
    {
        // Negative margin: the blur leaves no light edge at the borders (the rounded clip hides the excess).
        _backdrop.Margin = new Thickness(-24);
        _backdrop.Effect = new BlurEffect { Radius = 24 };
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);
        var layers = new Panel();
        layers.Children.Add(_backdrop);
        layers.Children.Add(_shade);
        layers.Children.Add(_image);
        _clip.Child = layers;
        Children.Add(_clip);
        SizeChanged += (_, _) => UpdateStretch();
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _image.Source = Source;
            _backdrop.Source = Source;
            UpdateStretch();
        }
        else if (change.Property == CornerRadiusProperty)
        {
            // Border clips its child to the rounded corners (one radius per corner: card covers round only the top).
            _clip.CornerRadius = CornerRadius;
        }
    }

    private void UpdateStretch()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var size = Source?.Size ?? default;
        var fills = size.Width > 0 && size.Height > 0 && w > 0 && h > 0
                    && Math.Abs(size.Width / size.Height - w / h) / (w / h) <= FillTolerance;
        _image.Stretch = fills ? Stretch.UniformToFill : Stretch.Uniform;
        _backdrop.IsVisible = _shade.IsVisible = !fills && Source is not null;
    }
}
