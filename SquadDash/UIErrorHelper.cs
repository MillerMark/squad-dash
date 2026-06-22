namespace SquadDash;
using System.Windows;

internal static class UIErrorHelper
{
    public static void ShowError(string caption, string message, Window? owner = null)
    {
        if (owner is not null)
            MessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowWarning(string caption, string message, Window? owner = null)
    {
        if (owner is not null)
            MessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
