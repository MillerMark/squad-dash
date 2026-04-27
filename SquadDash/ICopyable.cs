namespace SquadDash;

/// <summary>
/// Implemented by transcript view-model objects that can contribute plain-text content
/// to a clipboard copy operation spanning heterogeneous transcript elements.
/// Implementors must not throw; return <see cref="string.Empty"/> when there is
/// nothing to contribute (e.g. an empty tool list).
/// </summary>
internal interface ICopyable {
    string GetCopyText();
}
