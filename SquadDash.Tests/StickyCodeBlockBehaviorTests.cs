using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class StickyCodeBlockBehaviorTests
{
    private static readonly Size ButtonSize = new(26, 22);
    private static readonly Thickness Inset = new(4, 2, 4, 2);

    [Test]
    public void FullyVisibleBlock_AnchorsToBlockTopRight()
    {
        var layout = StickyCodeBlockBehavior.CalculateLayout(
            new Rect(10, 100, 500, 300),
            new Rect(0, 0, 600, 500),
            ButtonSize,
            Inset);

        Assert.Multiple(() =>
        {
            Assert.That(layout.IsVisible, Is.True);
            Assert.That(layout.ButtonBounds, Is.EqualTo(new Rect(480, 102, 26, 22)));
            Assert.That(layout.ClipBounds, Is.EqualTo(new Rect(10, 100, 500, 300)));
        });
    }

    [Test]
    public void BlockTopAboveViewport_PinsToViewportTopRight()
    {
        var layout = StickyCodeBlockBehavior.CalculateLayout(
            new Rect(0, 0, 500, 600),
            new Rect(0, 180, 500, 300),
            ButtonSize,
            Inset);

        Assert.Multiple(() =>
        {
            Assert.That(layout.IsVisible, Is.True);
            Assert.That(layout.ButtonBounds, Is.EqualTo(new Rect(470, 182, 26, 22)));
            Assert.That(layout.ClipBounds, Is.EqualTo(new Rect(0, 180, 500, 300)));
        });
    }

    [Test]
    public void BlockBottomLeavingViewport_ActionProgressivelyClipsUntilBlockIsGone()
    {
        var layout = StickyCodeBlockBehavior.CalculateLayout(
            new Rect(0, 0, 500, 400),
            new Rect(0, 390, 500, 300),
            ButtonSize,
            Inset);

        Assert.Multiple(() =>
        {
            Assert.That(layout.IsVisible, Is.True);
            Assert.That(layout.ButtonBounds.Y, Is.EqualTo(376));
            Assert.That(layout.ClipBounds, Is.EqualTo(new Rect(0, 390, 500, 10)));
        });

        var gone = StickyCodeBlockBehavior.CalculateLayout(
            new Rect(0, 0, 500, 400),
            new Rect(0, 400, 500, 300),
            ButtonSize,
            Inset);

        Assert.That(gone.IsVisible, Is.False);
    }

    [Test]
    public void ViewportRightEdgeInsideWideBlock_AnchorsToTranscriptNotCodeContentRight()
    {
        var layout = StickyCodeBlockBehavior.CalculateLayout(
            new Rect(0, 0, 1200, 300),
            new Rect(0, 0, 500, 300),
            ButtonSize,
            Inset);

        Assert.That(layout.ButtonBounds.X, Is.EqualTo(470));
    }

    [Test]
    public void BlockBelowViewport_RemainsHiddenUntilItEnters()
    {
        var layout = StickyCodeBlockBehavior.CalculateLayout(
            new Rect(0, 501, 500, 300),
            new Rect(0, 0, 500, 500),
            ButtonSize,
            Inset);

        Assert.That(layout.IsVisible, Is.False);
    }

    [Test]
    public void MultipleVisibleBlocks_EachReceiveAnIndependentVisibleLayout()
    {
        var viewport = new Rect(0, 100, 500, 600);
        var layouts = new[]
        {
            StickyCodeBlockBehavior.CalculateLayout(
                new Rect(0, 40, 500, 160), viewport, ButtonSize, Inset),
            StickyCodeBlockBehavior.CalculateLayout(
                new Rect(0, 220, 500, 180), viewport, ButtonSize, Inset),
            StickyCodeBlockBehavior.CalculateLayout(
                new Rect(0, 420, 500, 340), viewport, ButtonSize, Inset),
        };

        Assert.Multiple(() =>
        {
            Assert.That(layouts, Has.All.Property(nameof(StickyCodeBlockBehavior.StickyLayout.IsVisible)).True);
            Assert.That(layouts[0].ButtonBounds.Y, Is.EqualTo(102));
            Assert.That(layouts[1].ButtonBounds.Y, Is.EqualTo(222));
            Assert.That(layouts[2].ButtonBounds.Y, Is.EqualTo(422));
        });
    }

    [Test]
    public void ViewportResize_ReanchorsButtonToNewTranscriptRightEdge()
    {
        var codeBlock = new Rect(0, 0, 700, 300);
        var wide = StickyCodeBlockBehavior.CalculateLayout(
            codeBlock, new Rect(0, 0, 600, 300), ButtonSize, Inset);
        var narrow = StickyCodeBlockBehavior.CalculateLayout(
            codeBlock, new Rect(0, 0, 360, 300), ButtonSize, Inset);

        Assert.Multiple(() =>
        {
            Assert.That(wide.ButtonBounds.X, Is.EqualTo(570));
            Assert.That(narrow.ButtonBounds.X, Is.EqualTo(330));
        });
    }

    [Test]
    [NonParallelizable]
    public void LiveAdornerLayer_MultipleBlocksResizeAndClose_CleansUpEveryAdorner()
    {
        EnsureApplicationStaysAliveForWindowTests();
        var firstBlock = new Grid { Width = 700, Height = 180 };
        var secondBlock = new Grid { Width = 700, Height = 180 };
        var firstButton = new Button { Width = 26, Height = 22, Margin = Inset };
        var secondButton = new Button { Width = 26, Height = 22, Margin = Inset };
        StickyCodeBlockBehavior.Attach(firstBlock, firstButton);
        StickyCodeBlockBehavior.Attach(secondBlock, secondButton);

        var blocks = new StackPanel();
        blocks.Children.Add(firstBlock);
        blocks.Children.Add(secondBlock);
        var viewer = new ScrollViewer { Content = blocks };
        var decorator = new AdornerDecorator { Child = viewer };
        var window = new Window
        {
            Content = decorator,
            Width = 640,
            Height = 320,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

        AdornerLayer? layer = null;
        try
        {
            window.Show();
            FlushLayout(window);
            layer = AdornerLayer.GetAdornerLayer(firstBlock);

            Assert.Multiple(() =>
            {
                Assert.That(layer, Is.Not.Null);
                Assert.That(layer!.GetAdorners(firstBlock), Has.Exactly(1).Items);
                Assert.That(layer.GetAdorners(secondBlock), Has.Exactly(1).Items);
                Assert.That(firstButton.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(secondButton.Visibility, Is.EqualTo(Visibility.Visible));
            });

            var wideX = firstButton.TranslatePoint(new Point(0, 0), firstBlock).X;
            window.Width = 380;
            FlushLayout(window);
            var narrowX = firstButton.TranslatePoint(new Point(0, 0), firstBlock).X;

            Assert.That(narrowX, Is.LessThan(wideX));
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        Assert.Multiple(() =>
        {
            Assert.That(layer, Is.Not.Null);
            Assert.That(layer!.GetAdorners(firstBlock), Is.Null.Or.Empty);
            Assert.That(layer.GetAdorners(secondBlock), Is.Null.Or.Empty);
        });
    }

    [Test]
    [NonParallelizable]
    public void LiveFlowDocument_CodeBlockAttachesAdornerOutsideDocumentVisual()
    {
        EnsureApplicationStaysAliveForWindowTests();
        var block = new Grid { Height = 180 };
        var button = new Button { Width = 26, Height = 22, Margin = Inset };
        StickyCodeBlockBehavior.Attach(block, button);

        var document = new FlowDocument();
        document.Blocks.Add(new BlockUIContainer(block));
        var transcript = new RichTextBox(document)
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var decorator = new AdornerDecorator { Child = transcript };
        var window = new Window
        {
            Content = decorator,
            Width = 640,
            Height = 320,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            FlushLayout(window);
            var layer = AdornerLayer.GetAdornerLayer(block);

            Assert.Multiple(() =>
            {
                Assert.That(layer, Is.Not.Null);
                Assert.That(layer!.GetAdorners(block), Has.Exactly(1).Items);
                Assert.That(button.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(button.ActualWidth, Is.EqualTo(26));
                Assert.That(button.ActualHeight, Is.EqualTo(22));
            });
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    [Test]
    [NonParallelizable]
    public void LiveFlowDocument_ZeroSizedHydrationThenExpand_ButtonRecovers()
    {
        EnsureApplicationStaysAliveForWindowTests();
        var block = new Grid { Height = 0 };
        var button = new Button { Width = 26, Height = 22, Margin = Inset };
        StickyCodeBlockBehavior.Attach(block, button);

        var document = new FlowDocument();
        document.Blocks.Add(new BlockUIContainer(block));
        var transcript = new RichTextBox(document)
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var window = new Window
        {
            Content = new AdornerDecorator { Child = transcript },
            Width = 640,
            Height = 320,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            FlushLayout(window);
            Assert.That(button.Visibility, Is.EqualTo(Visibility.Hidden));

            block.Height = 180;
            FlushLayout(window);

            Assert.Multiple(() =>
            {
                Assert.That(button.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(button.ActualWidth, Is.EqualTo(26));
                Assert.That(button.ActualHeight, Is.EqualTo(22));
            });
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static void FlushLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void EnsureApplicationStaysAliveForWindowTests()
    {
        var app = Application.Current ?? new Application();
        if (app.Dispatcher.CheckAccess())
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        else
        {
            app.Dispatcher.Invoke(() => app.ShutdownMode = ShutdownMode.OnExplicitShutdown);
        }
    }
}
