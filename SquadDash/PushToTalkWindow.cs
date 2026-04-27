using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class PushToTalkWindow : Window
{
    private readonly Border _volumeTrack;
    private readonly Border _hintBorder;
    internal Border VolumeBar { get; }

    internal PushToTalkWindow(Window owner, bool showHint)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6)
        };

        // Volume bar track
        _volumeTrack = new Border
        {
            Width = 6,
            Height = 36,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 7, 0),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        _volumeTrack.SetResourceReference(Border.BackgroundProperty, "TrackSurface");

        VolumeBar = new Border
        {
            Height = 0,
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(3)
        };
        VolumeBar.SetResourceReference(Border.BackgroundProperty, "RecordingLevelFill");
        _volumeTrack.Child = VolumeBar;
        panel.Children.Add(_volumeTrack);

        // Mic icon
        var viewbox = new Viewbox
        {
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };

        var canvas = new Canvas { Width = 90, Height = 90 };

        var micPath1 = new Path
        {
            Data = Geometry.Parse("M 45 60.738 C 34.715 60.738 26.3 52.322 26.3 42.037 V 18.7 C 26.3 8.415 34.715 0 45 0 C 55.285 0 63.7 8.415 63.7 18.7 V 42.037 C 63.7 52.322 55.285 60.738 45 60.738 Z")
        };
        micPath1.SetResourceReference(Shape.FillProperty, "RecordingIconFill");
        canvas.Children.Add(micPath1);

        var micPath2 = new Path
        {
            Data = Geometry.Parse("M 45 70.968 C 28.987 70.968 15.958 57.94 15.958 41.927 C 15.958 40.215 17.346 38.827 19.057 38.827 C 20.769 38.827 22.157 40.215 22.157 41.927 C 22.157 54.522 32.404 64.77 45 64.77 C 57.595 64.77 67.843 54.522 67.843 41.927 C 67.843 40.215 69.23 38.827 70.942 38.827 C 72.654 38.827 74.042 40.215 74.042 41.927 C 74.042 57.94 61.013 70.968 45 70.968 Z")
        };
        micPath2.SetResourceReference(Shape.FillProperty, "RecordingIconFill");
        canvas.Children.Add(micPath2);

        var micPath3 = new Path
        {
            Data = Geometry.Parse("M 45 89.213 C 43.288 89.213 41.901 87.826 41.901 86.114 V 68.655 C 41.901 66.943 43.288 65.556 45 65.556 C 46.712 65.556 48.099 66.943 48.099 68.655 V 86.114 C 48.099 87.826 46.712 89.213 45 89.213 Z")
        };
        micPath3.SetResourceReference(Shape.FillProperty, "RecordingIconFill");
        canvas.Children.Add(micPath3);

        var micPath4 = new Path
        {
            Data = Geometry.Parse("M 55.451 90 H 34.549 C 32.837 90 31.45 88.613 31.45 86.901 C 31.45 85.189 32.837 83.802 34.549 83.802 H 55.451 C 57.163 83.802 58.55 85.189 58.55 86.901 C 58.55 88.613 57.163 90 55.451 90 Z")
        };
        micPath4.SetResourceReference(Shape.FillProperty, "RecordingIconFill");
        canvas.Children.Add(micPath4);

        viewbox.Child = canvas;
        panel.Children.Add(viewbox);

        // Frosted-glass style background — HorizontalAlignment.Left keeps it
        // from stretching to match the width of the hint pill below it.
        var backdrop = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel
        };
        backdrop.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        backdrop.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");

        // Hint pill shown below the recording indicator when auto-send is active
        var hintLine1 = new TextBlock
        {
            Text = "Release Ctrl to send IMMEDIATELY",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        };
        hintLine1.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var hintLine2 = new TextBlock
        {
            Text = "Tap Shift to edit dictation",
            FontSize = 11
        };
        hintLine2.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var hintPanel = new StackPanel
        {
            Margin = new Thickness(10, 6, 10, 6)
        };
        hintPanel.Children.Add(hintLine1);
        hintPanel.Children.Add(hintLine2);

        _hintBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 5, 0, 0),
            Child = hintPanel,
            Visibility = showHint ? Visibility.Visible : Visibility.Collapsed
        };
        _hintBorder.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        _hintBorder.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");

        var outer = new StackPanel { Orientation = Orientation.Vertical };
        outer.Children.Add(backdrop);
        outer.Children.Add(_hintBorder);

        Content = outer;
    }

    internal void MarkShiftSuppressed() => _hintBorder.Visibility = Visibility.Collapsed;

    internal void PositionUnderCaret(System.Windows.Point caretScreenPoint)
    {
        Left = caretScreenPoint.X - 6;
        Top = caretScreenPoint.Y + 4;
    }
}
