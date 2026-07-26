using System;

namespace SquadDash;

/// <summary>
/// Routes <see cref="IPromptBoxState"/> calls to MainWindow prompt-box state via injected delegates.
/// MainWindow holds one instance and passes it wherever an <see cref="IPromptBoxState"/> is required.
/// </summary>
internal sealed class PromptBoxStateController : IPromptBoxState
{
    private readonly Action                    _clearPromptTextBox;
    private readonly Action                    _focusPromptTextBox;
    private readonly Func<bool>               _getIsEnabled;
    private readonly Func<int>                _getQueueCount;
    private readonly Func<string>             _getPromptBoxText;
    private readonly Action<string>           _setPromptBoxText;
    private readonly Action<PromptQueueItem>  _enqueueSimItem;

    internal PromptBoxStateController(
        Action                   clearPromptTextBox,
        Action                   focusPromptTextBox,
        Func<bool>               getIsEnabled,
        Func<int>                getQueueCount,
        Func<string>             getPromptBoxText,
        Action<string>           setPromptBoxText,
        Action<PromptQueueItem>  enqueueSimItem)
    {
        _clearPromptTextBox = clearPromptTextBox ?? throw new ArgumentNullException(nameof(clearPromptTextBox));
        _focusPromptTextBox = focusPromptTextBox ?? throw new ArgumentNullException(nameof(focusPromptTextBox));
        _getIsEnabled       = getIsEnabled       ?? throw new ArgumentNullException(nameof(getIsEnabled));
        _getQueueCount      = getQueueCount      ?? throw new ArgumentNullException(nameof(getQueueCount));
        _getPromptBoxText   = getPromptBoxText   ?? throw new ArgumentNullException(nameof(getPromptBoxText));
        _setPromptBoxText   = setPromptBoxText   ?? throw new ArgumentNullException(nameof(setPromptBoxText));
        _enqueueSimItem     = enqueueSimItem     ?? throw new ArgumentNullException(nameof(enqueueSimItem));
    }

    // ── IPromptBoxState ───────────────────────────────────────────────────
    void   IPromptBoxState.ClearPromptTextBox()                => _clearPromptTextBox();
    void   IPromptBoxState.FocusPromptTextBox()                => _focusPromptTextBox();
    bool   IPromptBoxState.IsPromptTextBoxEnabled              => _getIsEnabled();
    int    IPromptBoxState.QueueCount                          => _getQueueCount();
    string IPromptBoxState.PromptBoxText                       => _getPromptBoxText();
    void   IPromptBoxState.SetPromptBoxText(string text)       => _setPromptBoxText(text);
    void   IPromptBoxState.EnqueueSimItem(PromptQueueItem item) => _enqueueSimItem(item);
}
