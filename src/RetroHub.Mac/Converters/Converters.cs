using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RetroHub.Mac.Converters;

/// <summary>"ok" | "warn" | "error" | "busy" | "idle" → status brush defined in the tokens.</summary>
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
        return Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush) && brush is IBrush b
            ? b
            : Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>WPF's BoolToVisibility for IsVisible: true → visible (Invert: true → hidden).</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert;
}

/// <summary>WPF's NullToVisibility for IsVisible: null/empty → hidden (Invert: visible only when null/empty).</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null or "") ^ !Invert;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Ratio 0–1 → star column width. With the "rest" parameter, returns what is left (1 − ratio).</summary>
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

/// <summary>Binds a RadioButton to a string: checked when value == parameter; checking it returns the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}

/// <summary>Tag (object) → Geometry, for templates that draw an icon passed in Tag.</summary>
public sealed class GeometryCast : IValueConverter
{
    public static readonly GeometryCast Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value as Geometry;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
