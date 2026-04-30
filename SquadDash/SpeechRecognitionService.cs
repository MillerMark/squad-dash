using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;

namespace SquadDash;

internal sealed class SpeechRecognitionService : IDisposable {
    private SpeechRecognizer? _recognizer;
    private WaveInEvent? _waveIn;
    private PushAudioInputStream? _pushStream;
    private volatile bool _stopping;

    public event EventHandler<string>? PhraseRecognized;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<string>? RecognitionError;

    public async Task StartAsync(string subscriptionKey, string region, IEnumerable<string>? phraseHints = null) {
        _stopping = false;

        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        _pushStream = AudioInputStream.CreatePushStream(format);

        var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        var audioConfig = AudioConfig.FromStreamInput(_pushStream);

        _recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        if (phraseHints is not null) {
            var phraseList = PhraseListGrammar.FromRecognizer(_recognizer);
            foreach (var phrase in phraseHints)
                if (!string.IsNullOrWhiteSpace(phrase))
                    phraseList.AddPhrase(phrase);
        }

        _recognizer.Recognized += (_, e) => {
            if (e.Result.Reason == ResultReason.RecognizedSpeech &&
                !string.IsNullOrWhiteSpace(e.Result.Text))
                PhraseRecognized?.Invoke(this, e.Result.Text);
        };

        _recognizer.Canceled += (_, e) => {
            if (!_stopping && e.Reason == CancellationReason.Error)
                RecognitionError?.Invoke(this, e.ErrorDetails ?? e.Reason.ToString());
        };

        _waveIn = new WaveInEvent {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 80
        };

        _waveIn.DataAvailable += OnAudioData;
        _waveIn.RecordingStopped += (_, _) => _pushStream?.Close();

        await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        _waveIn.StartRecording();
    }

    /// <summary>
    /// Variant for RC (remote phone) sessions: accepts an external PushAudioInputStream
    /// so the caller can write PCM bytes received over WebSocket. No WaveInEvent is created.
    /// </summary>
    public async Task StartFromStreamAsync(
        string subscriptionKey,
        string region,
        PushAudioInputStream pushStream,
        IEnumerable<string>? phraseHints = null) {
        _stopping = false;
        _pushStream = pushStream;

        var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        var audioConfig = AudioConfig.FromStreamInput(_pushStream);

        _recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        if (phraseHints is not null) {
            var phraseList = PhraseListGrammar.FromRecognizer(_recognizer);
            foreach (var phrase in phraseHints)
                if (!string.IsNullOrWhiteSpace(phrase))
                    phraseList.AddPhrase(phrase);
        }

        _recognizer.Recognized += (_, e) => {
            if (e.Result.Reason == ResultReason.RecognizedSpeech &&
                !string.IsNullOrWhiteSpace(e.Result.Text))
                PhraseRecognized?.Invoke(this, e.Result.Text);
        };

        _recognizer.Canceled += (_, e) => {
            if (!_stopping && e.Reason == CancellationReason.Error)
                RecognitionError?.Invoke(this, e.ErrorDetails ?? e.Reason.ToString());
        };

        await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Write raw PCM bytes (16 kHz / 16-bit / mono LE) from an RC audio chunk.
    /// Safe to call from any thread.
    /// </summary>
    public void WriteAudioData(byte[] buffer, int count) {
        if (_stopping || _pushStream is null) return;
        _pushStream.Write(buffer, count);
    }

    public async Task StopAsync() {
        _stopping = true;
        try { _waveIn?.StopRecording(); } catch { }
        if (_recognizer is not null) {
            try { await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); } catch { }
        }
    }

    public void Dispose() {
        _stopping = true;
        try { _waveIn?.Dispose(); } catch { }
        try { _recognizer?.Dispose(); } catch { }
        try { _pushStream?.Dispose(); } catch { }
        _waveIn = null;
        _recognizer = null;
        _pushStream = null;
    }

    private void OnAudioData(object? sender, WaveInEventArgs e) {
        if (_stopping || e.BytesRecorded == 0)
            return;

        // Compute RMS from 16-bit PCM samples for volume level
        var sampleCount = e.BytesRecorded / 2;
        double sumOfSquares = 0;
        for (var i = 0; i < e.BytesRecorded - 1; i += 2) {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            sumOfSquares += (double)sample * sample;
        }
        var rms = Math.Sqrt(sumOfSquares / sampleCount);
        var level = Math.Min(1.0, rms / 6000.0); // scale: normal speech peaks ~0.7–1.0
        VolumeChanged?.Invoke(this, level);

        _pushStream?.Write(e.Buffer, e.BytesRecorded);
    }
}
