using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// An animated inline indicator inserted into a <see cref="RichTextBox"/> FlowDocument
/// at the end of the user's selection while a Revise-with-AI request is in flight.
///
/// The indicator is an <see cref="InlineUIContainer"/> and is therefore intentionally
/// excluded from <see cref="RichTextBoxExtensions.GetPlainText"/> output — it never
/// appears in the text written to disk.
/// </summary>
internal sealed class RevisionPendingIndicator
{
    // Safety timeout slightly beyond the 120 s AI request timeout.
    private const double FallbackTimeoutSeconds = 130;

    private readonly InlineUIContainer _container;
    private readonly DispatcherTimer   _fallbackTimer;
    private bool _removed;

    private RevisionPendingIndicator(InlineUIContainer container)
    {
        _container = container;

        _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FallbackTimeoutSeconds) };
        _fallbackTimer.Tick += (_, _) => Remove();
        _fallbackTimer.Start();
    }

    /// <summary>
    /// Inserts a pulsing indicator inline immediately after <paramref name="afterCharOffset"/>
    /// in <paramref name="rtb"/>'s FlowDocument. Returns <c>null</c> on any error (the
    /// indicator is cosmetic — failure must never affect the revision flow).
    /// </summary>
    internal static RevisionPendingIndicator? Insert(RichTextBox rtb, int afterCharOffset)
    {
        try
        {
            var pointer     = rtb.GetTextPointerAt(afterCharOffset);
            var insertPoint = pointer.GetInsertionPosition(LogicalDirection.Forward);
            var element     = BuildElement(rtb);
            var container   = new InlineUIContainer(element, insertPoint);
            return new RevisionPendingIndicator(container);
        }
        catch { return null; }
    }

    /// <summary>
    /// Removes the indicator from the FlowDocument. Safe to call more than once.
    /// </summary>
    internal void Remove()
    {
        if (_removed) return;
        _removed = true;
        _fallbackTimer.Stop();
        try { _container.SiblingInlines?.Remove(_container); }
        catch { }
    }

    // ── Visual ───────────────────────────────────────────────────────────────

    private static UIElement BuildElement(RichTextBox rtb)
    {
        // Three dots that pulse with staggered phase — a classic "typing" indicator.
        var panel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(4, 0, 2, 0),
        };

        for (int i = 0; i < 3; i++)
        {
            var dot = new Ellipse
            {
                Width  = 5,
                Height = 5,
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
            };
            dot.SetResourceReference(Shape.FillProperty, "ActionLinkText");

            var anim = new DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(550))
            {
                AutoReverse      = true,
                RepeatBehavior   = RepeatBehavior.Forever,
                BeginTime        = TimeSpan.FromMilliseconds(i * 180),
                EasingFunction   = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            dot.BeginAnimation(UIElement.OpacityProperty, anim);
            panel.Children.Add(dot);
        }

        return panel;
    }
}
