using System.Threading.Tasks;

namespace SquadDash;

internal sealed class PostedUiActionTracker {
    private readonly object _gate = new();
    private long _postedCount;
    private long _completedCount;
    private TaskCompletionSource<bool>? _drainedTcs;

    public long RegisterPostedAction() {
        lock (_gate) {
            _postedCount++;
            _drainedTcs ??= CreateCompletionSource();
            return _postedCount;
        }
    }

    public void MarkCompleted(long sequence) {
        TaskCompletionSource<bool>? completion = null;

        lock (_gate) {
            if (sequence > _completedCount)
                _completedCount = sequence;

            if (_completedCount >= _postedCount && _drainedTcs is not null) {
                completion = _drainedTcs;
                _drainedTcs = null;
            }
        }

        completion?.TrySetResult(true);
    }

    public Task WaitForDrainAsync() {
        lock (_gate) {
            if (_completedCount >= _postedCount)
                return Task.CompletedTask;

            _drainedTcs ??= CreateCompletionSource();
            return _drainedTcs.Task;
        }
    }

    private static TaskCompletionSource<bool> CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
