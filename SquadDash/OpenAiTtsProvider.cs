using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SquadDash;

internal sealed class OpenAiTtsProvider : ITtsProvider
{
    private static readonly HttpClient s_http = new();

    private readonly byte[] _encryptedApiKey;
    private readonly string _voice;
    private readonly string _model;

    public OpenAiTtsProvider(string apiKey, string voice, string model)
    {
        _encryptedApiKey = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser);
        _voice  = voice;
        _model  = model;
    }

    public async Task SpeakAsync(string phrase, CancellationToken ct = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _model,
                input = phrase,
                voice = _voice
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
            string apiKey = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(_encryptedApiKey, null, DataProtectionScope.CurrentUser));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await s_http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var mp3Bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

            // Write to a temp .mp3 file and play via MediaPlayer on the UI thread.
            var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".mp3");
            await File.WriteAllBytesAsync(tempPath, mp3Bytes, ct).ConfigureAwait(false);

            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var p = new MediaPlayer();
                    p.Open(new Uri(tempPath));
                    p.Play();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"OpenAiTtsProvider: MediaPlayer error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenAiTtsProvider: SpeakAsync error: {ex.Message}");
        }
    }
}
