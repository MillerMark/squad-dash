using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace SquadDash;

internal static class TranscriptAnnotationShortcut
{
    internal static bool TryStart(
        RichTextBox? transcript,
        string? typedText,
        bool startDictation,
        Func<RichTextBox, FollowUpAttachment?> ensureAttachment,
        Action<FollowUpAttachment, string?, bool> openEditor)
    {
        if (transcript is not { IsKeyboardFocusWithin: true } || transcript.Selection.IsEmpty)
            return false;
        if (!startDictation && (string.IsNullOrEmpty(typedText) || typedText.All(char.IsControl)))
            return false;
        if (ensureAttachment(transcript) is not { } attachment)
            return false;

        openEditor(attachment, typedText, startDictation);
        return true;
    }

    internal static FollowUpAttachment? EnsureAttachment(
        List<FollowUpAttachment> attachments, FollowUpAttachment attachment)
    {
        var existing = attachments.FirstOrDefault(item => item.TranscriptQuote == attachment.TranscriptQuote);
        if (existing is not null)
            return existing;
        if (attachments.Count >= 15)
            return null;

        attachments.Add(attachment);
        return attachment;
    }
}
