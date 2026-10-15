using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RetroHub.App.Controls;

/// <summary>
/// Capa sem corte em qualquer moldura: a imagem aparece inteira (caixas de N64, GBA, PS2… têm formatos diferentes)
/// e o espaço que sobra é preenchido pela própria capa desfocada e escurecida. Se o formato da capa já é quase o da
/// moldura, ela preenche tudo.
/// </summary>
public sealed class CoverArt : Grid
{
    /// <summary>Diferença de proporção até a qual a capa é recortada para preencher (barras finas ficariam feias).</summary>
    private const double FillTolerance = 0.06;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(CoverArt), new PropertyMetadata(null, (d, _) => ((CoverArt)d).Refresh()));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(CoverArt), new PropertyMetadata(default(CornerRadius), (d, _) => ((CoverArt)d).UpdateClip()));

    private readonly ImageBrush _backdropBrush = new() { Stretch = Stretch.UniformToFill };
    private readonly Rectangle _backdrop;
    private readonly Rectangle _shade;
    private readonly Image _image = new();

    public CoverArt()
    {
        // Margem negativa: o desfoque não deixa borda clara nas pontas (o recorte arredondado esconde o excesso).
        _backdrop = new Rectangle { Fill = _backdropBrush, Margin = new Thickness(-24), Effect = new BlurEffect { Radius = 24, RenderingBias = RenderingBias.Performance } };
        _shade = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)) };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_backdrop);
        Children.Add(_shade);
        Children.Add(_image);
        SizeChanged += (_, _) => { UpdateClip(); UpdateStretch(); };
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private void Refresh()
    {
        _image.Source = Source;
        _backdropBrush.ImageSource = Source;
        UpdateStretch();
    }

    private void UpdateStretch()
    {
        var fills = Source is { Width: > 0, Height: > 0 } s && ActualWidth > 0 && ActualHeight > 0
                    && Math.Abs(s.Width / s.Height - ActualWidth / ActualHeight) / (ActualWidth / ActualHeight) <= FillTolerance;
        _image.Stretch = fills ? Stretch.UniformToFill : Stretch.Uniform;
        _backdrop.Visibility = _shade.Visibility = fills || Source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        Clip = RoundedRect(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius);
    }

    /// <summary>Retângulo com um raio por canto (a capa do card só arredonda os de cima).</summary>
    private static Geometry RoundedRect(Rect r, CornerRadius c)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            void Arc(Point to, double radius)
            {
                if (radius > 0) ctx.ArcTo(to, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                else ctx.LineTo(to, true, false);
            }

            ctx.BeginFigure(new Point(r.Left + c.TopLeft, r.Top), true, true);
            ctx.LineTo(new Point(r.Right - c.TopRight, r.Top), true, false);
            Arc(new Point(r.Right, r.Top + c.TopRight), c.TopRight);
            ctx.LineTo(new Point(r.Right, r.Bottom - c.BottomRight), true, false);
            Arc(new Point(r.Right - c.BottomRight, r.Bottom), c.BottomRight);
            ctx.LineTo(new Point(r.Left + c.BottomLeft, r.Bottom), true, false);
            Arc(new Point(r.Left, r.Bottom - c.BottomLeft), c.BottomLeft);
            ctx.LineTo(new Point(r.Left, r.Top + c.TopLeft), true, false);
            Arc(new Point(r.Left + c.TopLeft, r.Top), c.TopLeft);
        }
        geometry.Freeze();
        return geometry;
    }
}
