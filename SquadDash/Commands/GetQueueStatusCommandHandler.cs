namespace SquadDash.Commands;

internal sealed class GetQueueStatusCommandHandler : IHostCommandHandler {
    private readonly Func<IReadOnlyList<PromptQueueItem>> _getQueueItems;

    public GetQueueStatusCommandHandler(Func<IReadOnlyList<PromptQueueItem>> getQueueItems) =>
        _getQueueItems = getQueueItems;

    public string CommandName => "get_queue_status";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        var items = _getQueueItems();
        var json = System.Text.Json.JsonSerializer.Serialize(
            items.Select(i => new { id = i.Id, text = i.Text, sequenceNumber = i.SequenceNumber }),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return new HostCommandResult(true, Output: $"Queue status ({items.Count} items):\n{json}");
    }
}
