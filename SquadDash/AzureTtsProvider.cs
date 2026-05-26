using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace SquadDash;

internal sealed class AzureTtsProvider : ITtsProvider
{
    private readonly byte[] _encryptedSubscriptionKey;
    private readonly string _region;
    private readonly string _voiceName;

    public AzureTtsProvider(string subscriptionKey, string region, string voiceName)
    {
        _encryptedSubscriptionKey = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(subscriptionKey), null, DataProtectionScope.CurrentUser);
        _region    = region;
        _voiceName = voiceName;
    }

    public async Task SpeakAsync(string phrase, CancellationToken ct = default)
    {
        try
        {
            string subscriptionKey = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(_encryptedSubscriptionKey, null, DataProtectionScope.CurrentUser));
            var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, _region);
            speechConfig.SpeechSynthesisVoiceName = _voiceName;

            using var audioConfig  = AudioConfig.FromDefaultSpeakerOutput();
            using var synthesizer  = new SpeechSynthesizer(speechConfig, audioConfig);

            await synthesizer.SpeakTextAsync(phrase).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AzureTtsProvider: SpeakAsync error: {ex.Message}");
        }
    }
}
