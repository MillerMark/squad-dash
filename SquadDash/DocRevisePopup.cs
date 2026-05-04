using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

internal sealed class DocRevisePopup : Window
{
    private readonly string _selectedText;
    private readonly string _documentText;
    private readonly string _documentPath;
    private readonly Func<string, string, string, string, CancellationToken, Task<string>> _reviseCallback;

    private readonly TextBox _instructionBox;
    private readonly TextBlock _progressLabel;
    private readonly TextBlock _errorLabel;
    private CancellationTokenSource? _cts;

    private const string PlaceholderText = "Describe revisions (Enter to apply, Esc to cancel)";

    public string? RevisedText { get; private set; }

    internal DocRevisePopup(
        string selectedText,
        string documentText,
        string documentPath,
        Func<string, string, string, string, CancellationToken, Task<string>> reviseCallback)
    {
        _selectedText   = selectedText;
        _documentText   = documentText;
        _documentPath   = documentPath;
        _reviseCallback = reviseCallback;

        WindowStyle        = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode         = ResizeMode.NoResize;
        Width              = 340;
        Height             = 130;
        ShowInTaskbar      = false;

        this.SetResourceReference(BackgroundProperty, "CardSurface");

        var outerBorder = new Border {
            CornerRadius    = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12)
        };
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        outerBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");

        var panel = new StackPanel { Orientation = Orientation.Vertical };

        var titleLabel = new TextBlock {
            Text       = "✏  Revise with AI",
            FontWeight = FontWeights.SemiBold,
            FontSize   = 12,
            Margin     = new Thickness(0, 0, 0, 8)
        };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        _instructionBox = new TextBox {
            AcceptsReturn            = false,
            AcceptsTab               = false,
            TextWrapping             = TextWrapping.Wrap,
            Height                   = 36,
            FontSize                 = 12,
            Padding                  = new Thickness(6, 4, 6, 4),
            BorderThickness          = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            Text                     = PlaceholderText
        };
        _instructionBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _instructionBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _instructionBox.SetResourceReference(TextBox.ForegroundProperty, "SubtleText");

        _instructionBox.GotFocus += (_, _) => {
            if (_instructionBox.Text == PlaceholderText) {
                _instructionBox.Text = "";
                _instructionBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
            }
        };
        _instructionBox.LostFocus += (_, _) => {
            if (string.IsNullOrWhiteSpace(_instructionBox.Text)) {
                _instructionBox.Text = PlaceholderText;
                _instructionBox.SetResourceReference(TextBox.ForegroundProperty, "SubtleText");
            }
        };

        _instructionBox.PreviewKeyDown += InstructionBox_PreviewKeyDown;

        _progressLabel = new TextBlock {
            Text       = "⟳  Working…",
            FontSize   = 12,
            Visibility = Visibility.Collapsed,
            Margin     = new Thickness(0, 8, 0, 0)
        };
        _progressLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        _errorLabel = new TextBlock {
            FontSize     = 11,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33)),
            TextWrapping = TextWrapping.Wrap,
            Visibility   = Visibility.Collapsed,
            Margin       = new Thickness(0, 4, 0, 0)
        };

        panel.Children.Add(titleLabel);
        panel.Children.Add(_instructionBox);
        panel.Children.Add(_progressLabel);
        panel.Children.Add(_errorLabel);

        outerBorder.Child = panel;
        Content = outerBorder;

        Loaded += (_, _) => {
            _instructionBox.SelectAll();
            _instructionBox.Focus();
            Keyboard.Focus(_instructionBox);
        };

        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) {
                _cts?.Cancel();
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    private async void InstructionBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var instructions = _instructionBox.Text.Trim();
        if (string.IsNullOrEmpty(instructions) || instructions == PlaceholderText)
            return;

        _instructionBox.IsEnabled = false;
        _progressLabel.Visibility = Visibility.Visible;
        _errorLabel.Visibility    = Visibility.Collapsed;
        IsEnabled = false;

        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            var cwd = string.IsNullOrEmpty(_documentPath)
                ? ""
                : System.IO.Path.GetDirectoryName(_documentPath) ?? "";

            RevisedText = await _reviseCallback(
                instructions, _selectedText, _documentText, cwd, _cts.Token);

            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            _errorLabel.Text          = "Request timed out or was cancelled.";
            _errorLabel.Visibility    = Visibility.Visible;
            _progressLabel.Visibility = Visibility.Collapsed;
            _instructionBox.IsEnabled = true;
            IsEnabled = true;
        }
        catch (Exception ex)
        {
            _errorLabel.Text          = $"Error: {ex.Message}";
            _errorLabel.Visibility    = Visibility.Visible;
            _progressLabel.Visibility = Visibility.Collapsed;
            _instructionBox.IsEnabled = true;
            IsEnabled = true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
