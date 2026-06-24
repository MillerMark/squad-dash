using System.Windows;
using System.Windows.Controls;
using SquadDash.Hints;

namespace SquadDash;

/// <summary>
/// Options page for the Discoverability (Hints) system, shown in the Preferences window.
/// </summary>
internal partial class HintsOptionsPage : UserControl
{
    public HintsOptionsPage()
    {
        InitializeComponent();
    }

    /// <summary>Populates controls from the provided settings snapshot.</summary>
    public void Initialize(ApplicationSettingsSnapshot settings)
    {
        HintsEnabledCheckBox.IsChecked = settings.HintsEnabled;
        MinGapBox.Text = settings.HintMinGapMinutes.ToString();
    }

    /// <summary>Returns a <see cref="HintSettings"/> built from the current control values.</summary>
    public HintSettings GetCurrentSettings() =>
        new HintSettings
        {
            HintsEnabled  = HintsEnabledCheckBox.IsChecked == true,
            MinGapMinutes = int.TryParse(MinGapBox.Text.Trim(), out int v) && v >= 0 ? v : 10,
        };

    private void HintsEnabledCheckBox_Click(object sender, RoutedEventArgs e) { }

    private void MinGapBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinGapBox.Text.Trim(), out int v) || v < 0)
            MinGapBox.Text = (int.TryParse(MinGapBox.Text.Trim(), out _) ? v : 10).ToString();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset hint history? All hints will be eligible to appear again, as if this is a new install.",
            "Reset Hint History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            HintEngine.Instance.ClearHistory();
    }
}
