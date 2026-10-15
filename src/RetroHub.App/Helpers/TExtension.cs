using System.Windows.Markup;
using RetroHub.Core.Localization;

namespace RetroHub.App.Helpers;

/// <summary>Translated text in XAML: <c>Text="{l:T 'Install selected'}"</c> (English is the key; see <see cref="L"/>).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; set; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
