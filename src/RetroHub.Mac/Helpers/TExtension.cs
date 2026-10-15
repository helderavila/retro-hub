using RetroHub.Core.Localization;

namespace RetroHub.Mac.Helpers;

/// <summary>Translated text in XAML: <c>Text="{l:T 'Install selected'}"</c> (English is the key; see <see cref="L"/>).</summary>
public sealed class TExtension(string text)
{
    public string Text { get; set; } = text;

    public string ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
