using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Documents;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace SquadDash;

internal enum TranscriptThreadKind {
    Coordinator,
    Agent
}

internal record PromptEntry(Paragraph Paragraph, DateTimeOffset Timestamp);

internal sealed class TranscriptThreadState : INotifyPropertyChanged {
    private string _title;
    private string _statusText;
    private string _detailText;
    private string _chipLabel;
    private string _chipToolTip;
    private Brush _chipBackground;
    private Brush _chipBorderBrush;
    private Brush _chipForeground;
    private FontWeight _chipFontWeight;
    private Visibility _chipVisibility;
    private bool _isSelected;
    private bool _responseStreamed;

    public TranscriptThreadState(
        string threadId,
        TranscriptThreadKind kind,
        string title,
        DateTimeOffset startedAt) {
        ThreadId = threadId;
        Kind = kind;
        StartedAt = startedAt;
        Document = new FlowDocument {
            PagePadding = new Thickness(0)
        };
        _title = title;
        _statusText = string.Empty;
        _detailText = string.Empty;
        _chipLabel = string.Empty;
        _chipToolTip = string.Empty;
        _chipBackground = Brushes.White;
        _chipBorderBrush = Brushes.LightGray;
        _chipForeground = Brushes.DarkSlateGray;
        _chipFontWeight = FontWeights.Normal;
        _chipVisibility = Visibility.Collapsed;
    }

    public string ThreadId { get; }
    public TranscriptThreadKind Kind { get; }
    public FlowDocument Document { get; }
    public TranscriptTurnView? CurrentTurn { get; set; }
    public Paragraph? TransientFooterParagraph { get; set; }
    public Paragraph? CompletedTimeParagraph { get; set; }
    public string? AgentId { get; set; }
    public string? ToolCallId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentDisplayName { get; set; }
    public string? AgentDescription { get; set; }
    public string? AgentType { get; set; }
    public string? AgentCardKey { get; set; }
    public string? OriginAgentDisplayName { get; set; }
    public string? OriginParentToolCallId { get; set; }
    public string? RequestedAgentHandle { get; set; }
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(RequestedAgentHandle) && Title != AgentNameHumanizer.Humanize(RequestedAgentHandle)
            ? $"{AgentNameHumanizer.Humanize(RequestedAgentHandle)} (unverified)"
            : Title;
    public DateTimeOffset? LastObservedActivityAt { get; set; }
    public bool IsPlaceholderThread { get; set; }
    public string? Prompt { get; set; }
    public string? LatestResponse { get; set; }
    public string? LastCoordinatorAnnouncedResponse { get; set; }
    public string? LatestIntent { get; set; }
    public string[] RecentActivity { get; set; } = Array.Empty<string>();
    public string? ErrorText { get; set; }
    public bool WasObservedAsBackgroundTask { get; set; }
    public bool IsCurrentBackgroundRun { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int SequenceNumber { get; set; }
    public List<TranscriptTurnRecord> SavedTurns { get; } = [];
    public List<PromptEntry> PromptParagraphs { get; } = [];
    public int PromptNavIndex { get; set; } = -1;
    public string Title {
        get => _title;
        set => SetField(ref _title, value);
    }
    public string StatusText {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }
    public string DetailText {
        get => _detailText;
        set => SetField(ref _detailText, value);
    }
    public string ChipLabel {
        get => _chipLabel;
        set => SetField(ref _chipLabel, value);
    }
    public string ChipToolTip {
        get => _chipToolTip;
        set => SetField(ref _chipToolTip, value);
    }
    public Brush ChipBackground {
        get => _chipBackground;
        set => SetField(ref _chipBackground, value);
    }
    public Brush ChipBorderBrush {
        get => _chipBorderBrush;
        set => SetField(ref _chipBorderBrush, value);
    }
    public Brush ChipForeground {
        get => _chipForeground;
        set => SetField(ref _chipForeground, value);
    }
    public FontWeight ChipFontWeight {
        get => _chipFontWeight;
        set => SetField(ref _chipFontWeight, value);
    }
    public Visibility ChipVisibility {
        get => _chipVisibility;
        set => SetField(ref _chipVisibility, value);
    }
    public bool IsSelected {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool _isSecondaryPanelOpen;
    public bool IsSecondaryPanelOpen {
        get => _isSecondaryPanelOpen;
        set => SetField(ref _isSecondaryPanelOpen, value);
    }

    private Brush _chipSelectionIndicatorBrush = Brushes.Transparent;
    public Brush ChipSelectionIndicatorBrush {
        get => _chipSelectionIndicatorBrush;
        set => SetField(ref _chipSelectionIndicatorBrush, value);
    }

    public bool ResponseStreamed {
        get => _responseStreamed;
        set => _responseStreamed = value;
    }

    public bool SessionFooterAppended { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
