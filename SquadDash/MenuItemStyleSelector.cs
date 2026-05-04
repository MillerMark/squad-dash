using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class MenuItemStyleSelector : StyleSelector {
    public Style? MenuItemStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => container is Separator ? null : MenuItemStyle;
}
