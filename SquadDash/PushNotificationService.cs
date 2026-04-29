using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal interface IPushNotificationProvider {
    Task<bool> SendAsync(string title, string message, string? tags = null);
}

internal sealed class NtfyNotificationProvider : IPushNotificationProvider {
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string _topic;

    public NtfyNotificationProvider(string topic) {
        _topic = topic;
    }

    public async Task<bool> SendAsync(string title, string message, string? tags = null) {
        try {
            var url = $"https://ntfy.sh/{Uri.EscapeDataString(_topic)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Title", title);
            if (!string.IsNullOrWhiteSpace(tags)) {
                request.Headers.Add("Tags", tags);
            }
            request.Content = new StringContent(message, Encoding.UTF8, "text/plain");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Notifications", $"Failed to send ntfy notification: {ex.Message}");
            return false;
        }
    }
}

internal sealed class RateLimiterDecision {
    public bool ShouldSend { get; init; }
    public bool IsDigest { get; init; }
    public string? DigestMessage { get; init; }
}

internal sealed class RateLimiterState {
    public DateTimeOffset LastSent { get; set; }
    public int CountInWindow { get; set; }
}

internal sealed class NotificationRateLimiter {
    private readonly ConcurrentDictionary<string, RateLimiterState> _eventTypeTimestamps = new();
    private readonly ConcurrentDictionary<long, int> _minuteWindows = new();
    private readonly object _escalationLock = new();
    private TimeSpan _currentDigestInterval = TimeSpan.FromMinutes(1);
    private DateTimeOffset _lastDigestSent = DateTimeOffset.MinValue;
    private int _pendingDigestCount = 0;
    private DateTimeOffset _lastEscalationCheck = DateTimeOffset.MinValue;

    public RateLimiterDecision ShouldSend(string eventType) {
        var now = DateTimeOffset.UtcNow;

        // Track per-minute rate
        var currentMinute = now.Ticks / TimeSpan.TicksPerMinute;
        _minuteWindows.AddOrUpdate(currentMinute, 1, (_, count) => count + 1);

        // Clean old minute windows
        foreach (var key in _minuteWindows.Keys.ToArray()) {
            if (key < currentMinute - 2) {
                _minuteWindows.TryRemove(key, out _);
            }
        }

        // Calculate events per minute
        var eventsInLastMinute = _minuteWindows.TryGetValue(currentMinute, out var count) ? count : 0;
        if (currentMinute > 0 && _minuteWindows.TryGetValue(currentMinute - 1, out var prevCount)) {
            eventsInLastMinute += prevCount;
        }

        lock (_escalationLock) {
            // Check escalation thresholds
            if (eventsInLastMinute > 3 && (now - _lastEscalationCheck).TotalMinutes >= 1) {
                _lastEscalationCheck = now;

                // Escalate digest interval if still exceeding rate
                if (_currentDigestInterval == TimeSpan.FromMinutes(1) && eventsInLastMinute > 5) {
                    _currentDigestInterval = TimeSpan.FromMinutes(10);
                    SquadDashTrace.Write("Notifications", "Escalating digest interval to 10 minutes");
                }
                else if (_currentDigestInterval == TimeSpan.FromMinutes(10) && eventsInLastMinute > 5) {
                    _currentDigestInterval = TimeSpan.FromHours(1);
                    SquadDashTrace.Write("Notifications", "Escalating digest interval to 1 hour");
                }
                else if (_currentDigestInterval == TimeSpan.FromHours(1) && eventsInLastMinute > 5) {
                    _currentDigestInterval = TimeSpan.FromHours(24);
                    SquadDashTrace.Write("Notifications", "Escalating digest interval to 24 hours");
                }
            }
            else if (eventsInLastMinute <= 2 && (now - _lastEscalationCheck).TotalMinutes >= 1) {
                // Reset escalation if traffic drops
                if (_currentDigestInterval != TimeSpan.FromMinutes(1)) {
                    _currentDigestInterval = TimeSpan.FromMinutes(1);
                    _lastEscalationCheck = now;
                    SquadDashTrace.Write("Notifications", "Resetting digest interval to 1 minute");
                }
            }

            // Handle digest mode
            if (eventsInLastMinute > 3) {
                _pendingDigestCount++;

                if ((now - _lastDigestSent) >= _currentDigestInterval) {
                    _lastDigestSent = now;
                    var digestMsg = $"{_pendingDigestCount} events in the last {FormatInterval(_currentDigestInterval)}";
                    _pendingDigestCount = 0;
                    return new RateLimiterDecision {
                        ShouldSend = true,
                        IsDigest = true,
                        DigestMessage = digestMsg
                    };
                }

                return new RateLimiterDecision { ShouldSend = false };
            }
        }

        // Normal mode: per-event-type throttling
        var state = _eventTypeTimestamps.GetOrAdd(eventType, _ => new RateLimiterState { LastSent = DateTimeOffset.MinValue });

        lock (state) {
            if ((now - state.LastSent).TotalSeconds < 10) {
                return new RateLimiterDecision { ShouldSend = false };
            }

            state.LastSent = now;
            state.CountInWindow++;
        }

        return new RateLimiterDecision { ShouldSend = true };
    }

