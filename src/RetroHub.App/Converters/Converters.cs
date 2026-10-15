using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RetroHub.App.Converters;

/// <summary>"ok" | "warn" | "error" | "busy" | "idle" → brush de status definido nos tokens.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "ok" => "Brush.Status.Ok",
            "warn" => "Brush.Status.Warn",
            "error" => "Brush.Status.Error",
            "busy" => "Brush.Text.Primary",
            _ => "Brush.Text.Tertiary",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>true = visível só quando o valor é nulo/vazio.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null or "") ^ Invert ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Proporção 0–1 → largura de coluna em estrelas. Com parâmetro "rest", devolve o que sobra (1 − proporção).</summary>
public sealed class RatioToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ratio = Math.Clamp(value is double d ? d : 0, 0, 1);
        if (parameter as string == "rest") ratio = 1 - ratio;
        return new GridLength(Math.Max(ratio, 0.0001), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Liga um RadioButton a uma string: marcado quando value == parameter; ao marcar, devolve o parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
