namespace SquadDash;

using System.Windows;
using System.Windows.Controls;

/// <summary>
/// Shared factory for themed <see cref="ToolTip"/> instances.
/// Uses resource references so tooltips automatically update when the theme changes.
/// </summary>
internal static class ToolTipHelper
{
    internal static ToolTip MakeThemedToolTip(string text)
    {
        var tb = new TextBlock { Text = text };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "ToolTipForeground");
        var tip = new ToolTip
        {
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(6, 4, 6, 4),
            Content         = tb,
        };
        tip.SetResourceReference(Control.BackgroundProperty,   "InputSurface");
        tip.SetResourceReference(Control.BorderBrushProperty,  "InputBorder");
        return tip;
    }
}