    private static string FormatInterval(TimeSpan interval) {
        if (interval.TotalDays >= 1) return "day";
        if (interval.TotalHours >= 1) return "hour";
        if (interval.TotalMinutes >= 10) return "10 minutes";
        return "minute";
    }
}

internal sealed class PushNotificationService {
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly NotificationRateLimiter _rateLimiter = new();
    private IPushNotificationProvider? _provider;

    private static readonly IReadOnlyDictionary<string, bool> DefaultEventToggles = new Dictionary<string, bool> {
        ["assistant_turn_complete"] = true,
        ["loop_stopped"] = true,
        ["rc_connection_dropped"] = true,
        ["git_commit_pushed"] = false,
        ["loop_iteration_complete"] = false,
        ["rc_connection_established"] = false,
        ["quick_reply_needed"] = true
    };

    public PushNotificationService(ApplicationSettingsStore settingsStore) {
        _settingsStore = settingsStore;
        ReloadProvider();
    }

    public void ReloadProvider() {
        try {
            var settings = _settingsStore.Load();

            // Check environment variable override
            var ntfyTopic = Environment.GetEnvironmentVariable("SQUADASH_NTFY_TOPIC");

            if (!string.IsNullOrWhiteSpace(ntfyTopic)) {
                _provider = new NtfyNotificationProvider(ntfyTopic);
                SquadDashTrace.Write("Notifications", $"Loaded ntfy provider from env var: {ntfyTopic}");
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.NotificationProvider)) {
                _provider = null;
                return;
            }

            if (settings.NotificationProvider.Equals("ntfy", StringComparison.OrdinalIgnoreCase)) {
                if (settings.NotificationEndpoint?.TryGetValue("topic", out var topic) == true
                    && !string.IsNullOrWhiteSpace(topic)) {
                    _provider = new NtfyNotificationProvider(topic);
                    SquadDashTrace.Write("Notifications", $"Loaded ntfy provider: {topic}");
                }
                else {
                    _provider = null;
                    SquadDashTrace.Write("Notifications", "ntfy provider configured but no topic specified");
                }
            }
            else {
                _provider = null;
                SquadDashTrace.Write("Notifications", $"Unknown notification provider: {settings.NotificationProvider}");
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Notifications", $"Failed to reload provider: {ex.Message}");
            _provider = null;
        }
    }

    public async Task NotifyEventAsync(string eventName, string title, string message) {
        try {
            if (_provider == null) {
                return;
            }

            var settings = _settingsStore.Load();
            var eventToggles = settings.NotificationEventToggles ?? DefaultEventToggles;

            // Check if event is enabled (default to the DefaultEventToggles value, or false if not in defaults)
            var isEnabled = eventToggles.TryGetValue(eventName, out var enabled)
                ? enabled
                : (DefaultEventToggles.TryGetValue(eventName, out var defaultEnabled) && defaultEnabled);

            if (!isEnabled) {
                return;
            }

            var decision = _rateLimiter.ShouldSend(eventName);
            if (!decision.ShouldSend) {
                return;
            }

            var finalMessage = decision.IsDigest && decision.DigestMessage != null
                ? decision.DigestMessage
                : message;

            await _provider.SendAsync(title, finalMessage).ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Notifications", $"NotifyEventAsync failed for {eventName}: {ex.Message}");
        }
    }

    internal static string? ExtractNotificationJson(string? responseText) {
        if (string.IsNullOrWhiteSpace(responseText)) {
            return null;
        }

        try {
            // Match {"notification": "..."} with case-insensitive key
            var pattern = @"\{\s*""notification""\s*:\s*""([^""]*)""\s*\}";
            var match = Regex.Match(responseText, pattern, RegexOptions.IgnoreCase);

            if (match.Success && match.Groups.Count > 1) {
                return match.Groups[1].Value;
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Notifications", $"ExtractNotificationJson failed: {ex.Message}");
        }

        return null;
    }

    // Returns a best-effort summary of the prompt by stripping common stop words
    // and joining the first handful of meaningful tokens. Used as a fallback when
    // the AI response did not include a {"notification": "..."} summary.
    internal static string? BuildFallbackSummary(string? prompt) {
        if (string.IsNullOrWhiteSpace(prompt))
            return null;

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "up", "as", "is", "are", "was", "were",
            "be", "been", "being", "have", "has", "had", "do", "does", "did",
            "will", "would", "could", "should", "may", "might", "shall", "can",
            "that", "this", "these", "those", "it", "its", "i", "you", "we",
            "they", "he", "she", "my", "your", "our", "their", "his", "her",
            "me", "us", "him", "them", "not", "no", "so", "if", "then", "than",
            "just", "also", "even", "very", "some", "any", "all", "each", "both",
            "uh", "um", "like", "about", "into", "out", "when", "what",
            "which", "who", "how", "where", "want", "need", "make", "get", "go",
            "there", "here", "now", "ve", "re", "ll", "d", "m", "s",
            "probably", "actually", "basically", "really", "quite", "rather",
            "irregardless", "regardless", "something", "everything", "anything"
        };

        var words = Regex.Split(prompt.Trim(), @"[^a-zA-Z0-9'-]+")
            .Where(w => w.Length >= 2 && !stopWords.Contains(w))
            .Take(7)
            .ToArray();

        if (words.Length == 0)
            return null;

        var summary = string.Join(" ", words);
        return char.ToUpperInvariant(summary[0]) + summary[1..];
    }
}
