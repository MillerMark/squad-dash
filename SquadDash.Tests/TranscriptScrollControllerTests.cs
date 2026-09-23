using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptScrollControllerTests
{
    [TestCase(RoutedInputKind.MouseWheel)]
    [TestCase(RoutedInputKind.KeyboardPageUp)]
    [TestCase(RoutedInputKind.ScrollViewerLeftPointer)]
    public void PreserveViewportThroughLayout_RoutedInputBeforeContextIdle_CancelsPendingRestoration(
        RoutedInputKind inputKind)
    {
        WpfTestContext.Run(() =>
        {
            using var host = BuildHostWithTranscript();
            var rtb = host.RichTextBox;
            var sv = host.ScrollViewer;
            var controller = new TranscriptScrollController(rtb, Dispatcher.CurrentDispatcher);

            controller.ScrollToBottom();
            PumpDispatcher();

            controller.PreserveViewportThroughLayout(() => { }, "test");
            RaiseRoutedInput(inputKind, rtb, sv);
            sv.PageUp();
            PumpDispatcherThroughBackground();

            var offsetAfterInputScroll = sv.VerticalOffset;
            Assert.That(offsetAfterInputScroll, Is.LessThan(sv.ScrollableHeight - 1.0),
                "Setup failed: routed input should lead to a non-bottom reading position.");
            PumpDispatcher();

            Assert.That(controller.IsUserScrolledAway, Is.True,
                "Routed scroll input should lock the viewport away from tail-follow mode.");
            Assert.That(sv.VerticalOffset, Is.EqualTo(offsetAfterInputScroll).Within(2.0),
                "Deferred restoration must not override the reading position established by real input.");
        });
    }

    [Test]
    public void PreserveViewportThroughLayout_BottomAnchor_StillRestoresBottom()
    {
        WpfTestContext.Run(() =>
        {
            using var host = BuildHostWithTranscript();
            var rtb = host.RichTextBox;
            var sv = host.ScrollViewer;
            var controller = new TranscriptScrollController(rtb, Dispatcher.CurrentDispatcher);

            controller.ScrollToBottom();
            PumpDispatcher();

            controller.PreserveViewportThroughLayout(() => { sv.ScrollToVerticalOffset(0); }, "bottom restore");
            PumpDispatcher();

            Assert.That(sv.VerticalOffset, Is.EqualTo(sv.ScrollableHeight).Within(2.0),
                "Bottom-anchored preservation should restore to bottom after deferred layout.");
            Assert.That(controller.IsUserScrolledAway, Is.False,
                "Bottom restoration should keep tail-follow enabled.");
        });
    }

    [Test]
    public void PreserveViewportThroughLayout_NestedRequests_StillRestoreCapturedAnchor()
    {
        WpfTestContext.Run(() =>
        {
            using var host = BuildHostWithTranscript();
            var rtb = host.RichTextBox;
            var sv = host.ScrollViewer;
            var controller = new TranscriptScrollController(rtb, Dispatcher.CurrentDispatcher);

            controller.ScrollToBottom();
            PumpDispatcher();

            var anchorOffset = Math.Max(50, sv.ScrollableHeight * 0.35);
            controller.ScrollToOffset(anchorOffset);
            PumpDispatcher();

            var capturedOffset = sv.VerticalOffset;

            controller.PreserveViewportThroughLayout(() =>
            {
                controller.PreserveViewportThroughLayout(() => { sv.ScrollToVerticalOffset(0); }, "inner");
            }, "outer");
            PumpDispatcher();

            Assert.That(sv.VerticalOffset, Is.EqualTo(capturedOffset).Within(2.0),
                "Nested preservation should still restore the outer captured anchor.");
            Assert.That(controller.IsUserScrolledAway, Is.True,
                "Restoring a non-bottom anchor should keep the user-away lock state.");
        });
    }

    [Test]
    public void PreserveViewportThroughLayout_ScrollViewerRightClick_DoesNotCancelPendingRestoration()
    {
        WpfTestContext.Run(() =>
        {
            using var host = BuildHostWithTranscript();
            var rtb = host.RichTextBox;
            var sv = host.ScrollViewer;
            var controller = new TranscriptScrollController(rtb, Dispatcher.CurrentDispatcher);

            controller.ScrollToBottom();
            PumpDispatcher();

            var anchorOffset = Math.Max(50, sv.ScrollableHeight * 0.35);
            controller.ScrollToOffset(anchorOffset);
            PumpDispatcher();
            var capturedOffset = sv.VerticalOffset;

            controller.PreserveViewportThroughLayout(() => { sv.ScrollToVerticalOffset(0); }, "right click");
            RaiseScrollViewerPreviewMouseDown(sv, MouseButton.Right);
            PumpDispatcher();

            Assert.That(sv.VerticalOffset, Is.EqualTo(capturedOffset).Within(2.0),
                "Non-navigation right-click should not cancel viewport restoration.");
            Assert.That(controller.IsUserScrolledAway, Is.True,
                "Restoring a non-bottom anchor must keep the user-away lock.");
        });
    }

    private static HostHandle BuildHostWithTranscript()
    {
        var rtb = new RichTextBox
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Width = 640,
            Height = 320,
            Document = BuildDocument(),
        };

        rtb.ApplyTemplate();
        rtb.Measure(new Size(640, 320));
        rtb.Arrange(new Rect(0, 0, 640, 320));
        rtb.UpdateLayout();
        PumpDispatcher();

        var scrollViewer = rtb.Template?.FindName("PART_ContentHost", rtb) as ScrollViewer
            ?? FindScrollViewer(rtb)
            ?? throw new AssertionException("Failed to resolve transcript ScrollViewer.");

        return new HostHandle(rtb, scrollViewer);
    }

    private static FlowDocument BuildDocument()
    {
        var document = new FlowDocument();
        for (var i = 0; i < 800; i++)
            document.Blocks.Add(new Paragraph(new Run($"Line {i:D4}")));
        return document;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;

            var nested = FindScrollViewer(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static void PumpDispatcher()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void PumpDispatcherThroughBackground()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static void RaiseRoutedInput(RoutedInputKind inputKind, RichTextBox richTextBox, ScrollViewer scrollViewer)
    {
        switch (inputKind)
        {
            case RoutedInputKind.MouseWheel:
                RaisePreviewMouseWheel(richTextBox, delta: -120);
                break;
            case RoutedInputKind.KeyboardPageUp:
                RaisePreviewKeyDown(richTextBox, Key.PageUp);
                break;
            case RoutedInputKind.ScrollViewerLeftPointer:
                RaiseScrollViewerPreviewMouseDown(scrollViewer, MouseButton.Left);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(inputKind), inputKind, "Unknown routed input kind.");
        }
    }

    private static void RaisePreviewMouseWheel(RichTextBox richTextBox, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };
        richTextBox.RaiseEvent(args);
    }

    private static void RaisePreviewKeyDown(RichTextBox richTextBox, Key key)
    {
        var source = PresentationSource.FromVisual(richTextBox) ?? new TestPresentationSource(richTextBox);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        richTextBox.RaiseEvent(args);
    }

    private static void RaiseScrollViewerPreviewMouseDown(ScrollViewer scrollViewer, MouseButton button)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent,
        };
        scrollViewer.RaiseEvent(args);
    }

    public enum RoutedInputKind
    {
        MouseWheel,
        KeyboardPageUp,
        ScrollViewerLeftPointer,
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        private Visual? _rootVisual;

        public TestPresentationSource(Visual rootVisual)
        {
            _rootVisual = rootVisual;
        }

        public override Visual? RootVisual
        {
            get => _rootVisual;
            set => _rootVisual = value;
        }

        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }

    private sealed class HostHandle : IDisposable
    {
        public HostHandle(RichTextBox richTextBox, ScrollViewer scrollViewer)
        {
            RichTextBox = richTextBox;
            ScrollViewer = scrollViewer;
        }

        public RichTextBox RichTextBox { get; }

        public ScrollViewer ScrollViewer { get; }

        public void Dispose()
        {
        }
    }
}
