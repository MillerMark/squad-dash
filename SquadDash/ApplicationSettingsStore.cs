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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appData, "SquadDash");
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

    public ApplicationSettingsSnapshot SaveSpeechRegion(string? region) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with {
            SpeechRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim()
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveDeveloperIssueSimulation(
        DeveloperStartupIssueSimulation startupIssueSimulation,
        DeveloperRuntimeIssueSimulation runtimeIssueSimulation) {
        using var mutex = AcquireMutex();

        var current = LoadCore();
        var updated = current with {
            StartupIssueSimulation = startupIssueSimulation,
            RuntimeIssueSimulation = runtimeIssueSimulation
        };

        SaveCore(updated);
        return updated;
    }

    public ApplicationSettingsSnapshot SaveTheme(string theme) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            Theme = theme is "Light" or "Dark" ? theme : null
        };
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

    public ApplicationSettingsSnapshot SaveUtilityWindowState(bool tasksWindowOpen, bool traceWindowOpen) {
        using var mutex = AcquireMutex();
        var current = LoadCore();
        var updated = current with {
            TasksWindowOpen = tasksWindowOpen,
            TraceWindowOpen = traceWindowOpen
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
                FullScreenTranscript = state.FullScreenTranscript ?? existing.FullScreenTranscript,
                TasksPanelVisible = state.TasksPanelVisible ?? existing.TasksPanelVisible,
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
    /// Whether the Tasks sidebar panel was visible. <c>null</c> or <c>false</c> = hidden (default).
    /// <c>true</c> = user had the panel open and wants it restored on next startup.
    /// </summary>
    public bool? TasksPanelVisible { get; init; }
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
    public double TranscriptFontSize { get; init; } = 14;

    /// <summary>
    /// Font size for the documentation source editor (DocSourceTextBox). Global/machine-wide.
    /// </summary>
    public double DocSourceFontSize { get; init; } = 12;
    public DeveloperStartupIssueSimulation StartupIssueSimulation { get; init; }
    public DeveloperRuntimeIssueSimulation RuntimeIssueSimulation { get; init; }
    public string? Theme { get; init; }
    public string? LastUsedModel { get; init; }
    public bool TasksWindowOpen { get; init; }
    public bool TraceWindowOpen { get; init; }

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
            StartupIssueSimulation = StartupIssueSimulation,
            RuntimeIssueSimulation = RuntimeIssueSimulation,
            Theme = Theme is "Light" or "Dark" ? Theme : null,
            LastUsedModel = string.IsNullOrWhiteSpace(LastUsedModel) ? null : LastUsedModel.Trim(),
            TasksWindowOpen = TasksWindowOpen,
            TraceWindowOpen = TraceWindowOpen,
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
        };
    }

    private static double NormalizeFontSize(double fontSize) {
        return double.IsFinite(fontSize) && fontSize > 0
            ? fontSize
            : 14;
    }
}

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
