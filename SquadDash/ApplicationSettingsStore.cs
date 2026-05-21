using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Collections.Generic;

namespace SquadDash;

internal sealed class ApplicationSettingsStore {
    private const int MaxRecentFolders = 12;
    private const string MutexName = @"Local\SquadDash.ApplicationSettings";
    private readonly string _settingsPath;

    public ApplicationSettingsStore() {
        var settingsDirectory = SquadDashPaths.AppData;
        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    internal ApplicationSettingsStore(string settingsPath) {
        if (string.IsNullOrWhiteSpace(settingsPath))
            throw new ArgumentException("Settings path cannot be empty.", nameof(settingsPath));

        var settingsDirectory = Path.GetDirectoryName(Path.GetFullPath(settingsPath))
            ?? throw new DirectoryNotFoundException("Could not determine the settings directory.");
        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public ApplicationSettingsSnapshot Load() {
        using var mutex = AcquireMutex();
        var snapshot = JsonFileStorage.ReadOrDefault<ApplicationSettingsSnapshot>(_settingsPath, null!);
        return snapshot?.Normalize() ?? ApplicationSettingsSnapshot.Empty.Normalize();
    }

    public ApplicationSettingsSnapshot RememberFolder(string folderPath) {
        using var mutex = AcquireMutex();

        var normalizedFolder = NormalizeFolder(folderPath);
        var current = LoadCore();
        var recentFolders = new List<string> { normalizedFolder };
        recentFolders.AddRange(
            current.RecentFolders.Where(path => !PathsEqual(path, normalizedFolder)));

        var updated = current with {
            LastOpenedFolder = normalizedFolder,
            RecentFolders = recentFolders.Take(MaxRecentFolders).ToArray()
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveAgentAccentColor(
        string workspaceFolder,
        string agentName,
        string accentColorHex) {
        using var mutex = AcquireMutex();

        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var workspaceColors = current.AgentAccentColorsByWorkspace
            .ToDictionary(
                entry => entry.Key,
                entry => new Dictionary<string, string>(entry.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        if (!workspaceColors.TryGetValue(normalizedWorkspace, out var agentColors)) {
            agentColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            workspaceColors[normalizedWorkspace] = agentColors;
        }
        else {
            agentColors = new Dictionary<string, string>(agentColors, StringComparer.OrdinalIgnoreCase);
            workspaceColors[normalizedWorkspace] = agentColors;
        }

        agentColors[agentName] = NormalizeColorHex(accentColorHex);
        var readOnlyWorkspaceColors = workspaceColors.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<string, string>)entry.Value,
            StringComparer.OrdinalIgnoreCase);

        var updated = current with {
            AgentAccentColorsByWorkspace = readOnlyWorkspaceColors
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveWindowPlacement(
        string workspaceFolder,
        WorkspaceWindowPlacement placement) {
        using var mutex = AcquireMutex();

        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var placements = current.WindowPlacementByWorkspace
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);

        placements[normalizedWorkspace] = placement.Normalize();

        var updated = current with { WindowPlacementByWorkspace = placements };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SavePromptFontSize(double promptFontSize) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { PromptFontSize = NormalizeFontSize(promptFontSize) };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveTranscriptFontSize(double transcriptFontSize) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { TranscriptFontSize = NormalizeFontSize(transcriptFontSize) };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveDocSourceFontSize(double docSourceFontSize) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { DocSourceFontSize = NormalizeFontSize(docSourceFontSize) };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveAgentImagePath(
        string workspaceFolder,
        string agentKey,
        string? imagePath) {
        using var mutex = AcquireMutex();

        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var workspaceImages = current.AgentImagePathsByWorkspace
            .ToDictionary(
                entry => entry.Key,
                entry => new Dictionary<string, string>(entry.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        if (!workspaceImages.TryGetValue(normalizedWorkspace, out var agentImages)) {
            agentImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            workspaceImages[normalizedWorkspace] = agentImages;
        }
        else {
            agentImages = new Dictionary<string, string>(agentImages, StringComparer.OrdinalIgnoreCase);
            workspaceImages[normalizedWorkspace] = agentImages;
        }

        if (string.IsNullOrWhiteSpace(imagePath))
            agentImages.Remove(agentKey);
        else
            agentImages[agentKey] = imagePath.Trim();

        var readOnlyWorkspaceImages = workspaceImages.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<string, string>)entry.Value,
            StringComparer.OrdinalIgnoreCase);

        var updated = current with { AgentImagePathsByWorkspace = readOnlyWorkspaceImages };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveIgnoredRoutingIssueFingerprint(
        string workspaceFolder,
        string? fingerprint) {
        using var mutex = AcquireMutex();

        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var ignoredFingerprints = current.IgnoredRoutingIssueFingerprintsByWorkspace
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(fingerprint))
            ignoredFingerprints.Remove(normalizedWorkspace);
        else
            ignoredFingerprints[normalizedWorkspace] = fingerprint.Trim();

        var updated = current with {
            IgnoredRoutingIssueFingerprintsByWorkspace = ignoredFingerprints
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveUserName(string? userName) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with {
            UserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim()
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveCleanupPrompt(string? prompt) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            CleanupPrompt = string.IsNullOrWhiteSpace(prompt)
                ? "Clean up and clarify this text."
                : prompt.Trim()
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveSpeechRegion(string? region) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with {
            SpeechRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim()
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SavePttAutoSend(bool value) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with { PttAutoSend = value };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveVoiceReplacementRules(IEnumerable<VoiceReplacementRule> rules) {
        using var mutex = AcquireMutex();
        var list = rules.Where(r => !string.IsNullOrWhiteSpace(r.Pattern)).ToList();
        var current = LoadCore();
        var updated = current with { VoiceReplacementRules = list.AsReadOnly() };
        SaveCore(updated);
        return updated.Normalize();
    }

    public ApplicationSettingsSnapshot SaveSpeechProvider(SpeechProvider provider, string? openAiKey) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            SpeechProvider = provider,
            OpenAiSpeechApiKey = string.IsNullOrWhiteSpace(openAiKey) ? null : openAiKey.Trim()
        };
        SaveCore(updated);
        return updated.Normalize();
    }

    public ApplicationSettingsSnapshot SaveSpeechLanguage(string? language) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            SpeechLanguage = string.IsNullOrWhiteSpace(language) ? null : language.Trim()
        };
        SaveCore(updated);
        return updated.Normalize();
    }

    public ApplicationSettingsSnapshot SavePreferencesLastPage(int page) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with { Preferences_LastPage = Math.Max(0, page) };
        SaveCore(updated);
        return updated.Normalize();
    }

    public ApplicationSettingsSnapshot SaveFontSizeScaleLevel(int level) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with { FontSizeScaleLevel = Math.Clamp(level, 0, 6) };
        SaveCore(updated);
        return updated.Normalize();
    }

    public ApplicationSettingsSnapshot SaveNotificationSettings(
        string? provider,
        IReadOnlyDictionary<string, string>? endpoint,
        IReadOnlyDictionary<string, bool>? eventToggles) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            NotificationProvider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim(),
            NotificationEndpoint = endpoint,
            NotificationEventToggles = eventToggles
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveTunnelSettings(string? mode, string? token) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            TunnelMode = mode is "ngrok" or "cloudflare" ? mode : null,
            TunnelToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim()
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveByokSettings(
        string? providerUrl,
        string? model,
        string? providerType,
        string? apiKey) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            ByokProviderUrl = string.IsNullOrWhiteSpace(providerUrl) ? null : providerUrl.Trim(),
            ByokModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            ByokProviderType = providerType is "openai" or "azure" or "anthropic" ? providerType : null,
            ByokApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim()
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveDeveloperIssueSimulation(
        string workspaceFolder,
        DeveloperStartupIssueSimulation startupIssueSimulation,
        DeveloperRuntimeIssueSimulation runtimeIssueSimulation) {
        using var mutex = AcquireMutex();

        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();

        var startupDict = current.StartupIssueSimulationByWorkspace
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        startupDict[normalizedWorkspace] = startupIssueSimulation;

        var runtimeDict = current.RuntimeIssueSimulationByWorkspace
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        runtimeDict[normalizedWorkspace] = runtimeIssueSimulation;

        var updated = current with {
            StartupIssueSimulationByWorkspace = startupDict,
            RuntimeIssueSimulationByWorkspace = runtimeDict
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveTheme(string theme) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            Theme = theme is "Light" or "Dark" or "Auto" ? theme : null
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveWorkspaceTintStop(string workspaceFolder, int tintStop) {
        using var mutex = AcquireMutex();
        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var stops = current.TintStopByWorkspace
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        stops[normalizedWorkspace] = Math.Clamp(tintStop, 0, 7);
        var updated = current with { TintStopByWorkspace = stops };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveWorkspaceAccentHueOffset(string workspaceFolder, int offsetDegrees) {
        using var mutex = AcquireMutex();
        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var offsets = current.AccentHueOffsetByWorkspace
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        offsets[normalizedWorkspace] = Math.Clamp(offsetDegrees, -180, 180);
        var updated = current with { AccentHueOffsetByWorkspace = offsets };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveLastUsedModel(string model) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            LastUsedModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim()
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveUtilityWindowState(bool tasksWindowOpen, bool traceWindowOpen, bool approvalWindowOpen = false) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            TasksWindowOpen    = tasksWindowOpen,
            TraceWindowOpen    = traceWindowOpen,
            ApprovalWindowOpen = approvalWindowOpen
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveDisabledTraceCategories(IReadOnlyList<TraceCategory> disabled) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            DisabledTraceCategories = disabled.Select(c => c.ToString()).ToArray()
        };
        SaveCore(updated);
        return updated.Normalize();
    }

    public ApplicationSettingsSnapshot SaveTranscriptViewMode(
        string workspaceFolder,
        TranscriptViewMode mode) {
        using var mutex = AcquireMutex();

        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var modes = current.TranscriptViewModeByWorkspace
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);

        modes[normalizedWorkspace] = mode == TranscriptViewMode.Multi ? "multi" : "single";

        var updated = current with { TranscriptViewModeByWorkspace = modes };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveLoopMode(LoopMode mode) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { LoopMode = mode };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveLoopContinuousContext(bool continuous) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { LoopContinuousContext = continuous };

        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Saves whether the native loop was active at the time of the call.
    /// Used to auto-resume the loop on the next startup if it was running when the app exited.
    /// When <paramref name="active"/> is false, also resets <see cref="ApplicationSettingsSnapshot.LoopLastIteration"/> to 0.
    /// </summary>
    public ApplicationSettingsSnapshot SaveLoopActive(bool active) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = active
            ? current with { LoopActiveOnExit = true }
            : current with { LoopActiveOnExit = false, LoopLastIteration = 0 };

        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Persists the most recently completed loop iteration number so it can be
    /// restored when the loop auto-resumes after a restart.
    /// </summary>
    public ApplicationSettingsSnapshot SaveLoopIteration(int iteration) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { LoopLastIteration = iteration };

        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Saves whether Remote Access was active at the time of the call.
    /// Used to auto-resume RC on the next startup if it was running when the app exited.
    /// </summary>
    public ApplicationSettingsSnapshot SaveRemoteAccessActive(bool active) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { RemoteAccessActiveOnExit = active };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveApprovalShowApproved(bool show) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with { ApprovalShowApproved = show };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveApprovalShowRejected(bool show) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with { ApprovalShowRejected = show };
        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Persists the RC session token so the same QR code URL stays valid across restarts.
    /// </summary>
    public ApplicationSettingsSnapshot SaveRcToken(string? token) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { RcPersistentToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim() };

        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Persists the active RC port so the same browser URL stays valid across restarts.
    /// </summary>
    public ApplicationSettingsSnapshot SaveRcPort(int port) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with { RcPersistentPort = port };

        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Saves the documentation panel as explicitly closed, capturing the current
    /// tree expansion state and selected topic for restoration on next startup.
    /// </summary>
    public ApplicationSettingsSnapshot SaveDocsPanelClosed(
        IReadOnlyList<string>? expandedNodes,
        string? selectedTopic,
        double? docsPanelWidth = null,
        double? docsTopicsWidth = null,
        double? docsPanelWidthFraction = null,
        double? docsTopicsWidthFraction = null,
        bool? docsSourceOpen = null,
        double? docsSourceWidth = null) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with {
            DocsPanelOpen    = false,
            DocsExpandedNodes = expandedNodes?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray(),
            DocsSelectedTopic = string.IsNullOrWhiteSpace(selectedTopic) ? null : selectedTopic.Trim(),
            DocsPanelWidth = docsPanelWidth,
            DocsTopicsWidth = docsTopicsWidth,
            DocsPanelWidthFraction = docsPanelWidthFraction,
            DocsTopicsWidthFraction = docsTopicsWidthFraction,
            DocsSourceOpen = docsSourceOpen,
            DocsSourceWidth = docsSourceWidth
        };

        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Records that the documentation panel is open and, if panel is open,
    /// snapshots the current tree state (expansion + selected topic).
    /// Leaves any previously saved expansion/topic intact when no current state
    /// is provided (i.e. the panel was re-opened after a startup-restore).
    /// </summary>
    public ApplicationSettingsSnapshot SaveDocsPanelOpen(
        IReadOnlyList<string>? expandedNodes = null,
        string? selectedTopic = null,
        double? docsPanelWidth = null,
        double? docsTopicsWidth = null,
        double? docsPanelWidthFraction = null,
        double? docsTopicsWidthFraction = null,
        bool? docsSourceOpen = null,
        double? docsSourceWidth = null) {
        using var mutex = AcquireMutex();

        var current = LoadCore();

        // Keep previously-saved tree state when the caller has nothing new to offer.
        var updated = current with {
            DocsPanelOpen     = null,  // null = open (absence = open)
            DocsExpandedNodes = expandedNodes is not null
                ? expandedNodes.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                : current.DocsExpandedNodes,
            DocsSelectedTopic = !string.IsNullOrWhiteSpace(selectedTopic)
                ? selectedTopic.Trim()
                : current.DocsSelectedTopic,
            DocsPanelWidth = docsPanelWidth ?? current.DocsPanelWidth,
            DocsTopicsWidth = docsTopicsWidth ?? current.DocsTopicsWidth,
            DocsPanelWidthFraction = docsPanelWidthFraction ?? current.DocsPanelWidthFraction,
            DocsTopicsWidthFraction = docsTopicsWidthFraction ?? current.DocsTopicsWidthFraction,
            DocsSourceOpen = docsSourceOpen ?? current.DocsSourceOpen,
            DocsSourceWidth = docsSourceWidth ?? current.DocsSourceWidth
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveSoundNotificationSettings(
        SoundEvent evt,
        bool enabled,
        string? customPath)
    {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var path = customPath?.Trim() ?? "";
        var updated = evt switch
        {
            SoundEvent.PromptComplete        => current with { Sound_PromptComplete_Enabled        = enabled, Sound_PromptComplete_CustomPath        = path },
            SoundEvent.PromptError           => current with { Sound_PromptError_Enabled           = enabled, Sound_PromptError_CustomPath           = path },
            SoundEvent.ApprovalNeeded        => current with { Sound_ApprovalNeeded_Enabled        = enabled, Sound_ApprovalNeeded_CustomPath        = path },
            SoundEvent.QueueEmpty            => current with { Sound_QueueEmpty_Enabled            = enabled, Sound_QueueEmpty_CustomPath            = path },
            SoundEvent.LoopIterationComplete => current with { Sound_LoopIterationComplete_Enabled = enabled, Sound_LoopIterationComplete_CustomPath = path },
            SoundEvent.LoopStopped           => current with { Sound_LoopStopped_Enabled           = enabled, Sound_LoopStopped_CustomPath           = path },
            SoundEvent.CommitMade            => current with { Sound_CommitMade_Enabled            = enabled, Sound_CommitMade_CustomPath            = path },
            SoundEvent.QuickRepliesShown     => current with { Sound_QuickRepliesShown_Enabled     = enabled, Sound_QuickRepliesShown_CustomPath     = path },
            _                               => current
        };
        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveTtsSettings(
        TtsProvider provider,
        string? azureVoice,
        string? openAiVoice,
        OpenAiTtsModel openAiModel)
    {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            Tts_Provider     = provider,
            Tts_Azure_Voice  = string.IsNullOrWhiteSpace(azureVoice)  ? "en-US-JennyNeural" : azureVoice.Trim(),
            Tts_OpenAi_Voice = string.IsNullOrWhiteSpace(openAiVoice) ? "alloy"              : openAiVoice.Trim(),
            Tts_OpenAi_Model = openAiModel,
        };
        SaveCore(updated);
        return updated.Normalize();
    }

    private ApplicationSettingsSnapshot LoadCore(){
        if (!File.Exists(_settingsPath))
            return ApplicationSettingsSnapshot.Empty.Normalize();

        try {
            var json = File.ReadAllText(_settingsPath);
            var snapshot = JsonSerializer.Deserialize<ApplicationSettingsSnapshot>(json);
            return snapshot?.Normalize() ?? ApplicationSettingsSnapshot.Empty.Normalize();
        }
        catch {
            return ApplicationSettingsSnapshot.Empty.Normalize();
        }
    }

    /// <summary>
    /// Records the shutdown timestamp for the given workspace.
    /// Called on clean shutdown so the next startup can display a session gap indicator.
    /// </summary>
    public ApplicationSettingsSnapshot SaveWorkspaceShutdownTime(string workspaceFolder, DateTimeOffset time) {
        using var mutex = AcquireMutex();
        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        var times = current.WorkspaceShutdownTimes is not null
            ? new Dictionary<string, DateTimeOffset>(current.WorkspaceShutdownTimes, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        times[normalizedWorkspace] = time;
        var updated = current with { WorkspaceShutdownTimes = times };
        SaveCore(updated);
        return updated;
    }

    /// <summary>
    /// Removes the shutdown timestamp for the given workspace after the session gap
    /// indicator has been displayed, preventing it from appearing again.
    /// </summary>
    public ApplicationSettingsSnapshot ClearWorkspaceShutdownTime(string workspaceFolder) {
        using var mutex = AcquireMutex();
        var normalizedWorkspace = NormalizeFolder(workspaceFolder);
        var current = LoadCore();
        if (current.WorkspaceShutdownTimes is null ||
            !current.WorkspaceShutdownTimes.ContainsKey(normalizedWorkspace))
            return current;
        var times = new Dictionary<string, DateTimeOffset>(current.WorkspaceShutdownTimes, StringComparer.OrdinalIgnoreCase);
        times.Remove(normalizedWorkspace);
        var updated = current with { WorkspaceShutdownTimes = times.Count > 0 ? times : null };
        SaveCore(updated);
        return updated;
    }

    private void SaveCore(ApplicationSettingsSnapshot snapshot) {
        var normalized = snapshot.Normalize();
        JsonFileStorage.AtomicWrite(_settingsPath, normalized);
    }

    private static MutexLease AcquireMutex() {
        return MutexLease.Acquire(MutexName);
    }

    private static bool PathsEqual(string left, string right) {
        return string.Equals(
            NormalizeFolder(left),
            NormalizeFolder(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolder(string folderPath) {
        return Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeColorHex(string accentColorHex) {
        return accentColorHex.Trim().ToUpperInvariant();
    }

    private static double NormalizeFontSize(double fontSize) {
        return double.IsFinite(fontSize) && fontSize > 0
            ? fontSize
            : 14;
    }

    public WorkspaceDocsPanelState GetDocsPanelState(string? workspaceFolder)
    {
        var current = LoadCore();
        if (!string.IsNullOrEmpty(workspaceFolder))
        {
            var key = NormalizeFolder(workspaceFolder);
            if (current.DocsPanelStateByWorkspace.TryGetValue(key, out var ws))
                return ws;
        }
        // Fall back to legacy global fields for backward compatibility
        return new WorkspaceDocsPanelState
        {
            Open = current.DocsPanelOpen,
            ExpandedNodes = current.DocsExpandedNodes,
            SelectedTopic = current.DocsSelectedTopic,
            PanelWidth = current.DocsPanelWidth,
            TopicsWidth = current.DocsTopicsWidth,
            PanelWidthFraction = current.DocsPanelWidthFraction,
            TopicsWidthFraction = current.DocsTopicsWidthFraction,
            SourceOpen = current.DocsSourceOpen,
            SourceWidth = current.DocsSourceWidth,
        };
    }

    public ApplicationSettingsSnapshot SaveDocsPanelState(string? workspaceFolder, WorkspaceDocsPanelState state)
    {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var dict = current.DocsPanelStateByWorkspace
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        var key = string.IsNullOrEmpty(workspaceFolder)
            ? "__default__"
            : NormalizeFolder(workspaceFolder);
        if (dict.TryGetValue(key, out var existing))
        {
            // Merge: use new value if not null, else preserve existing
            state = state with {
                Open = state.Open ?? existing.Open,
                ExpandedNodes = state.ExpandedNodes ?? existing.ExpandedNodes,
                SelectedTopic = state.SelectedTopic ?? existing.SelectedTopic,
                PanelWidth = state.PanelWidth ?? existing.PanelWidth,
                TopicsWidth = state.TopicsWidth ?? existing.TopicsWidth,
                PanelWidthFraction = state.PanelWidthFraction ?? existing.PanelWidthFraction,
                TopicsWidthFraction = state.TopicsWidthFraction ?? existing.TopicsWidthFraction,
                SourceOpen = state.SourceOpen ?? existing.SourceOpen,
                SourceWidth = state.SourceWidth ?? existing.SourceWidth,
                SourceLayoutTopBottom = state.SourceLayoutTopBottom ?? existing.SourceLayoutTopBottom,
                FullScreenTranscript = state.FullScreenTranscript ?? existing.FullScreenTranscript,
                TasksPanelVisible = state.TasksPanelVisible ?? existing.TasksPanelVisible,
                ApprovalPanelVisible = state.ApprovalPanelVisible ?? existing.ApprovalPanelVisible,
                NotesPanelVisible = state.NotesPanelVisible ?? existing.NotesPanelVisible,
                LoopPanelVisible = state.LoopPanelVisible ?? existing.LoopPanelVisible,
                MaintenancePanelVisible = state.MaintenancePanelVisible ?? existing.MaintenancePanelVisible,
                DraftFollowUpsJson = state.DraftFollowUpsJson ?? existing.DraftFollowUpsJson,
                SelectedLoopFile = state.SelectedLoopFile ?? existing.SelectedLoopFile,
                NotesSortOrder = state.NotesSortOrder ?? existing.NotesSortOrder,
                PromptPanelOnTop = state.PromptPanelOnTop ?? existing.PromptPanelOnTop,
                QueuePaused = state.QueuePaused ?? existing.QueuePaused,
                ApprovalsPanelFilter = state.ApprovalsPanelFilter ?? existing.ApprovalsPanelFilter,
                TasksPanelFilter = state.TasksPanelFilter ?? existing.TasksPanelFilter,
                NotesPanelFilter = state.NotesPanelFilter ?? existing.NotesPanelFilter,
            };
        }
        dict[key] = state;
        var updated = current with { DocsPanelStateByWorkspace = dict };
        SaveCore(updated);
        return updated.Normalize();
    }
}

/// <summary>Documentation panel layout state saved per workspace.</summary>
internal sealed record WorkspaceDocsPanelState
{
    public bool? FullScreenTranscript { get; init; }
    /// <summary>null/true = open (default). false = explicitly closed.</summary>
    public bool? Open { get; init; }
    public IReadOnlyList<string>? ExpandedNodes { get; init; }
    public string? SelectedTopic { get; init; }
    public double? PanelWidth { get; init; }
    public double? TopicsWidth { get; init; }
    public double? PanelWidthFraction { get; init; }
    public double? TopicsWidthFraction { get; init; }
    public bool? SourceOpen { get; init; }
    public double? SourceWidth { get; init; }
    /// <summary>
    /// Whether the source panel is in top-bottom layout. <c>null</c> or <c>false</c> = side-by-side (default).
    /// </summary>
    public bool? SourceLayoutTopBottom { get; init; }
    /// <summary>
    /// Whether the Tasks sidebar panel was visible. <c>null</c> or <c>false</c> = hidden (default).
    /// <c>true</c> = user had the panel open and wants it restored on next startup.
    /// </summary>
    public bool? TasksPanelVisible { get; init; }

    /// <summary>
    /// Whether the Commit Approvals inline panel was visible. <c>null</c> or <c>false</c> = hidden (default).
    /// <c>true</c> = user had the panel open and wants it restored on next startup.
    /// </summary>
    public bool? ApprovalPanelVisible { get; init; }

    /// <summary>
    /// Whether the Notes inline panel was visible. <c>null</c> or <c>false</c> = hidden (default).
    /// <c>true</c> = user had the panel open and wants it restored on next startup.
    /// </summary>
    public bool? NotesPanelVisible { get; init; }

    /// <summary>
    /// Whether the Loop inline panel was explicitly closed by the user.
    /// <c>null</c> = never changed (show by default). <c>false</c> = user closed it (hide on startup).
    /// <c>true</c> = user explicitly opened it via menu.
    /// </summary>
    public bool? LoopPanelVisible { get; init; }

    /// <summary>
    /// Whether the Maintenance inline panel was visible.
    /// <c>null</c> or <c>false</c> = hidden (default). <c>true</c> = user had the panel open.
    /// </summary>
    public bool? MaintenancePanelVisible { get; init; }

    /// <summary>
    /// Follow-up attachments on the active draft tab. Persisted so they survive restart.
    /// JSON-serialized list of <see cref="FollowUpAttachmentDto"/>. Null when no attachments are present.
    /// </summary>
    public string? DraftFollowUpsJson { get; init; }

    /// <summary>
    /// The last loop file selected in the loop panel file picker.
    /// Null means use the default (loop.md).
    /// </summary>
    public string? SelectedLoopFile { get; init; }

    /// <summary>
    /// Sort order for the Notes panel. Null = default (MostRecentOnTop).
    /// </summary>
    public NotesSortOrder? NotesSortOrder { get; init; }

    /// <summary>
    /// Whether the prompt input panel is docked above the transcript instead of below.
    /// <c>null</c> / <c>false</c> = default (below). <c>true</c> = user moved it above.
    /// </summary>
    public bool? PromptPanelOnTop { get; init; }

    /// <summary>
    /// Whether the queue was manually paused by the user.
    /// <c>null</c> / <c>false</c> = running (default). <c>true</c> = user paused it.
    /// </summary>
    public bool? QueuePaused { get; init; }

    /// <summary>
    /// Last filter text entered in the Approvals panel filter box. Null = no filter.
    /// </summary>
    public string? ApprovalsPanelFilter { get; init; }

    /// <summary>
    /// Last filter text entered in the Tasks panel filter box. Null = no filter.
    /// </summary>
    public string? TasksPanelFilter { get; init; }

    /// <summary>
    /// Last filter text entered in the Notes panel filter box. Null = no filter.
    /// </summary>
    public string? NotesPanelFilter { get; init; }

    /// <summary>
    /// Legacy single-item follow-up fields — kept for reading old settings files.
    /// New writes use <see cref="DraftFollowUpsJson"/> instead.
    /// </summary>
    public string? DraftFollowUpCommitSha { get; init; }
    public string? DraftFollowUpDescription { get; init; }
    public string? DraftFollowUpOriginalPrompt { get; init; }
}

internal sealed record ApplicationSettingsSnapshot(
    string? LastOpenedFolder,
    IReadOnlyList<string> RecentFolders,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AgentAccentColorsByWorkspace,
    IReadOnlyDictionary<string, WorkspaceWindowPlacement> WindowPlacementByWorkspace,
    double PromptFontSize,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AgentImagePathsByWorkspace,
    IReadOnlyDictionary<string, string> IgnoredRoutingIssueFingerprintsByWorkspace) {

    public string? UserName { get; init; }
    public string? SpeechRegion { get; init; }
    public SpeechProvider SpeechProvider { get; init; } = SpeechProvider.Azure;
    public string? OpenAiSpeechApiKey { get; init; }
    /// <summary>
    /// BCP-47 locale for speech recognition (e.g. "fr-FR", "de-DE").
    /// <c>null</c> means auto-detect (Azure) / auto-detect (Whisper).
    /// </summary>
    public string? SpeechLanguage { get; init; }
    public bool PttAutoSend { get; init; } = true;

    /// <summary>
    /// Prompt sent to AI when the user triggers the Quick Cleanup (Ctrl+Shift+C) command.
    /// </summary>
    public string CleanupPrompt { get; init; } = "Clean up and clarify this text.";
    public double TranscriptFontSize { get; init; } = 14;

    /// <summary>
    /// Font size for the documentation source editor (DocSourceTextBox). Global/machine-wide.
    /// </summary>
    public double DocSourceFontSize { get; init; } = 12;
    public IReadOnlyDictionary<string, DeveloperStartupIssueSimulation> StartupIssueSimulationByWorkspace { get; init; } =
        new Dictionary<string, DeveloperStartupIssueSimulation>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, DeveloperRuntimeIssueSimulation> RuntimeIssueSimulationByWorkspace { get; init; } =
        new Dictionary<string, DeveloperRuntimeIssueSimulation>(StringComparer.OrdinalIgnoreCase);

    public DeveloperStartupIssueSimulation GetStartupIssueSimulation(string workspaceFolder) {
        if (string.IsNullOrEmpty(workspaceFolder)) return DeveloperStartupIssueSimulation.None;
        var key = Path.GetFullPath(workspaceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return StartupIssueSimulationByWorkspace.TryGetValue(key, out var v) ? v : DeveloperStartupIssueSimulation.None;
    }

    public DeveloperRuntimeIssueSimulation GetRuntimeIssueSimulation(string workspaceFolder) {
        if (string.IsNullOrEmpty(workspaceFolder)) return DeveloperRuntimeIssueSimulation.None;
        var key = Path.GetFullPath(workspaceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return RuntimeIssueSimulationByWorkspace.TryGetValue(key, out var v) ? v : DeveloperRuntimeIssueSimulation.None;
    }
    public string? Theme { get; init; }
    public string? LastUsedModel { get; init; }
    public bool TasksWindowOpen { get; init; }
    public bool TraceWindowOpen { get; init; }
    public bool ApprovalWindowOpen { get; init; }

    /// <summary>
    /// Whether the documentation panel was open. <c>null</c> (absent) or <c>true</c> = open (default).
    /// Only written as <c>false</c> when the user explicitly closes the panel.
    /// </summary>
    public bool? DocsPanelOpen { get; init; }

    /// <summary>
    /// Keys of expanded <see cref="System.Windows.Controls.TreeViewItem"/> nodes in the
    /// documentation tree. Each key is the item's <c>Tag</c> file-path (if it has one)
    /// or its <c>Header</c> string. <c>null</c> means "not saved yet" → expand all.
    /// </summary>
    public IReadOnlyList<string>? DocsExpandedNodes { get; init; }

    /// <summary>
    /// Tag (file path) of the last-selected documentation topic. <c>null</c> = first item.
    /// </summary>
    public string? DocsSelectedTopic { get; init; }

    /// <summary>
    /// Width of the documentation panel column in pixels. <c>null</c> = default 600.
    /// </summary>
    public double? DocsPanelWidth { get; init; }

    /// <summary>
    /// Width of the topics column within the docs panel in pixels. <c>null</c> = default 220.
    /// </summary>
    public double? DocsTopicsWidth { get; init; }

    /// <summary>
    /// Documentation panel width as a fraction of the main grid width (0–1).
    /// Preferred over <see cref="DocsPanelWidth"/> for proportional restore.
    /// </summary>
    public double? DocsPanelWidthFraction { get; init; }

    /// <summary>
    /// Topics column width as a fraction of the docs panel width (0–1).
    /// Preferred over <see cref="DocsTopicsWidth"/> for proportional restore.
    /// </summary>
    public double? DocsTopicsWidthFraction { get; init; }

    /// <summary>
    /// Whether the "View Source" panel was open. <c>null</c> = default (closed).
    /// </summary>
    public bool? DocsSourceOpen { get; init; }

    /// <summary>
    /// Width of the source editor column in pixels when it is open.
    /// </summary>
    public double? DocsSourceWidth { get; init; }

    // ── Notifications ──────────────────────────────────────────────────────
    /// <summary>Push notification provider. "ntfy" or null (disabled).</summary>
    public string? NotificationProvider { get; init; }

    /// <summary>Endpoint config for push notifications. For ntfy: { "topic": "my-topic" }.</summary>
    public IReadOnlyDictionary<string, string>? NotificationEndpoint { get; init; }

    /// <summary>Per-event notification toggles. Keys are event names, values are on/off booleans.</summary>
    public IReadOnlyDictionary<string, bool>? NotificationEventToggles { get; init; }

    // ── Tunnel ────────────────────────────────────────────────────────────
    /// <summary>Tunnel provider for public RC access. "ngrok", "cloudflare", or null (disabled).</summary>
    public string? TunnelMode { get; init; }

    /// <summary>Auth token for the tunnel provider (ngrok authtoken or cloudflare tunnel token).</summary>
    public string? TunnelToken { get; init; }

    // ── BYOK (Bring Your Own Key) provider settings ───────────────────────
    /// <summary>Base URL for a custom OpenAI-compatible model provider (e.g. a local Ollama instance).</summary>
    public string? ByokProviderUrl { get; init; }

    /// <summary>Model name to use with the custom provider (e.g. "qwen3-coder:30b").</summary>
    public string? ByokModel { get; init; }

    /// <summary>Provider type: "openai", "azure", "anthropic", or null (default Copilot).</summary>
    public string? ByokProviderType { get; init; }

    /// <summary>API key for the custom provider. Sensitive — not logged.</summary>
    public string? ByokApiKey { get; init; }

    /// <summary>
    /// Ordered list of regex find/replace rules applied to every voice phrase
    /// before it is inserted at the cursor. Rules are applied in order.
    /// </summary>
    public IReadOnlyList<VoiceReplacementRule> VoiceReplacementRules { get; init; } =
        Array.Empty<VoiceReplacementRule>();

    /// <summary>
    /// Names of <see cref="TraceCategory"/> values that should be suppressed in
    /// the live trace window.  Stored as strings so the JSON round-trips cleanly
    /// if new enum members are added later.
    /// <para>
    /// <c>null</c> means the property was never explicitly saved (e.g. first launch
    /// or an older settings file).  <see cref="Normalize"/> treats <c>null</c> as
    /// "all categories disabled" so new users see a quiet trace window by default.
    /// An empty array means the user has explicitly enabled every category.
    /// </para>
    /// </summary>
    public IReadOnlyList<string>? DisabledTraceCategories { get; init; } = null;

    /// <summary>
    /// Efficient lookup set built from <see cref="DisabledTraceCategories"/> during
    /// <see cref="Normalize"/>.  Not persisted — recomputed on every load.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<TraceCategory> DisabledTraceCategorySet { get; init; } = new HashSet<TraceCategory>();

    /// <summary>
    /// Per-workspace transcript view mode.  Values are <c>"single"</c> or <c>"multi"</c>.
    /// Keyed by the normalised workspace folder path (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, string> TranscriptViewModeByWorkspace { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-workspace documentation panel state. Keyed by normalised workspace folder path.
    /// </summary>
    public IReadOnlyDictionary<string, WorkspaceDocsPanelState> DocsPanelStateByWorkspace { get; init; }
        = new Dictionary<string, WorkspaceDocsPanelState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the loop runs via the native agent controller (NativeAgents) or the
    /// Squad CLI bridge (SquadCli). Defaults to NativeAgents.
    /// </summary>
    public LoopMode LoopMode { get; init; } = LoopMode.NativeAgents;

    /// <summary>
    /// When true the native loop reuses the active conversation session across iterations.
    /// When false each iteration gets a fresh session so state does not accumulate.
    /// </summary>
    public bool LoopContinuousContext { get; init; } = true;

    /// <summary>
    /// True when the native loop was running at the time of the last write.
    /// SquadDash uses this to auto-resume the loop after an unexpected shutdown or restart.
    /// </summary>
    public bool LoopActiveOnExit { get; init; } = false;

    /// <summary>
    /// The last completed loop iteration number at the time of the last write.
    /// Used to continue the iteration counter when auto-resuming after a restart.
    /// Reset to 0 when the loop is stopped normally.
    /// </summary>
    public int LoopLastIteration { get; init; } = 0;

    /// <summary>
    /// When true, Remote Access was active when the app last exited.
    /// SquadDash uses this to auto-resume RC after an unexpected shutdown or restart.
    /// </summary>
    public bool RemoteAccessActiveOnExit { get; init; } = false;

    /// <summary>
    /// Whether the Approved section is visible in the Approvals panel.
    /// Defaults to true (approved items shown by default). Machine-wide setting.
    /// </summary>
    public bool ApprovalShowApproved { get; init; } = true;

    /// <summary>
    /// Whether the Rejected section is visible in the Approvals panel.
    /// Defaults to true (rejected items shown by default). Machine-wide setting.
    /// </summary>
    public bool ApprovalShowRejected { get; init; } = true;

    /// <summary>
    /// The RC session token from the last successful RC start.
    /// Passed back on the next <c>rc_start</c> so the phone's saved QR link keeps working.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("rcPersistentToken")]
    public string? RcPersistentToken { get; init; }

    /// <summary>
    /// The TCP port the RC WebSocket server was last listening on.
    /// Passed back on the next <c>rc_start</c> so the phone's browser (which has the port
    /// baked into its URL) can reconnect without re-scanning the QR code.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("rcPersistentPort")]
    public int RcPersistentPort { get; init; }

    // ── Sound notifications ───────────────────────────────────────────────────

    /// <summary>Whether a sound plays when a prompt completes. Default: true.</summary>
    public bool Sound_PromptComplete_Enabled { get; init; } = true;
    /// <summary>Custom audio file for PromptComplete. Empty = use system sound.</summary>
    public string Sound_PromptComplete_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays when a prompt errors. Default: false.</summary>
    public bool Sound_PromptError_Enabled { get; init; } = false;
    /// <summary>Custom audio file for PromptError. Empty = use system sound.</summary>
    public string Sound_PromptError_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays when approval is needed. Default: false.</summary>
    public bool Sound_ApprovalNeeded_Enabled { get; init; } = false;
    /// <summary>Custom audio file for ApprovalNeeded. Empty = use system sound.</summary>
    public string Sound_ApprovalNeeded_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays when the queue becomes empty. Default: false.</summary>
    public bool Sound_QueueEmpty_Enabled { get; init; } = false;
    /// <summary>Custom audio file for QueueEmpty. Empty = use system sound.</summary>
    public string Sound_QueueEmpty_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays on each loop iteration completion. Default: false.</summary>
    public bool Sound_LoopIterationComplete_Enabled { get; init; } = false;
    /// <summary>Custom audio file for LoopIterationComplete. Empty = use system sound.</summary>
    public string Sound_LoopIterationComplete_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays when the loop stops. Default: false.</summary>
    public bool Sound_LoopStopped_Enabled { get; init; } = false;
    /// <summary>Custom audio file for LoopStopped. Empty = use system sound.</summary>
    public string Sound_LoopStopped_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays when a git commit is made. Default: false.</summary>
    public bool Sound_CommitMade_Enabled { get; init; } = false;
    /// <summary>Custom audio file for CommitMade. Empty = use system sound.</summary>
    public string Sound_CommitMade_CustomPath { get; init; } = "";

    /// <summary>Whether a sound plays when quick reply buttons are shown. Default: false.</summary>
    public bool Sound_QuickRepliesShown_Enabled { get; init; } = false;
    /// <summary>Custom audio file for QuickRepliesShown. Empty = use system sound.</summary>
    public string Sound_QuickRepliesShown_CustomPath { get; init; } = "";

    // ── TTS (Text-to-Speech spoken phrases) ──────────────────────────────────

    /// <summary>Which TTS provider is used to speak quoted phrases in Sound event paths.</summary>
    public TtsProvider Tts_Provider { get; init; } = TtsProvider.Azure;

    /// <summary>
    /// Azure Neural voice name, e.g. "en-US-JennyNeural".
    /// Stored independently of <see cref="Tts_OpenAi_Voice"/> so switching providers
    /// never clobbers the other provider's choice.
    /// </summary>
    public string Tts_Azure_Voice { get; init; } = "en-US-JennyNeural";

    /// <summary>
    /// OpenAI TTS voice: alloy, echo, fable, onyx, nova, or shimmer.
    /// Stored independently of <see cref="Tts_Azure_Voice"/>.
    /// </summary>
    public string Tts_OpenAi_Voice { get; init; } = "alloy";

    /// <summary>OpenAI TTS model quality: Standard (tts-1) or HD (tts-1-hd).</summary>
    public OpenAiTtsModel Tts_OpenAi_Model { get; init; } = OpenAiTtsModel.Standard;

    /// <summary>Index of the last-visited page in the Preferences dialog (0 = General).</summary>
    public int Preferences_LastPage { get; init; } = 0;

    /// <summary>Index into the discrete font-size scale levels (0=XSmall … 6=Huge). Default 2 = 1× scale.</summary>
    public int FontSizeScaleLevel { get; init; } = 2;

    /// <summary>
    /// Per-workspace shutdown timestamps.  Keyed by normalised workspace folder path.
    /// Saved on clean shutdown; consumed once on the next startup to display a session
    /// gap indicator in the transcript; then cleared.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset>? WorkspaceShutdownTimes { get; init; }

    /// <summary>
    /// Per-workspace hue rotation stop (0 = natural; 1–7 = hue offsets at 45° increments).
    /// Keyed by normalised workspace folder path.
    /// </summary>
    public IReadOnlyDictionary<string, int> TintStopByWorkspace { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-workspace accent hue offset in degrees applied on top of the tint stop for all
    /// ActiveAccent keys (0 = natural complement; valid range −180 to +180).
    /// Keyed by normalised workspace folder path.
    /// </summary>
    public IReadOnlyDictionary<string, int> AccentHueOffsetByWorkspace { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static ApplicationSettingsSnapshot Empty{ get; } =
        new(
            null,
            Array.Empty<string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, WorkspaceWindowPlacement>(StringComparer.OrdinalIgnoreCase),
            14,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public ApplicationSettingsSnapshot Normalize() {
        var normalizedFolders = RecentFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lastOpenedFolder = string.IsNullOrWhiteSpace(LastOpenedFolder)
            ? normalizedFolders.FirstOrDefault()
            : Path.GetFullPath(LastOpenedFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var normalizedAgentColors = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var normalizedPlacements = new Dictionary<string, WorkspaceWindowPlacement>(StringComparer.OrdinalIgnoreCase);

        if (AgentAccentColorsByWorkspace is not null) {
            foreach (var workspaceEntry in AgentAccentColorsByWorkspace) {
                if (string.IsNullOrWhiteSpace(workspaceEntry.Key))
                    continue;

                var normalizedWorkspace = Path.GetFullPath(workspaceEntry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedAgentEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var agentEntry in workspaceEntry.Value) {
                    if (string.IsNullOrWhiteSpace(agentEntry.Key) || string.IsNullOrWhiteSpace(agentEntry.Value))
                        continue;

                    normalizedAgentEntries[agentEntry.Key] = agentEntry.Value.Trim().ToUpperInvariant();
                }

                normalizedAgentColors[normalizedWorkspace] = normalizedAgentEntries;
            }
        }

        var normalizedAgentImages = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (AgentImagePathsByWorkspace is not null) {
            foreach (var workspaceEntry in AgentImagePathsByWorkspace) {
                if (string.IsNullOrWhiteSpace(workspaceEntry.Key))
                    continue;

                var normalizedWorkspace = Path.GetFullPath(workspaceEntry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var agentEntry in workspaceEntry.Value) {
                    if (string.IsNullOrWhiteSpace(agentEntry.Key) || string.IsNullOrWhiteSpace(agentEntry.Value))
                        continue;
                    normalizedEntries[agentEntry.Key] = agentEntry.Value.Trim();
                }

                normalizedAgentImages[normalizedWorkspace] = normalizedEntries;
            }
        }

        var normalizedIgnoredRoutingIssueFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (IgnoredRoutingIssueFingerprintsByWorkspace is not null) {
            foreach (var entry in IgnoredRoutingIssueFingerprintsByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedIgnoredRoutingIssueFingerprints[normalizedWorkspace] = entry.Value.Trim();
            }
        }

        var normalizedTranscriptViewModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TranscriptViewModeByWorkspace is not null) {
            foreach (var entry in TranscriptViewModeByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedMode = string.Equals(entry.Value.Trim(), "multi", StringComparison.OrdinalIgnoreCase)
                    ? "multi" : "single";
                normalizedTranscriptViewModes[normalizedWorkspace] = normalizedMode;
            }
        }

        var normalizedDocsPanelState = new Dictionary<string, WorkspaceDocsPanelState>(StringComparer.OrdinalIgnoreCase);
        if (DocsPanelStateByWorkspace is not null) {
            foreach (var entry in DocsPanelStateByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;
                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedDocsPanelState[normalizedWorkspace] = entry.Value;
            }
        }

        if (WindowPlacementByWorkspace is not null) {
            foreach (var entry in WindowPlacementByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedPlacement = entry.Value.Normalize();
                if (!normalizedPlacement.IsUsable)
                    continue;

                normalizedPlacements[normalizedWorkspace] = normalizedPlacement;
            }
        }

        var normalizedTintStops = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (TintStopByWorkspace is not null) {
            foreach (var entry in TintStopByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedTintStops[normalizedWorkspace] = Math.Clamp(entry.Value, 0, 7);
            }
        }

        var normalizedAccentOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (AccentHueOffsetByWorkspace is not null) {
            foreach (var entry in AccentHueOffsetByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedAccentOffsets[normalizedWorkspace] = Math.Clamp(entry.Value, -180, 180);
            }
        }

        IReadOnlyDictionary<string, DateTimeOffset>? normalizedShutdownTimes = null;
        if (WorkspaceShutdownTimes is not null) {
            var dict = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in WorkspaceShutdownTimes) {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;
                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                dict[normalizedWorkspace] = entry.Value;
            }
            normalizedShutdownTimes = dict.Count > 0 ? dict : null;
        }

        var normalizedStartupSimulations = new Dictionary<string, DeveloperStartupIssueSimulation>(StringComparer.OrdinalIgnoreCase);
        if (StartupIssueSimulationByWorkspace is not null) {
            foreach (var entry in StartupIssueSimulationByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedStartupSimulations[normalizedWorkspace] = entry.Value;
            }
        }

        var normalizedRuntimeSimulations = new Dictionary<string, DeveloperRuntimeIssueSimulation>(StringComparer.OrdinalIgnoreCase);
        if (RuntimeIssueSimulationByWorkspace is not null) {
            foreach (var entry in RuntimeIssueSimulationByWorkspace) {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                var normalizedWorkspace = Path.GetFullPath(entry.Key)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalizedRuntimeSimulations[normalizedWorkspace] = entry.Value;
            }
        }

        return new ApplicationSettingsSnapshot(
            lastOpenedFolder,
            normalizedFolders,
            normalizedAgentColors,
            normalizedPlacements,
            NormalizeFontSize(PromptFontSize),
            normalizedAgentImages,
            normalizedIgnoredRoutingIssueFingerprints) {
            UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
            SpeechRegion = string.IsNullOrWhiteSpace(SpeechRegion) ? null : SpeechRegion.Trim(),
            TranscriptFontSize = NormalizeFontSize(TranscriptFontSize),
            DocSourceFontSize = NormalizeFontSize(DocSourceFontSize),
            Theme = Theme is "Light" or "Dark" or "Auto" ? Theme : null,
            LastUsedModel = string.IsNullOrWhiteSpace(LastUsedModel) ? null : LastUsedModel.Trim(),
            TasksWindowOpen = TasksWindowOpen,
            TraceWindowOpen = TraceWindowOpen,
            ApprovalWindowOpen = ApprovalWindowOpen,
            DisabledTraceCategories = (DisabledTraceCategories ?? Enum.GetNames<TraceCategory>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DisabledTraceCategorySet = new HashSet<TraceCategory>(
                (DisabledTraceCategories ?? Enum.GetNames<TraceCategory>())
                    .Select(s => Enum.TryParse<TraceCategory>(s, ignoreCase: true, out var v) ? (TraceCategory?)v : null)
                    .OfType<TraceCategory>()),
            TranscriptViewModeByWorkspace = normalizedTranscriptViewModes,
            DocsPanelStateByWorkspace = normalizedDocsPanelState,
            DocsPanelOpen = DocsPanelOpen,
            DocsExpandedNodes = DocsExpandedNodes is null
                ? null
                : DocsExpandedNodes.Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToArray(),
            DocsSelectedTopic = string.IsNullOrWhiteSpace(DocsSelectedTopic) ? null : DocsSelectedTopic.Trim(),
            DocsSourceOpen = DocsSourceOpen,
            DocsSourceWidth = DocsSourceWidth,
            NotificationProvider = string.IsNullOrWhiteSpace(NotificationProvider) ? null : NotificationProvider.Trim(),
            NotificationEndpoint = NotificationEndpoint,
            NotificationEventToggles = NotificationEventToggles,
            TunnelMode = TunnelMode is "ngrok" or "cloudflare" ? TunnelMode : null,
            TunnelToken = string.IsNullOrWhiteSpace(TunnelToken) ? null : TunnelToken.Trim(),
            ByokProviderUrl = string.IsNullOrWhiteSpace(ByokProviderUrl) ? null : ByokProviderUrl.Trim(),
            ByokModel = string.IsNullOrWhiteSpace(ByokModel) ? null : ByokModel.Trim(),
            ByokProviderType = ByokProviderType is "openai" or "azure" or "anthropic" ? ByokProviderType : null,
            ByokApiKey = string.IsNullOrWhiteSpace(ByokApiKey) ? null : ByokApiKey.Trim(),
            LoopMode = LoopMode,
            LoopContinuousContext = LoopContinuousContext,
            LoopActiveOnExit = LoopActiveOnExit,
            LoopLastIteration = LoopLastIteration,
            RemoteAccessActiveOnExit = RemoteAccessActiveOnExit,
            RcPersistentToken = string.IsNullOrWhiteSpace(RcPersistentToken) ? null : RcPersistentToken.Trim(),
            RcPersistentPort = RcPersistentPort,
            ApprovalShowApproved = ApprovalShowApproved,
            ApprovalShowRejected = ApprovalShowRejected,
            CleanupPrompt = string.IsNullOrWhiteSpace(CleanupPrompt)
                ? "Clean up and clarify this text."
                : CleanupPrompt,
            SpeechProvider = SpeechProvider,
            PttAutoSend = PttAutoSend,
            OpenAiSpeechApiKey = string.IsNullOrWhiteSpace(OpenAiSpeechApiKey) ? null : OpenAiSpeechApiKey.Trim(),
            SpeechLanguage = string.IsNullOrWhiteSpace(SpeechLanguage) ? null : SpeechLanguage.Trim(),
            VoiceReplacementRules = VoiceReplacementRules
                .Where(r => !string.IsNullOrWhiteSpace(r?.Pattern))
                .ToArray(),
            Sound_PromptComplete_Enabled            = Sound_PromptComplete_Enabled,
            Sound_PromptComplete_CustomPath         = Sound_PromptComplete_CustomPath ?? "",
            Sound_PromptError_Enabled               = Sound_PromptError_Enabled,
            Sound_PromptError_CustomPath            = Sound_PromptError_CustomPath ?? "",
            Sound_ApprovalNeeded_Enabled            = Sound_ApprovalNeeded_Enabled,
            Sound_ApprovalNeeded_CustomPath         = Sound_ApprovalNeeded_CustomPath ?? "",
            Sound_QueueEmpty_Enabled                = Sound_QueueEmpty_Enabled,
            Sound_QueueEmpty_CustomPath             = Sound_QueueEmpty_CustomPath ?? "",
            Sound_LoopIterationComplete_Enabled     = Sound_LoopIterationComplete_Enabled,
            Sound_LoopIterationComplete_CustomPath  = Sound_LoopIterationComplete_CustomPath ?? "",
            Sound_LoopStopped_Enabled               = Sound_LoopStopped_Enabled,
            Sound_LoopStopped_CustomPath            = Sound_LoopStopped_CustomPath ?? "",
            Sound_CommitMade_Enabled                = Sound_CommitMade_Enabled,
            Sound_CommitMade_CustomPath             = Sound_CommitMade_CustomPath ?? "",
            Sound_QuickRepliesShown_Enabled         = Sound_QuickRepliesShown_Enabled,
            Sound_QuickRepliesShown_CustomPath      = Sound_QuickRepliesShown_CustomPath ?? "",
            Tts_Provider     = Tts_Provider,
            Tts_Azure_Voice  = string.IsNullOrWhiteSpace(Tts_Azure_Voice)  ? "en-US-JennyNeural" : Tts_Azure_Voice.Trim(),
            Tts_OpenAi_Voice = string.IsNullOrWhiteSpace(Tts_OpenAi_Voice) ? "alloy"              : Tts_OpenAi_Voice.Trim(),
            Tts_OpenAi_Model = Tts_OpenAi_Model,
            Preferences_LastPage = Math.Max(0, Preferences_LastPage),
            WorkspaceShutdownTimes = normalizedShutdownTimes,
            TintStopByWorkspace = normalizedTintStops,
            AccentHueOffsetByWorkspace = normalizedAccentOffsets,
            StartupIssueSimulationByWorkspace = normalizedStartupSimulations,
            RuntimeIssueSimulationByWorkspace = normalizedRuntimeSimulations,
            FontSizeScaleLevel = Math.Clamp(FontSizeScaleLevel, 0, 6),
        };
    }

    private static double NormalizeFontSize(double fontSize) {
        return double.IsFinite(fontSize) && fontSize > 0
            ? fontSize
            : 14;
    }
}

public enum LoopMode { NativeAgents, SquadCli }

internal enum SpeechProvider { Azure, OpenAI }

internal enum TtsProvider { Azure, OpenAI }

/// <summary>Corresponds to OpenAI's <c>tts-1</c> (Standard) and <c>tts-1-hd</c> (HD) models.</summary>
internal enum OpenAiTtsModel { Standard, HD }

internal enum LoopConfigFlyoutMode { Configure, Edit }

internal enum DeveloperStartupIssueSimulation {
    None,
    MissingNodeTooling,
    SquadNotInstalled,
    PartialSquadInstall
}

internal enum DeveloperRuntimeIssueSimulation {
    None,
    CopilotAuthRequired,
    BundledSdkRepair,
    BuildTempFiles,
    GenericRuntimeFailure
}

internal sealed record WorkspaceWindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized) {

    public bool IsUsable =>
        IsFinitePositive(Width) &&
        IsFinitePositive(Height) &&
        IsFinite(Left) &&
        IsFinite(Top);

    public WorkspaceWindowPlacement Normalize() {
        return new WorkspaceWindowPlacement(
            NormalizeFinite(Left),
            NormalizeFinite(Top),
            NormalizePositive(Width),
            NormalizePositive(Height),
            IsMaximized);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinitePositive(double value) =>
        IsFinite(value) && value > 0;

    private static double NormalizeFinite(double value) =>
        IsFinite(value) ? value : 0;

    private static double NormalizePositive(double value) =>
        IsFinitePositive(value) ? value : 0;
}