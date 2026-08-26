using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash;

public class CopyButton : Button
{
    private readonly DispatcherTimer _resetTimer;

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
        _resetTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = FeedbackDuration
        };
        _resetTimer.Tick += (_, _) =>
        {
            _resetTimer.Stop();
            IsCopied = false;
        };
        Unloaded += (_, _) => _resetTimer.Stop();
        UpdateVisualState();
    }

    protected override void OnClick()
    {
        base.OnClick();
        IsCopied = true;
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
        Content = icon ?? (IsCopied ? "✔" : "⧉");
    }
}
