using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace SquadDash;

/// <summary>
/// Speech recognition via OpenAI Whisper API (/v1/audio/transcriptions).
/// Buffers microphone audio in 5-second chunks and submits each to the REST API.
/// Note: phrase hints (team name boosting) are silently ignored — Whisper has no grammar API.
/// Note: higher latency than Azure due to batch-oriented API.
/// </summary>
internal sealed class WhisperSpeechRecognitionService : ISpeechRecognitionService
{
    private static readonly HttpClient _http = new();

    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private byte[]? _encryptedApiKey;
    private string? _language;
    private CancellationTokenSource? _cts;
    private volatile bool _stopping;

    private const int SampleRate = 16000;
    private const int Bits = 16;
    private const int Channels = 1;
    private const int ChunkSeconds = 5;

    public event EventHandler<string>? PhraseRecognized;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<string>? RecognitionError;

    public Task StartAsync(string apiKey, string regionOrEndpoint, IEnumerable<string>? phraseHints = null, string? language = null)
    {
        _stopping = false;
        _encryptedApiKey = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser);
        _language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        _cts = new CancellationTokenSource();

        ResetBuffer();

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, Bits, Channels),
            BufferMilliseconds = 80
        };
        _waveIn.DataAvailable += OnAudioData;
        _waveIn.StartRecording();

        _ = ChunkLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _cts?.Cancel();
        try { _waveIn?.StopRecording(); } catch { }
        await FlushBufferAsync().ConfigureAwait(false);
    }

    public void WriteAudioData(byte[] buffer, int count)
    {
        // RC streaming not supported for Whisper — no-op.
    }

    public void Dispose()
    {
        _stopping = true;
        _cts?.Cancel();
        try { _waveIn?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _buffer?.Dispose(); } catch { }
        _waveIn = null;
        _writer = null;
        _buffer = null;
    }

    private void ResetBuffer()
    {
        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(_buffer, new WaveFormat(SampleRate, Bits, Channels));
    }

    private void OnAudioData(object? sender, WaveInEventArgs e)
    {
        if (_stopping || e.BytesRecorded == 0) return;

        // Volume metering
        var sampleCount = e.BytesRecorded / 2;
        double sumOfSquares = 0;
        for (var i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            sumOfSquares += (double)sample * sample;
        }
        var rms = Math.Sqrt(sumOfSquares / sampleCount);
        VolumeChanged?.Invoke(this, Math.Min(1.0, rms / 6000.0));

        lock (this)
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private async Task ChunkLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(ChunkSeconds), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            await FlushBufferAsync().ConfigureAwait(false);
        }
    }

    private async Task FlushBufferAsync()
    {
        byte[] wavBytes;
        lock (this)
        {
            _writer?.Flush();
            wavBytes = _buffer?.ToArray() ?? Array.Empty<byte>();
            ResetBuffer();
        }

        // Skip if buffer is essentially empty (just WAV header ~44 bytes)
        if (wavBytes.Length < 100) return;

        try
        {
            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavBytes);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");
            content.Add(new StringContent("whisper-1"), "model");

            // Pass language hint if configured; Whisper uses ISO-639-1 (e.g. "fr" from "fr-FR").
            if (_language is not null)
            {
                var iso = _language.Contains('-') ? _language[.._language.IndexOf('-')] : _language;
                content.Add(new StringContent(iso.ToLowerInvariant()), "language");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
            string apiKey = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(_encryptedApiKey!, null, DataProtectionScope.CurrentUser));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = content;

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                RecognitionError?.Invoke(this, $"Whisper API error {(int)response.StatusCode}: {err}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    PhraseRecognized?.Invoke(this, text.Trim());
            }
        }
        catch (Exception ex) when (!_stopping)
        {
            RecognitionError?.Invoke(this, ex.Message);
        }
    }
}
