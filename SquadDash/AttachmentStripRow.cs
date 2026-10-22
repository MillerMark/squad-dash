using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

internal static class AttachmentStripRow
{
    internal static void Populate(DockPanel row, TextBlock label, FollowUpAttachment attachment, Action<bool> openViewer)
    {
        row.Background = Brushes.Transparent;
        row.Cursor = Cursors.Hand;
        row.Focusable = true;
        AutomationProperties.SetName(row, $"View attachment: {attachment.Description}");
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            openViewer(false);
        };
        row.KeyDown += (_, e) =>
        {
            if (row.IsKeyboardFocused && e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                openViewer(false);
            }
        };

        if (attachment.SupportsNotes)
        {
            var hasNotes = !string.IsNullOrEmpty(attachment.Notes);
            var bubble = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 2,1 L 14,1 Q 16,1 16,3 L 16,10 Q 16,12 14,12 L 7,12 L 3,16 L 3,12 L 2,12 Q 0,12 0,10 L 0,3 Q 0,1 2,1 Z"),
                Stretch = Stretch.Uniform,
                StrokeThickness = 1,
            };
            bubble.SetResourceReference(FrameworkElement.WidthProperty, "FontSizeBody");
            bubble.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
            bubble.SetResourceReference(Shape.StrokeProperty, hasNotes ? "ImportantText" : "SubtleText");
            if (hasNotes)
                bubble.SetResourceReference(Shape.FillProperty, "ImportantText");
            var notesButton = new Button
            {
                Content = bubble,
                Padding = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = hasNotes ? "Edit attachment notes" : "Add attachment notes",
            };
            notesButton.SetResourceReference(FrameworkElement.StyleProperty, "ThemedIconButtonStyle");
            AutomationProperties.SetName(notesButton, $"{notesButton.ToolTip}: {attachment.Description}");
            notesButton.Click += (_, e) =>
            {
                e.Handled = true;
                openViewer(true);
            };
            DockPanel.SetDock(notesButton, Dock.Right);
            row.Children.Add(notesButton);
        }

        row.Children.Add(label);
    }
}
