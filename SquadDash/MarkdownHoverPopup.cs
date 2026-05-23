namespace SquadDash;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

/// <summary>
/// Attaches a themed, animated, markdown-rendering hover popup to any panel row.
/// All three panels (Tasks, Notes, Maintenance) share this single implementation.
/// </summary>
internal static class MarkdownHoverPopup {

    // Shared across all panels so "instant open" works when sliding between any rows.
    private static DateTime _lastShownTime = DateTime.MinValue;

    // Incremented for each Attach call so trace lines can be correlated across calls.
    private static int _attachSeq;

    /// <summary>
    /// Attaches a markdown hover popup to <paramref name="row"/>.
    /// The popup opens after a 400 ms hover-dwell (instant if one was recently visible),
    /// fades out when the mouse leaves, and lazily builds content on first open.
    /// </summary>
    /// <param name="row">The element that triggers the popup on hover.</param>
    /// <param name="buildHeader">
    ///   Optional factory returning a header element shown above the markdown body.
    ///   Called once, lazily, on first open.
    /// </param>
    /// <param name="getMarkdown">Lazily returns the markdown string. Called once on first open.</param>
    /// <param name="placement">Which side of the row to place the popup.</param>
    /// <param name="maxWidth">Maximum width of the popup border.</param>
    /// <param name="maxHeight">
    ///   Maximum height of the markdown scroll viewer. Pass 0 (default) to use
    ///   up to 50% of the primary screen height automatically.
    /// </param>
    public static void Attach(
        FrameworkElement  row,
        Func<UIElement?>? buildHeader,
        Func<string?>     getMarkdown,
        PlacementMode     placement = PlacementMode.Left,
        double            maxWidth  = 680,
        double            maxHeight = 0) {

        var seq = System.Threading.Interlocked.Increment(ref _attachSeq);
        var rowType = row.GetType().Name;
        var rowTag  = row.Tag?.ToString() ?? "(no tag)";
        SquadDashTrace.Write(TraceCategory.UI,
            $"HoverPopup[{seq}] Attach — row={rowType} tag={rowTag} placement={placement} maxW={maxWidth}");

        // Resolve effective max height: caller-supplied value, or 50% of screen height.
        double EffectiveMaxHeight() =>
            maxHeight > 0 ? maxHeight
                          : SystemParameters.PrimaryScreenHeight * 0.5;

        var contentStack = new StackPanel { Margin = new Thickness(10) };

        var popupBorder = new Border {
            Child           = contentStack,
            Padding         = new Thickness(0),
            BorderThickness = new Thickness(1),
            MinWidth        = 440,
            MaxWidth        = maxWidth,
        };

        var popup = new Popup {
            Child              = popupBorder,
            PlacementTarget    = row,
            Placement          = placement,
            StaysOpen          = true,
            AllowsTransparency = true,
        };

        bool brushesResolved = false;
        void ResolveBrushes() {
            if (brushesResolved) return;
            brushesResolved = true;
            var surface = row.TryFindResource("PopupSurface") as Brush;
            var border  = row.TryFindResource("ActivePanelBorder") as Brush;
            SquadDashTrace.Write(TraceCategory.UI,
                $"HoverPopup[{seq}] ResolveBrushes — PopupSurface={(surface is null ? "NULL" : surface.ToString())} ActivePanelBorder={(border is null ? "NULL" : "ok")}");
            popupBorder.Background  = surface
                                   ?? new SolidColorBrush(Color.FromRgb(0x30, 0x2C, 0x28));
            popupBorder.BorderBrush = border
                                   ?? new SolidColorBrush(Color.FromRgb(0x55, 0x4E, 0x47));
        }

        bool contentBuilt = false;
        void EnsureContentBuilt() {
            if (contentBuilt) return;
            contentBuilt = true;

            var header = buildHeader?.Invoke();
            SquadDashTrace.Write(TraceCategory.UI,
                $"HoverPopup[{seq}] EnsureContentBuilt — hasHeader={header is not null}");

            if (header is not null)
                contentStack.Children.Add(header);

            var markdown = getMarkdown();
            SquadDashTrace.Write(TraceCategory.UI,
                $"HoverPopup[{seq}] markdown={(markdown is null ? "NULL" : string.IsNullOrWhiteSpace(markdown) ? "WHITESPACE" : $"{markdown.Length} chars: {markdown[..Math.Min(80, markdown.Length)].Replace('\n', '↵')}")}");

            if (!string.IsNullOrWhiteSpace(markdown)) {
                var doc = MarkdownFlowDocumentBuilder.Build(markdown);
                doc.TextAlignment = TextAlignment.Left;
                var bg = Application.Current.Resources["PopupSurface"] as Brush
                      ?? new SolidColorBrush(Color.FromRgb(0x30, 0x2C, 0x28));
                SquadDashTrace.Write(TraceCategory.UI,
                    $"HoverPopup[{seq}] building RichTextBox — bg={(bg is null ? "NULL" : bg.ToString())} maxH={EffectiveMaxHeight()}");
                var viewer = new RichTextBox(doc) {
                    IsReadOnly                    = true,
                    IsDocumentEnabled             = true,
                    Background                    = bg,
                    BorderThickness               = new Thickness(0),
                    MaxWidth                      = maxWidth - 20,
                    MaxHeight                     = EffectiveMaxHeight(),
                    Margin                        = header is null ? new Thickness(0) : new Thickness(0, 6, 0, 0),
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                };
                contentStack.Children.Add(viewer);
                SquadDashTrace.Write(TraceCategory.UI,
                    $"HoverPopup[{seq}] RichTextBox added to contentStack (children={contentStack.Children.Count})");
            } else if (header is null) {
                SquadDashTrace.Write(TraceCategory.UI,
                    $"HoverPopup[{seq}] no markdown and no header — showing 'No content' placeholder");
                var noContent = new TextBlock { Text = "No content", FontStyle = FontStyles.Italic, Opacity = 0.6 };
                noContent.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                noContent.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
                contentStack.Children.Add(noContent);
            } else {
                SquadDashTrace.Write(TraceCategory.UI,
                    $"HoverPopup[{seq}] no markdown but header present — showing header only");
            }
        }

        bool isFading = false;
        bool isMouseOverPopup = false;
        DispatcherTimer? fadeDelayTimer = null;
        DispatcherTimer? openTimer = null;

        void CancelPendingFade() {
            fadeDelayTimer?.Stop();
            fadeDelayTimer = null;
        }

        void CancelFade() {
            CancelPendingFade();
            if (!isFading) return;
            popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
            popupBorder.Opacity = 1.0;
            isFading = false;
        }

        void BeginFadeOut() {
            if (!popup.IsOpen || isFading || isMouseOverPopup) return;
            isFading = true;
            var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(350)) {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) => {
                popup.IsOpen = false;
                popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
                popupBorder.Opacity = 1.0;
                isFading = false;
            };
            popupBorder.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        void ScheduleFadeOut(int delayMs = 500) {
            CancelPendingFade();
            if (!popup.IsOpen || isMouseOverPopup) return;
            fadeDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            fadeDelayTimer.Tick += (_, _) => {
                fadeDelayTimer!.Stop();
                fadeDelayTimer = null;
                BeginFadeOut();
            };
            fadeDelayTimer.Start();
        }

        void OpenPopup() {
            if (popup.IsOpen || isFading) return;
            SquadDashTrace.Write(TraceCategory.UI, $"HoverPopup[{seq}] OpenPopup");
            ResolveBrushes();
            EnsureContentBuilt();
            popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
            popupBorder.Opacity = 1.0;
            popup.IsOpen = true;
            SquadDashTrace.Write(TraceCategory.UI,
                $"HoverPopup[{seq}] popup opened — IsOpen={popup.IsOpen} borderBg={popupBorder.Background} children={contentStack.Children.Count}");
            _lastShownTime = DateTime.Now;
        }

        void StartOpenTimer(bool instant = false) {
            openTimer?.Stop();
            openTimer = null;
            if (instant) {
                OpenPopup();
            } else {
                openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                openTimer.Tick += (_, _) => {
                    openTimer!.Stop();
                    openTimer = null;
                    OpenPopup();
                };
                openTimer.Start();
            }
        }

        popupBorder.MouseEnter += (_, _) => {
            isMouseOverPopup = true;
            CancelFade();
        };
        popupBorder.MouseLeave += (_, _) => {
            isMouseOverPopup = false;
            BeginFadeOut();
        };

        row.MouseEnter += (_, _) => {
            CancelFade();
            if (!popup.IsOpen) {
                bool instant = (DateTime.Now - _lastShownTime).TotalMilliseconds < 1000;
                StartOpenTimer(instant);
            }
        };
        row.MouseLeave += (_, _) => {
            openTimer?.Stop();
            openTimer = null;
            ScheduleFadeOut(500);
        };

        row.PreviewMouseDown += (_, _) => BeginFadeOut();

        row.Unloaded += (_, _) => {
            openTimer?.Stop();
            openTimer = null;
            fadeDelayTimer?.Stop();
            fadeDelayTimer = null;
            if (popup.IsOpen) {
                popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
                popup.IsOpen = false;
            }
        };
    }
}
