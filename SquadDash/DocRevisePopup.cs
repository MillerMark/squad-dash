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
    private readonly Action<string> _onRevised;
    private readonly Action<Point>? _onSubmitting;
    private readonly Action<TextBox>? _startPtt;
    private readonly Action? _stopPtt;

    private readonly TextBox _instructionBox;
    private readonly TextBlock _progressLabel;
    private readonly TextBlock _errorLabel;
    private readonly Button _okButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _cts;
    private bool _isSubmitting;

    // PTT double-tap detection at Window level (PreviewKeyDown/PreviewKeyUp)
    private readonly CtrlDoubleTapGestureTracker _pttGesture =
        new CtrlDoubleTapGestureTracker(maxTapHoldMs: 250, doubleTapGapMs: 350);
    private bool _pttActive;

    private const string PlaceholderText = "Describe revisions (Enter to apply, Esc to cancel)";

    internal DocRevisePopup(
        string selectedText,
        string documentText,
        string documentPath,
        Func<string, string, string, string, CancellationToken, Task<string>> reviseCallback,
        Action<string> onRevised,
        Action<Point>? onSubmitting = null,
        Action<TextBox>? startPtt = null,
        Action? stopPtt = null)
    {
        _selectedText   = selectedText;
        _documentText   = documentText;
        _documentPath   = documentPath;
        _reviseCallback = reviseCallback;
        _onRevised      = onRevised;
        _onSubmitting   = onSubmitting;
        _startPtt       = startPtt;
        _stopPtt        = stopPtt;

        WindowStyle        = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode         = ResizeMode.NoResize;
        SizeToContent      = SizeToContent.Height;
        Width              = 460;
        ShowInTaskbar      = false;

        this.SetResourceReference(BackgroundProperty, "CardSurface");

        var outerBorder = new Border {
            CornerRadius    = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(14)
        };
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        outerBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");

        var panel = new StackPanel { Orientation = Orientation.Vertical };

        // Title bar row — drag handle + close button
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        var titleLabel = new TextBlock {
            Text       = "✏  Revise with AI",
            FontWeight = FontWeights.SemiBold,
            FontSize   = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(titleLabel, Dock.Left);
        titleRow.Children.Add(titleLabel);

        // Dragging from title row moves the popup
        titleRow.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        titleLabel.Cursor = Cursors.SizeAll;
        titleRow.Cursor   = Cursors.SizeAll;

        _instructionBox = new TextBox {
            AcceptsReturn            = false,
            AcceptsTab               = false,
            TextWrapping             = TextWrapping.Wrap,
            MinHeight                = 80,
            MaxHeight                = 180,
            FontSize                 = 12,
            Padding                  = new Thickness(6, 6, 6, 6),
            BorderThickness          = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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

        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) {
                _cts?.Cancel();
                Close();
                e.Handled = true;
                return;
            }
            OnPopupPreviewKeyDown(e);
        };
        PreviewKeyUp += (_, e) => OnPopupPreviewKeyUp(e);

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

        // Button row
        _okButton = new Button {
            Content = "Apply",
            MinWidth = 70,
            Height = 28,
            FontSize = 12,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        _okButton.Click += (_, _) => _ = SubmitAsync();

        _cancelButton = new Button {
            Content = "Cancel",
            MinWidth = 70,
            Height = 28,
            FontSize = 12,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true,
        };
        _cancelButton.Click += (_, _) => {
            _cts?.Cancel();
            if (_cts is null)
                Close();
        };

        var buttonRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttonRow.Children.Add(_cancelButton);
        DockPanel.SetDock(_cancelButton, Dock.Left);
        buttonRow.Children.Add(_okButton);
        DockPanel.SetDock(_okButton, Dock.Right);

        panel.Children.Add(titleRow);
        panel.Children.Add(_instructionBox);
        panel.Children.Add(_progressLabel);
        panel.Children.Add(_errorLabel);
        panel.Children.Add(buttonRow);

        outerBorder.Child = panel;
        Content = outerBorder;

        Loaded += (_, _) => {
            _instructionBox.SelectAll();
            _instructionBox.Focus();
            Keyboard.Focus(_instructionBox);
        };
    }

    // ── PTT: double-tap Ctrl detection (Window-level PreviewKeyDown/PreviewKeyUp) ──

    private void OnPopupPreviewKeyDown(KeyEventArgs e)
    {
        if (_startPtt is null) return;
        var action = _pttGesture.HandleKeyDown(e.Key, e.IsRepeat, DateTime.UtcNow);
        if (action != CtrlDoubleTapGestureAction.Triggered) return;
        _pttActive = true;
        _startPtt(_instructionBox);
    }

    private void OnPopupPreviewKeyUp(KeyEventArgs e)
    {
        if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key)) return;
        if (_pttActive) {
            _pttActive = false;
            _stopPtt?.Invoke();
            return;
        }
        _pttGesture.HandleKeyUp(e.Key, DateTime.UtcNow);
    }

    // ── Submission ──────────────────────────────────────────────────────────

    private async void InstructionBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        var instructions = _instructionBox.Text.Trim();
        if (string.IsNullOrEmpty(instructions) || instructions == PlaceholderText)
            return;

        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var capturedToken = _cts.Token;

        if (_onSubmitting is not null)
        {
            // New behavior: close popup immediately, restore focus, show working overlay,
            // then run the AI call in the background.
            _isSubmitting = true;
            _onSubmitting(new Point(Left + Width / 2, Top + ActualHeight / 2));
            Close();
            _ = RunRevisionAsync(instructions, capturedToken);
        }
        else
        {
            // Legacy behavior: show progress inside the popup while waiting.
            _instructionBox.IsEnabled = false;
            _okButton.IsEnabled       = false;
            _progressLabel.Visibility = Visibility.Visible;
            _errorLabel.Visibility    = Visibility.Collapsed;

            try
            {
                var cwd = string.IsNullOrEmpty(_documentPath)
                    ? ""
                    : System.IO.Path.GetDirectoryName(_documentPath) ?? "";

                var revised = await _reviseCallback(
                    instructions, _selectedText, _documentText, cwd, capturedToken);

                if (string.IsNullOrWhiteSpace(revised))
                {
                    ShowError("AI returned an empty response. Try rephrasing your instructions.");
                    return;
                }

                _onRevised(revised);
                Close();
            }
            catch (OperationCanceledException)
            {
                ShowError("Request cancelled.");
            }
            catch (Exception ex)
            {
                ShowError($"Error: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private async Task RunRevisionAsync(string instructions, CancellationToken ct)
    {
        try
        {
            var cwd = string.IsNullOrEmpty(_documentPath)
                ? ""
                : System.IO.Path.GetDirectoryName(_documentPath) ?? "";

            var revised = await _reviseCallback(
                instructions, _selectedText, _documentText, cwd, ct);

            if (!string.IsNullOrWhiteSpace(revised))
                _onRevised(revised);
        }
        catch { /* popup already dismissed — swallow silently */ }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ShowError(string message)
    {
        _errorLabel.Text          = message;
        _errorLabel.Visibility    = Visibility.Visible;
        _progressLabel.Visibility = Visibility.Collapsed;
        _instructionBox.IsEnabled = true;
        _okButton.IsEnabled       = true;
        _instructionBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_isSubmitting)
            _cts?.Cancel();
        base.OnClosed(e);
    }
}
