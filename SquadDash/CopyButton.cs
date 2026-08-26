using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace SquadDash;

public class CopyButton : Button
{
    private readonly DispatcherTimer _resetTimer;
    private readonly Popup _copiedPopup;

    public static readonly DependencyProperty CopyTooltipProperty =
        DependencyProperty.Register(
            nameof(CopyTooltip),
            typeof(string),
            typeof(CopyButton),
            new PropertyMetadata("Copy to clipboard", OnCopyTooltipChanged));

    public static readonly DependencyProperty FeedbackDurationProperty =
        DependencyProperty.Register(
            nameof(FeedbackDuration),
            typeof(TimeSpan),
            typeof(CopyButton),
            new PropertyMetadata(TimeSpan.FromSeconds(5), OnFeedbackDurationChanged));

    private static readonly DependencyPropertyKey IsCopiedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsCopied),
            typeof(bool),
            typeof(CopyButton),
            new PropertyMetadata(false, OnIsCopiedChanged));

    public static readonly DependencyProperty IsCopiedProperty = IsCopiedPropertyKey.DependencyProperty;

    public string CopyTooltip
    {
        get => (string)GetValue(CopyTooltipProperty);
        set => SetValue(CopyTooltipProperty, value);
    }

    public TimeSpan FeedbackDuration
    {
        get => (TimeSpan)GetValue(FeedbackDurationProperty);
        set => SetValue(FeedbackDurationProperty, value);
    }

    public bool IsCopied
    {
        get => (bool)GetValue(IsCopiedProperty);
        private set => SetValue(IsCopiedPropertyKey, value);
    }

    public CopyButton()
    {
        // Keep icon-only copy actions tied to the application's live font scale even
        // when a caller replaces the implicit CopyButton style with a keyed style.
        SetResourceReference(FontSizeProperty, "FontSizeNormal");

        var popupText = new TextBlock
        {
            Text = "Copied."
        };
        popupText.SetResourceReference(TextBlock.ForegroundProperty, "ToolTipForeground");

        var popupBorder = new Border
        {
            Child = popupText,
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            SnapsToDevicePixels = true
        };
        popupBorder.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

        _copiedPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Top,
            VerticalOffset = -4,
            StaysOpen = true,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Focusable = false,
            IsHitTestVisible = false,
            Child = popupBorder
        };

        _resetTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = FeedbackDuration
        };
        _resetTimer.Tick += (_, _) =>
        {
            _resetTimer.Stop();
            _copiedPopup.IsOpen = false;
            IsCopied = false;
        };
        Unloaded += (_, _) =>
        {
            _resetTimer.Stop();
            _copiedPopup.IsOpen = false;
        };
        UpdateVisualState();
    }

    protected override void OnClick()
    {
        base.OnClick();
        IsCopied = true;
        _copiedPopup.IsOpen = false;
        _copiedPopup.IsOpen = true;
        _resetTimer.Stop();
        _resetTimer.Interval = FeedbackDuration;
        _resetTimer.Start();
    }

    private static void OnCopyTooltipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CopyButton button && !button.IsCopied)
            button.UpdateVisualState();
    }

    private static void OnFeedbackDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CopyButton button)
            return;

        button._resetTimer.Interval = button.FeedbackDuration;
    }

    private static void OnIsCopiedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CopyButton button)
            button.UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        ToolTip = IsCopied ? "Copied." : CopyTooltip;

        var resourceKey = IsCopied ? "CopiedFeedback" : "CopyButton";
        var icon = TryFindResource(resourceKey) ?? Application.Current.TryFindResource(resourceKey);
        if (icon is FrameworkElement iconElement)
        {
            var fontSizeBinding = new Binding(nameof(FontSize)) { Source = this };
            BindingOperations.SetBinding(iconElement, FrameworkElement.WidthProperty, fontSizeBinding);
            BindingOperations.SetBinding(
                iconElement,
                FrameworkElement.HeightProperty,
                new Binding(nameof(FontSize)) { Source = this });
        }
        Content = icon ?? (IsCopied ? "✔" : "⧉");
    }
}
