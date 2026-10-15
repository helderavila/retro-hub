using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroHub.App.Helpers;

/// <summary>Gera um ladrilho de ruído branco translúcido para o "grain" do fundo.</summary>
public static class NoiseBrush
{
    public static Brush Create(int maxAlpha, int size = 128, int seed = 7)
    {
        var random = new Random(seed);
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // BGRA pré-multiplicado: branco com alfa aleatório.
            var a = (byte)random.Next(0, maxAlpha + 1);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = a;
            pixels[i + 3] = a;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}
