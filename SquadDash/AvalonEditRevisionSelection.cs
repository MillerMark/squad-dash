using ICSharpCode.AvalonEdit.Document;

namespace SquadDash;

internal sealed class AvalonEditRevisionSelection
{
    private readonly TextDocument _document;
    private readonly TextAnchor _start;
    private readonly TextAnchor _end;
    internal string OriginalText { get; }
    internal bool IsPending { get; set; }

    internal AvalonEditRevisionSelection(TextDocument document, int start, int length)
    {
        _document = document;
        OriginalText = document.GetText(start, length);
        _start = document.CreateAnchor(start);
        _start.MovementType = AnchorMovementType.AfterInsertion;
        _end = document.CreateAnchor(start + length);
        _end.MovementType = AnchorMovementType.BeforeInsertion;
    }

    internal bool TryGetRange(out int start, out int length)
    {
        start = length = 0;
        if (_start.IsDeleted || _end.IsDeleted || _end.Offset < _start.Offset)
            return false;
        start = _start.Offset;
        length = _end.Offset - start;
        return true;
    }

    internal bool TryReplace(string revised)
    {
        if (!TryGetRange(out var start, out var length) || _document.GetText(start, length) != OriginalText)
            return false;
        var replacement = revised.ReplaceLineEndings("\n");
        if (replacement != OriginalText)
            _document.Replace(start, length, replacement);
        return true;
    }
}
