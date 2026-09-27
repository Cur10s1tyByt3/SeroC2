using System;
using System.Windows.Markup;

namespace SeroServer.UI;

/// <summary>
/// XAML markup extension that produces a ContentTextBrush-tinted icon,
/// identical to the opacity-mask technique used by FeatureContextMenu.MakeIcon.
/// Usage:  Icon="{ui:TintedIcon 'SvgImages/...'}"
/// </summary>
[MarkupExtensionReturnType(typeof(System.Windows.FrameworkElement))]
public sealed class TintedIconExtension : MarkupExtension
{
    [ConstructorArgument("path")]
    public string Path { get; set; } = string.Empty;

    public TintedIconExtension() { }
    public TintedIconExtension(string path) { Path = path; }

    public override object ProvideValue(IServiceProvider serviceProvider)
        => FeatureContextMenu.MakeIcon(Path);
}
