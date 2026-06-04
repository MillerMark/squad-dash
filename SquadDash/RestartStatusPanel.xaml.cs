using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

/// <summary>
/// RestartStatusPanel is a status panel that appears during restart requests when an active turn is running.
/// It displays a message indicating that the restart is waiting for the current turn to complete,
/// and provides a close button to dismiss the notification (while restart still proceeds).
/// </summary>
public partial class RestartStatusPanel : UserControl
{
    public RestartStatusPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the close button click event. Hides the panel but does not cancel the restart.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Visibility = Visibility.Collapsed;
    }
}
