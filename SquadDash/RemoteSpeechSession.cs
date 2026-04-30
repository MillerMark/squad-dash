using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech.Audio;

namespace SquadDash;

/// <summary>
/// Wraps a <see cref="SpeechRecognitionService"/> for a single RC (remote phone) PTT session.
/// Created when <c>rc_audio_start</c> arrives, disposed on <c>rc_audio_end</c>.
/// </summary>
internal sealed class RemoteSpeechSession : IAsyncDisposable {
    private readonly SpeechRecognitionService _service = new();
    private readonly PushAudioInputStream _pushStream;
    private int _disposed;

    public string ConnectionId { get; }

    /// <summary>Raised on the thread pool when a phrase is recognised.</summary>
    public event EventHandler<string>? PhraseRecognized;

    /// <summary>Raised when Azure Speech reports a fatal recognition error.</summary>
    public event EventHandler<string>? RecognitionError;

    private RemoteSpeechSession(string connectionId, PushAudioInputStream pushStream) {
        ConnectionId = connectionId;
        _pushStream = pushStream;
    }

    /// <summary>
    /// Create and start a new session for the given connection.
    /// </summary>
    public static async Task<RemoteSpeechSession> StartAsync(
        string connectionId,
        string subscriptionKey,
        string region,
        string[]? phraseHints = null) {

        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        var pushStream = AudioInputStream.CreatePushStream(format);

        var session = new RemoteSpeechSession(connectionId, pushStream);
        session._service.PhraseRecognized += (_, text) => session.PhraseRecognized?.Invoke(session, text);
        session._service.RecognitionError += (_, msg) => session.RecognitionError?.Invoke(session, msg);

        await session._service.StartFromStreamAsync(subscriptionKey, region, pushStream, phraseHints)
            .ConfigureAwait(false);

        return session;
    }

    /// <summary>
    /// Write a PCM audio chunk received from the phone.
    /// The buffer must contain 16 kHz / 16-bit / mono / little-endian PCM data.
    /// </summary>
    public void WriteAudioChunk(byte[] buffer, int count) {
        if (_disposed != 0) return;
        _pushStream.Write(buffer, count);
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        try { await _service.StopAsync().ConfigureAwait(false); } catch { }
        try { _pushStream.Close(); } catch { }
        _service.Dispose();
    }
}
