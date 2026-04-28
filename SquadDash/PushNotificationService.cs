using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SquadDash;

// -- Provider interface -------------------------------------------------

internal interface IPushNotificationProvider {
    Task<bool> SendAsync(string title, string message, string? tags = null);
}

// -- ntfy.sh provider ---------------------------------------------------

internal sealed class NtfyNotificationProvider : IPushNotificationProvider {
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string _topic;

    public NtfyNotificationProvider(string topic) {
        _topic = topic;
    }

    public async Task<bool> SendAsync(string title, string message, string? tags = null) {
        try {
            var url = $"https://ntfy.sh/{Uri.EscapeDataString(_topic)}";
            var req = new HttpRequestMessage(HttpMethod.Post, url) {
                Content = new StringContent(message, Encoding.UTF8, "text/plain")
            };
            req.Headers.TryAddWithoutValidation("Title", title);
            if (!string.IsNullOrWhiteSpace(tags))
                req.Headers.TryAddWithoutValidation("Tags", tags);
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Notifications", $"NtfyNotificationProvider.SendAsync failed: {ex.Message}");
            return false;
        }
    }
}

// -- Rate limiter --------------------------------------------------------

internal sealed class NotificationRateLimiter {
    private static readonly TimeSpan[] _ladder = [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(1),
    ];
    private const int DigestThreshold = 3;
    private readonly ConcurrentDictionary<string, EventState> _events = new();
    private readonly object _lock = new();
    private readonly Queue<DateTimeOffset> _recent = new();
    private int _level = 0;
    private DateTimeOffset _resetEligibleAt = DateTimeOffset.MinValue;

    internal sealed record Decision(bool ShouldSend, bool IsDigest, string? DigestMessage);

    private sealed class EventState {
        public DateTimeOffset LastSentAt { get; set; } = DateTimeOffset.MinValue;
        public int Suppressed { get; set; }
    }

    public Decision Evaluate(string eventName) {
        var now = DateTimeOffset.UtcNow;
        lock (_lock) {
            var window = _ladder[Math.Min(_level, _ladder.Length - 1)];
            while (_recent.Count > 0 && now - _recent.Peek() > window)
                _recent.Dequeue();
            int recentInMin = 0;
            foreach (var ts in _recent)
                if (now - ts <= TimeSpan.FromSeconds(60)) recentInMin++;
            if (recentInMin > DigestThreshold && _level < _ladder.Length - 1) {
                _level++;
                _resetEligibleAt = now + window;
            }
            if (_level > 0 && now >= _resetEligibleAt && recentInMin <= 1) {
                _level--;
                _resetEligibleAt = now + _ladder[_level];
            }
            var state = _events.GetOrAdd(eventName, _ => new EventState());
            var interval = _ladder[Math.Min(_level, _ladder.Length - 1)];
            if (now - state.LastSentAt < interval) {
                state.Suppressed++;
                return new Decision(false, false, null);
            }
            bool isDigest = _level > 0 && state.Suppressed > 0;
            string? msg = isDigest ? $"{state.Suppressed + 1} {eventName} events" : null;
            state.LastSentAt = now;
            state.Suppressed = 0;
            _recent.Enqueue(now);
            return new Decision(true, isDigest, msg);
        }
    }
}

// -- Orchestrator --------------------------------------------------------

internal sealed class PushNotificationService {
    private static readonly IReadOnlyDictionary<string, bool> _defaults =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["assistant_turn_complete"]    = true,
            ["loop_stopped"]              = true,
            ["rc_connection_dropped"]     = true,
            ["git_commit_pushed"]         = false,
            ["loop_iteration_complete"]   = false,
            ["rc_connection_established"] = false,
        };

    private readonly ApplicationSettingsStore _store;
    private readonly NotificationRateLimiter _rateLimiter = new();
    private IPushNotificationProvider? _provider;
    private ApplicationSettingsSnapshot _settings;

    public PushNotificationService(ApplicationSettingsStore store) {
        _store = store;
        _settings = store.Load();
        _provider = Build(_settings);
    }

    public void ReloadProvider() {
        _settings = _store.Load();
        _provider = Build(_settings);
    }

    public async Task NotifyEventAsync(string eventName, string title, string message) {
        try {
            var provider = _provider;
            if (provider is null) return;
            var toggles = _settings.NotificationEventToggles;
            bool enabled = toggles is not null && toggles.TryGetValue(eventName, out var t)
                ? t
                : _defaults.TryGetValue(eventName, out var d) && d;
            if (!enabled) return;
            var dec = _rateLimiter.Evaluate(eventName);
            if (!dec.ShouldSend) {
                SquadDashTrace.Write("Notifications", $"Rate-limited: {eventName}");
                return;
            }
            var msg = dec.IsDigest ? dec.DigestMessage! : message;
            var ok = await provider.SendAsync(title, msg).ConfigureAwait(false);
            SquadDashTrace.Write("Notifications", $"{(ok ? "Sent" : "Failed")}: {eventName}");
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Notifications", $"NotifyEventAsync: {ex.Message}");
        }
    }

    private static IPushNotificationProvider? Build(ApplicationSettingsSnapshot s) {
        if (string.IsNullOrWhiteSpace(s.NotificationProvider)) return null;
        if (string.Equals(s.NotificationProvider, "ntfy", StringComparison.OrdinalIgnoreCase)) {
            var topic = Environment.GetEnvironmentVariable("SQUADASH_NTFY_TOPIC");
            if (string.IsNullOrWhiteSpace(topic))
                s.NotificationEndpoint?.TryGetValue("topic", out topic);
            if (string.IsNullOrWhiteSpace(topic)) {
                SquadDashTrace.Write("Notifications", "ntfy: no topic configured");
                return null;
            }
            return new NtfyNotificationProvider(topic!);
        }
        SquadDashTrace.Write("Notifications", $"Unknown provider: {s.NotificationProvider}");
        return null;
    }

    private static readonly Regex _rx = new(
        @"{\s*""notification""\s*:\s*""((?:[^""\\]|\\.)*)""\s*}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string? ExtractNotificationJson(string responseText) {
        if (string.IsNullOrEmpty(responseText)) return null;
        var m = _rx.Match(responseText);
        if (!m.Success) return null;
        var raw = m.Groups[1].Value;
        try { return JsonSerializer.Deserialize<string>($"\"{raw}\""); }
        catch { return raw; }
    }
}