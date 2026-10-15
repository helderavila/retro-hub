using System.Globalization;
using Avalonia.Data.Converters;
using RetroHub.Core.Localization;

namespace RetroHub.Mac.Converters;

/// <summary>
/// Shows a string in the interface language (<c>L.T(value)</c>) while the bound value stays the original key,
/// e.g. graphics preset names in chips whose SelectedItem must remain the manifest key.
/// </summary>
public sealed class TranslateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && text.Length > 0 ? L.T(text) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
