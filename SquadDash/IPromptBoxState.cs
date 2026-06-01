namespace SquadDash;

internal interface IPromptBoxState {
    void ClearPromptTextBox();
    void FocusPromptTextBox();
    bool IsPromptTextBoxEnabled { get; }
    int QueueCount { get; }
    string PromptBoxText { get; }
    void SetPromptBoxText(string text);
    void EnqueueSimItem(PromptQueueItem item);
}
