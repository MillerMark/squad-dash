using System;
using System.IO;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Identifies the application event that triggers a notification sound.
/// </summary>
internal enum SoundEvent
{
    PromptComplete,
    PromptError,
    ApprovalNeeded,
    QueueEmpty,
    LoopIterationComplete,
    LoopStopped,
    CommitMade,
    QuickRepliesShown
}

/// <summary>
/// Plays per-event notification sounds according to the current settings.
/// Safe to call from any thread. Play is fire-and-forget.
/// </summary>
internal sealed class SoundNotificationService
{
    private readonly ApplicationSettingsStore _settingsStore;

    public SoundNotificationService(ApplicationSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Plays the sound for the given event if the event is enabled in settings.
    /// Uses a custom audio file when configured and the file exists; falls back
    /// to <see cref="SystemSounds.Asterisk"/> otherwise.
    /// Never blocks the caller — exceptions are swallowed and debug-logged.
    /// </summary>
    public void Play(SoundEvent evt)
    {
        try
        {
            var settings = _settingsStore.Load();
            var (enabled, customPath) = GetEventSettings(settings, evt);

            if (!enabled)
                return;

            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                // MediaPlayer must run on a thread that has a Dispatcher.
                // BeginInvoke is fire-and-forget — never blocks the caller.
                var capturedPath = customPath;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var player = new MediaPlayer();
                        player.Open(new Uri(capturedPath, UriKind.Absolute));
                        player.Play();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"SoundNotificationService: MediaPlayer error for {evt}: {ex.Message}");
                    }
                });
            }
            else
            {
                // System sound: plays synchronously but is nearly instantaneous.
                SystemSounds.Asterisk.Play();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SoundNotificationService: Play error for {evt}: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static (bool Enabled, string CustomPath) GetEventSettings(
        ApplicationSettingsSnapshot s, SoundEvent evt) =>
        evt switch
        {
            SoundEvent.PromptComplete        => (s.Sound_PromptComplete_Enabled,        s.Sound_PromptComplete_CustomPath),
            SoundEvent.PromptError           => (s.Sound_PromptError_Enabled,           s.Sound_PromptError_CustomPath),
            SoundEvent.ApprovalNeeded        => (s.Sound_ApprovalNeeded_Enabled,        s.Sound_ApprovalNeeded_CustomPath),
            SoundEvent.QueueEmpty            => (s.Sound_QueueEmpty_Enabled,            s.Sound_QueueEmpty_CustomPath),
            SoundEvent.LoopIterationComplete => (s.Sound_LoopIterationComplete_Enabled, s.Sound_LoopIterationComplete_CustomPath),
            SoundEvent.LoopStopped           => (s.Sound_LoopStopped_Enabled,           s.Sound_LoopStopped_CustomPath),
            SoundEvent.CommitMade            => (s.Sound_CommitMade_Enabled,            s.Sound_CommitMade_CustomPath),
            SoundEvent.QuickRepliesShown     => (s.Sound_QuickRepliesShown_Enabled,     s.Sound_QuickRepliesShown_CustomPath),
            _                                => (false, "")
        };
}
