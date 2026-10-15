using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RetroHub.Mac.Helpers;

/// <summary>Generates a translucent white-noise tile for the background "grain".</summary>
public static class NoiseBrush
{
    public static Bitmap CreateTile(int maxAlpha, int size = 128, int seed = 7)
    {
        var random = new Random(seed);
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // Premultiplied BGRA: white with random alpha.
            var a = (byte)random.Next(0, maxAlpha + 1);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = a;
            pixels[i + 3] = a;
        }

        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < size; row++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, row * size * 4, buffer.Address + row * buffer.RowBytes, size * 4);
        }
        return bitmap;
    }
}

/// <summary>
/// Grain over the whole window: the noise tile drawn edge to edge. (WPF uses a tiled ImageBrush; Avalonia's tiling
/// leaves visible seams on Retina screens, so the tiles are drawn one by one.)
/// </summary>
public sealed class GrainLayer : Control
{
    public static readonly StyledProperty<int> MaxAlphaProperty = AvaloniaProperty.Register<GrainLayer, int>(nameof(MaxAlpha), 9);

    private Bitmap? _tile;

    static GrainLayer() => AffectsRender<GrainLayer>(MaxAlphaProperty);

    public GrainLayer() => IsHitTestVisible = false;

    public int MaxAlpha
    {
        get => GetValue(MaxAlphaProperty);
        set => SetValue(MaxAlphaProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MaxAlphaProperty)
            _tile = null;
    }

    public override void Render(DrawingContext context)
    {
        _tile ??= NoiseBrush.CreateTile(MaxAlpha);
        var size = _tile.Size;
        for (var y = 0.0; y < Bounds.Height; y += size.Height)
            for (var x = 0.0; x < Bounds.Width; x += size.Width)
                context.DrawImage(_tile, new Rect(x, y, size.Width, size.Height));
    }
}
