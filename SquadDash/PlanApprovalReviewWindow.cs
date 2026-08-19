using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// A modeless, single-plan host for the complete human-approval checklist. Keeping the
/// interactive card outside the coordinator transcript lets unrelated plan work continue
/// without inserting approval UI into an actively written conversation.
/// </summary>
internal sealed class PlanApprovalReviewWindow : Window
{
    private readonly TextBlock _status;
    private readonly FlowDocument _document;
    private TranscriptApprovalCardBuilder.CardResult? _card;

    internal string PlanId { get; }
    internal ApprovalClickToken CurrentToken { get; private set; }

    internal PlanApprovalReviewWindow(
        string planId,
        string planTitle,
        ApprovalClickToken token,
        TranscriptApprovalCardBuilder.CardResult card)
    {
        PlanId = planId;
        CurrentToken = token;
        Title = $"Approval Review — {planTitle}";
        Width = 820;
        Height = 720;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;

        SetResourceReference(BackgroundProperty, "AppSurface");

        _status = new TextBlock
        {
            Text = "Review the current checklist. No approval has been applied.",
            Margin = new Thickness(14, 10, 14, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        _document = new FlowDocument
        {
            PagePadding = new Thickness(12, 4, 12, 16),
            ColumnWidth = double.PositiveInfinity,
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"),
        };
        _document.SetResourceReference(TextElement.ForegroundProperty, "BodyText");

        var viewer = new FlowDocumentScrollViewer
        {
            Document = _document,
            IsToolBarVisible = false,
            IsSelectionEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var root = new DockPanel();
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);
        root.Children.Add(viewer);
        Content = root;

        Replace(planTitle, token, card);
    }

    internal bool Matches(ApprovalClickToken token) => CurrentToken.Matches(token);

    internal void Replace(
        string planTitle,
        ApprovalClickToken token,
        TranscriptApprovalCardBuilder.CardResult card,
        string? status = null)
    {
        Title = $"Approval Review — {planTitle}";
        CurrentToken = token;
        _card = card;
        _document.Blocks.Clear();
        _document.Blocks.Add(card.Container);
        ShowStatus(status ?? "Review the current checklist. No approval has been applied.");
    }

    internal void BeginApprovalUpdate()
    {
        if (_card is not null)
            TranscriptApprovalCardBuilder.ShowUpdatingState(_card);
        ShowStatus("Applying approval…");
    }

    internal void CancelApprovalUpdate(string status)
    {
        if (_card is not null)
            TranscriptApprovalCardBuilder.HideUpdatingState(_card);
        ShowStatus(status);
    }

    internal void ShowStatus(string status)
    {
        _status.Text = status;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
    }
}
