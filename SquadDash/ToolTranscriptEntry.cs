using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace SquadDash;

internal sealed class TranscriptTurnView {
    public TranscriptTurnView(
        TranscriptThreadState ownerThread,
        string prompt,
        DateTimeOffset startedAt,
        Section narrativeSection,
        IReadOnlyList<Block> topLevelBlocks) {
        OwnerThread = ownerThread;
        Prompt = prompt;
        StartedAt = startedAt;
        NarrativeSection = narrativeSection;
        TopLevelBlocks = topLevelBlocks;
        ResponseTextBuilder = new StringBuilder();
        ThoughtEntries = new List<TranscriptThoughtEntry>();
        ThoughtBlocks = new List<TranscriptThoughtBlockView>();
        ThinkingBlocks = new List<TranscriptThinkingBlockView>();
        ResponseEntries = new List<TranscriptResponseEntry>();
        ToolEntries = new List<ToolTranscriptEntry>();
        NextNarrativeSequence = 1;
    }

    public TranscriptThreadState OwnerThread { get; }
    public string Prompt { get; }
    public DateTimeOffset StartedAt { get; }
    public Section NarrativeSection { get; }
    public IReadOnlyList<Block> TopLevelBlocks { get; }
    public StringBuilder ResponseTextBuilder { get; }
    public List<TranscriptThoughtEntry> ThoughtEntries { get; }
    public List<TranscriptThoughtBlockView> ThoughtBlocks { get; }
    public List<TranscriptThinkingBlockView> ThinkingBlocks { get; }
    public List<TranscriptResponseEntry> ResponseEntries { get; }
    public List<ToolTranscriptEntry> ToolEntries { get; }
    public int NextNarrativeSequence { get; set; }
}

internal sealed class TranscriptThinkingBlockView : ICopyable {
    public TranscriptThinkingBlockView(
        TranscriptTurnView turn,
        int sequence,
        TextBlock headerTextBlock,
        Expander expander,
        StackPanel contentPanel) {
        Turn = turn;
        Sequence = sequence;
        HeaderTextBlock = headerTextBlock;
        Expander = expander;
        ContentPanel = contentPanel;
        ToolEntries = new List<ToolTranscriptEntry>();
    }

    public TranscriptTurnView Turn { get; }
    public int Sequence { get; }
    public TextBlock HeaderTextBlock { get; }
    public Expander Expander { get; }
    public StackPanel ContentPanel { get; }
    public List<ToolTranscriptEntry> ToolEntries { get; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public bool UserPinnedOpen { get; set; }

    internal bool ShouldCollapse() => !UserPinnedOpen;

    public string GetCopyText() {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Tooling...]");
        foreach (var entry in ToolEntries.OrderBy(e => e.StartedAt)) {
            var line = entry.GetCopyText();
            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine("  " + line);
        }
        return sb.ToString().TrimEnd();
    }
}

internal sealed class TranscriptThoughtBlockView : ICopyable {
    public TranscriptThoughtBlockView(
        TranscriptTurnView turn,
        int sequence,
        TextBlock headerTextBlock,
        Expander expander,
        StackPanel contentPanel) {
        Turn = turn;
        Sequence = sequence;
        HeaderTextBlock = headerTextBlock;
        Expander = expander;
        ContentPanel = contentPanel;
        ThoughtEntries = new List<TranscriptThoughtEntry>();
    }

    public TranscriptTurnView Turn { get; }
    public int Sequence { get; }
    public TextBlock HeaderTextBlock { get; }
    public Expander Expander { get; }
    public StackPanel ContentPanel { get; }
    public List<TranscriptThoughtEntry> ThoughtEntries { get; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public bool UserPinnedOpen { get; set; }

    internal bool ShouldCollapse() => !UserPinnedOpen;

    public string GetCopyText() {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Thinking...]");
        foreach (var entry in ThoughtEntries) {
            var text = entry.RawTextBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine($"  {entry.Speaker}: {text}");
        }
        return sb.ToString().TrimEnd();
    }
}

internal sealed class TranscriptThoughtEntry {
    public TranscriptThoughtEntry(
        TranscriptTurnView turn,
        int sequence,
        string speaker,
        TextBlock textBlock) {
        Turn = turn;
        Sequence = sequence;
        Speaker = speaker;
        TextBlock = textBlock;
        RawTextBuilder = new StringBuilder();
    }

    public TranscriptTurnView Turn { get; }
    public int Sequence { get; }
    public string Speaker { get; }
    public TextBlock TextBlock { get; }
    public StringBuilder RawTextBuilder { get; }
}

internal sealed class TranscriptResponseEntry {
    public TranscriptResponseEntry(
        TranscriptTurnView turn,
        int sequence,
        Section section,
        bool allowQuickReplies = true) {
        Turn = turn;
        Sequence = sequence;
        Section = section;
        AllowQuickReplies = allowQuickReplies;
        RawTextBuilder = new StringBuilder();
    }

    public TranscriptTurnView Turn { get; }
    public int Sequence { get; }
    public Section Section { get; }
    public bool AllowQuickReplies { get; set; }
    public StringBuilder RawTextBuilder { get; }
    public bool HasPendingRender { get; set; }
    public DateTimeOffset? LastRenderedAt { get; set; }
}

internal sealed class ToolTranscriptEntry : ICopyable {
    public ToolTranscriptEntry(
        string toolCallId,
        TranscriptTurnView turn,
        TranscriptThinkingBlockView thinkingBlock,
        ToolTranscriptDescriptor descriptor,
        string? argsJson,
        DateTimeOffset startedAt,
        Expander expander,
        TextBlock iconTextBlock,
        Image emojiImage,
        TextBlock messageTextBlock,
        TextBox detailTextBox,
        Button transcriptButton) {
        ToolCallId = toolCallId;
        Turn = turn;
        ThinkingBlock = thinkingBlock;
        Descriptor = descriptor;
        ArgsJson = argsJson;
        StartedAt = startedAt;
        Expander = expander;
        IconTextBlock = iconTextBlock;
        EmojiImage = emojiImage;
        MessageTextBlock = messageTextBlock;
        DetailTextBox = detailTextBox;
        TranscriptButton = transcriptButton;
    }

    public string ToolCallId { get; }
    public TranscriptTurnView Turn { get; }
    public TranscriptThinkingBlockView ThinkingBlock { get; }
    public ToolTranscriptDescriptor Descriptor { get; }
    public string? ArgsJson { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ProgressText { get; set; }
    public string? OutputText { get; set; }
    public string? DetailContent { get; set; }
    public bool IsCompleted { get; set; }
    public bool Success { get; set; }
    public string? TranscriptThreadId { get; set; }
    public Expander Expander { get; }
    public TextBlock IconTextBlock { get; }
    public Image EmojiImage { get; }
    public TextBlock MessageTextBlock { get; }
    public TextBox DetailTextBox { get; }
    public Button TranscriptButton { get; }

    public string GetCopyText() {
        var icon    = IconTextBlock.Text?.Trim() ?? string.Empty;
        var emoji   = ToolTranscriptFormatter.GetToolEmoji(Descriptor).Trim();
        var message = TranscriptCopyService.ExtractInlineText(MessageTextBlock.Inlines).Trim();
        return string.Join(" ", new[] { icon, emoji, message }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
