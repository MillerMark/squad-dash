namespace SquadDash;
using System.Windows;

internal static class UIErrorHelper
{
    public static void ShowError(string caption, string message) =>
        MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void ShowWarning(string caption, string message) =>
        MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
}
