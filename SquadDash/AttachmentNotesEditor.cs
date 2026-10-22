using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash;

internal static class AttachmentNotesEditor
{
    internal static DockPanel? Create(FollowUpAttachment attachment, Action<string> notesChanged)
    {
        if (!attachment.SupportsNotes)
            return null;

        var editor = new TextBox
        {
            Text = attachment.Notes ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            // Native line constraints are remeasured by WPF when FontSize/FontFamily changes.
            MinLines = 1,
            MaxLines = 8,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(4, 2, 4, 2),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.IBeam,
        };
        editor.SetResourceReference(Control.FontSizeProperty, "FontSizeBody");
        editor.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        editor.SetResourceReference(Control.ForegroundProperty, "ImportantText");
        editor.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        AutomationProperties.SetName(editor, $"Notes for {attachment.Description}");
        editor.TextChanged += (_, _) => notesChanged(editor.Text);

        var label = new Label
        {
            Content = "Notes",
            Target = editor,
            Padding = new Thickness(0, 2, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        label.SetResourceReference(Control.FontSizeProperty, "FontSizeSmall");
        label.SetResourceReference(Control.ForegroundProperty, "LabelText");
        DockPanel.SetDock(label, Dock.Left);
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Cursor = Cursors.Arrow };
        panel.Children.Add(label);
        panel.Children.Add(editor);
        return panel;
    }
}
