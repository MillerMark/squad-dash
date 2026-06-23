using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;

namespace SquadDash;

internal sealed class AzureSpeechRecognitionService : ISpeechRecognitionService {
    private SpeechRecognizer? _recognizer;
    private WaveInEvent? _waveIn;
    private PushAudioInputStream? _pushStream;
    private volatile bool _stopping;

    public event EventHandler<string>? PhraseRecognized;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<string>? RecognitionError;

    public async Task StartAsync(string subscriptionKey, string region, IEnumerable<string>? phraseHints = null, string? language = null) {
        _stopping = false;

        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        _pushStream = AudioInputStream.CreatePushStream(format);

        var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        if (!string.IsNullOrWhiteSpace(language))
            speechConfig.SpeechRecognitionLanguage = language.Trim();
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
        IEnumerable<string>? phraseHints = null,
        string? language = null) {
        _stopping = false;
        _pushStream = pushStream;

        var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        if (!string.IsNullOrWhiteSpace(language))
            speechConfig.SpeechRecognitionLanguage = language.Trim();
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
        // Explicitly close the push stream so Azure sees EOF immediately.
        // RecordingStopped also closes it, but fires asynchronously after the last
        // WaveIn buffer is returned — which may be after StopContinuousRecognitionAsync
        // has already started, causing the final phrase to be silently dropped.
        try { _pushStream?.Close(); } catch { }
        if (_recognizer is not null) {
            try { await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); } catch { }
        }
    }

    public void Dispose() {
        _stopping = true;
        // DisposeWaveIn is [NoInlining] so that if NAudio.WinMM fails to load (e.g. missing
        // in certain deployment configurations), the JIT failure occurs at the call-site here
        // where the catch can observe it, rather than preventing this entire method from
        // compiling.
        try { DisposeWaveIn(); }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or BadImageFormatException)
        {
            _waveIn = null;
            System.Diagnostics.Trace.TraceWarning(
                $"WaveInEvent disposal skipped — NAudio.WinMM unavailable: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _waveIn = null;
            System.Diagnostics.Debug.WriteLine($"WaveInEvent disposal error (harmless): {ex.GetType().Name}");
        }
        try { _recognizer?.Dispose(); } catch { }
        try { _pushStream?.Dispose(); } catch { }
        _recognizer = null;
        _pushStream = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisposeWaveIn() {
        _waveIn?.Dispose();
        _waveIn = null;
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
