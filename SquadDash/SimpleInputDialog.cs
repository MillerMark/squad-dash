using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Minimal themed single-line text input dialog (replacement for VB InputBox).
/// </summary>
internal sealed class SimpleInputDialog : ChromedWindow
{
    private readonly TextBox _inputBox;
    public string? Result { get; private set; }

    private SimpleInputDialog(string prompt, string title, string defaultValue)
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        Title                 = title;
        Width                 = 360;
        SizeToContent         = SizeToContent.Height;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost               = true;

        var contentArea = ApplyOuterBorder("AppSurface", title);

        var promptBlock = new TextBlock
        {
            Text         = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(12, 10, 12, 6),
        };
        promptBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        promptBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        _inputBox = new TextBox
        {
            Text    = defaultValue,
            Height  = 28,
            Margin  = new Thickness(12, 0, 12, 8),
            Padding = new Thickness(6, 4, 6, 4),
        };
        _inputBox.SetResourceReference(TextBox.BackgroundProperty,  "TextBoxBackground");
        _inputBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _inputBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _inputBox.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeBody");
        _inputBox.Loaded += (_, _) => { _inputBox.SelectAll(); _inputBox.Focus(); };

        var okButton = new Button { Content = "OK", Width = 70, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
        okButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        okButton.Click += (_, _) => { Result = _inputBox.Text.Trim(); Close(); };

        var cancelButton = new Button { Content = "Cancel", Width = 70, Height = 26 };
        cancelButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        cancelButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(12, 0, 12, 12),
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var stack = new StackPanel();
        stack.Children.Add(promptBlock);
        stack.Children.Add(_inputBox);
        stack.Children.Add(buttons);
        contentArea.Child = stack;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Result = _inputBox.Text.Trim(); Close(); }
            if (e.Key == Key.Escape) Close();
        };
    }

    /// <summary>
    /// Shows a single-line input dialog and returns the entered text, or null if cancelled.
    /// </summary>
    internal static string? Show(Window owner, string prompt, string title = "Input", string defaultValue = "")
    {
        var dlg = new SimpleInputDialog(prompt, title, defaultValue) { Owner = owner };
        dlg.ShowDialog();
        return string.IsNullOrWhiteSpace(dlg.Result) ? null : dlg.Result;
    }
}
