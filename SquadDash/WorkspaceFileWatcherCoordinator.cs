using System;
using System.IO;

namespace SquadDash;

/// <summary>
/// Owns and manages all <see cref="FileSystemWatcher"/> instances used by
/// <see cref="MainWindow"/>. MainWindow holds a single instance, provides
/// per-event callbacks at construction time, and calls the Configure* /
/// Dispose* methods to control watcher lifecycles.
///
/// Threading: FileSystemWatcher events arrive on thread-pool threads.
/// Callbacks that require the UI thread must marshal themselves (e.g. via
/// Dispatcher.BeginInvoke); this class makes no threading guarantees beyond
/// faithfully forwarding the raw FileSystemEventArgs.
/// </summary>
internal sealed class WorkspaceFileWatcherCoordinator : IDisposable
{
    // ── Infrastructure callbacks ───────────────────────────────────────────
    private readonly Action<Action, string> _postToUi;
    private readonly Action<string, Exception, bool> _handleException;

    // ── Per-watcher event callbacks ────────────────────────────────────────
    private readonly Action _onInboxChanged;
    private readonly Action<string?> _onTeamFileChanged;
    private readonly Action<string?, string?> _onTeamFileRenamed;
    private readonly Action _onGitHeadChanged;
    private readonly Action _onRestartRequestChanged;
    private readonly Action<FileSystemEventArgs> _onDocsChanged;
    private readonly Action<RenamedEventArgs> _onDocsRenamed;
    private readonly Action _onCodeHealthMdChanged;
    private readonly Action _onFeatureCategoryChanged;

    // ── Watcher instances ──────────────────────────────────────────────────
    private FileSystemWatcher? _inboxWatcher;
    private FileSystemWatcher? _teamFileWatcher;
    private FileSystemWatcher? _restartRequestWatcher;
    private FileSystemWatcher? _gitHeadWatcher;
    private FileSystemWatcher? _docsWatcher;
    private FileSystemWatcher? _codeHealthMdWatcher;
    private FileSystemWatcher? _featureCategoryWatcher;
    private System.Timers.Timer? _featureCategoryDebounce;

    public WorkspaceFileWatcherCoordinator(
        Action<Action, string> postToUi,
        Action<string, Exception, bool> handleException,
        Action onInboxChanged,
        Action<string?> onTeamFileChanged,
        Action<string?, string?> onTeamFileRenamed,
        Action onGitHeadChanged,
        Action onRestartRequestChanged,
        Action<FileSystemEventArgs> onDocsChanged,
        Action<RenamedEventArgs> onDocsRenamed,
        Action onCodeHealthMdChanged,
        Action onFeatureCategoryChanged)
    {
        _postToUi = postToUi;
        _handleException = handleException;
        _onInboxChanged = onInboxChanged;
        _onTeamFileChanged = onTeamFileChanged;
        _onTeamFileRenamed = onTeamFileRenamed;
        _onGitHeadChanged = onGitHeadChanged;
        _onRestartRequestChanged = onRestartRequestChanged;
        _onDocsChanged = onDocsChanged;
        _onDocsRenamed = onDocsRenamed;
        _onCodeHealthMdChanged = onCodeHealthMdChanged;
        _onFeatureCategoryChanged = onFeatureCategoryChanged;
    }

    // ── Inbox watcher ──────────────────────────────────────────────────────

    public void ConfigureInboxWatcher(string inboxPath)
    {
        DisposeInboxWatcher();
        Directory.CreateDirectory(inboxPath);
        _inboxWatcher = new FileSystemWatcher(inboxPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        _inboxWatcher.Created += InboxWatcher_Changed;
        _inboxWatcher.Deleted += InboxWatcher_Changed;
        _inboxWatcher.Renamed += InboxWatcher_Renamed;
        _inboxWatcher.Changed += InboxWatcher_Changed;
        _inboxWatcher.EnableRaisingEvents = true;
    }

    public void DisposeInboxWatcher()
    {
        if (_inboxWatcher is null) return;
        _inboxWatcher.EnableRaisingEvents = false;
        _inboxWatcher.Created -= InboxWatcher_Changed;
        _inboxWatcher.Deleted -= InboxWatcher_Changed;
        _inboxWatcher.Renamed -= InboxWatcher_Renamed;
        _inboxWatcher.Changed -= InboxWatcher_Changed;
        _inboxWatcher.Dispose();
        _inboxWatcher = null;
    }

    private void InboxWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try { _postToUi(_onInboxChanged, "InboxWatcher.Changed"); }
        catch (Exception ex) { _handleException(nameof(InboxWatcher_Changed), ex, false); }
    }

    private void InboxWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try { _postToUi(_onInboxChanged, "InboxWatcher.Renamed"); }
        catch (Exception ex) { _handleException(nameof(InboxWatcher_Renamed), ex, false); }
    }

    // ── Team file watcher ─────────────────────────────────────────────────

    public void ConfigureTeamFileWatcher(string watchPath)
    {
        DisposeTeamFileWatcher();
        if (!Directory.Exists(watchPath)) return;
        _teamFileWatcher = new FileSystemWatcher(watchPath, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _teamFileWatcher.Changed += TeamFileWatcher_Changed;
        _teamFileWatcher.Created += TeamFileWatcher_Changed;
        _teamFileWatcher.Deleted += TeamFileWatcher_Changed;
        _teamFileWatcher.Renamed += TeamFileWatcher_Renamed;
        _teamFileWatcher.EnableRaisingEvents = true;
    }

    public void DisposeTeamFileWatcher()
    {
        if (_teamFileWatcher is null) return;
        _teamFileWatcher.EnableRaisingEvents = false;
        _teamFileWatcher.Changed -= TeamFileWatcher_Changed;
        _teamFileWatcher.Created -= TeamFileWatcher_Changed;
        _teamFileWatcher.Deleted -= TeamFileWatcher_Changed;
        _teamFileWatcher.Renamed -= TeamFileWatcher_Renamed;
        _teamFileWatcher.Dispose();
        _teamFileWatcher = null;
    }

    private void TeamFileWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try { _postToUi(() => _onTeamFileChanged(e.FullPath), "TeamFileWatcher.Changed"); }
        catch (Exception ex) { _handleException(nameof(TeamFileWatcher_Changed), ex, false); }
    }

    private void TeamFileWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try { _postToUi(() => _onTeamFileRenamed(e.OldFullPath, e.FullPath), "TeamFileWatcher.Renamed"); }
        catch (Exception ex) { _handleException(nameof(TeamFileWatcher_Renamed), ex, false); }
    }

    // ── Git HEAD watcher ──────────────────────────────────────────────────

    /// <summary>Returns true when the watcher was successfully started.</summary>
    public bool TryConfigureGitHeadWatcher(string gitDir)
    {
        DisposeGitHeadWatcher();
        if (!Directory.Exists(gitDir)) return false;
        try
        {
            _gitHeadWatcher = new FileSystemWatcher(gitDir, "HEAD")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _gitHeadWatcher.Changed += GitHeadWatcher_Changed;
            _gitHeadWatcher.Created += GitHeadWatcher_Changed;
            _gitHeadWatcher.EnableRaisingEvents = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void DisposeGitHeadWatcher()
    {
        _gitHeadWatcher?.Dispose();
        _gitHeadWatcher = null;
    }

    private void GitHeadWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try { _postToUi(_onGitHeadChanged, "GitHeadWatcher.Changed"); }
        catch (Exception ex) { _handleException(nameof(GitHeadWatcher_Changed), ex, false); }
    }

    // ── Restart request watcher ───────────────────────────────────────────

    public void ConfigureRestartRequestWatcher(string directory, string fileName)
    {
        DisposeRestartRequestWatcher();
        Directory.CreateDirectory(directory);
        _restartRequestWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _restartRequestWatcher.Changed += RestartRequestWatcher_Changed;
        _restartRequestWatcher.Created += RestartRequestWatcher_Changed;
        _restartRequestWatcher.Renamed += RestartRequestWatcher_Renamed;
        _restartRequestWatcher.EnableRaisingEvents = true;
    }

    public void DisposeRestartRequestWatcher()
    {
        if (_restartRequestWatcher is null) return;
        _restartRequestWatcher.EnableRaisingEvents = false;
        _restartRequestWatcher.Changed -= RestartRequestWatcher_Changed;
        _restartRequestWatcher.Created -= RestartRequestWatcher_Changed;
        _restartRequestWatcher.Renamed -= RestartRequestWatcher_Renamed;
        _restartRequestWatcher.Dispose();
        _restartRequestWatcher = null;
    }

    private void RestartRequestWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try { _postToUi(_onRestartRequestChanged, "RestartRequestWatcher.Changed"); }
        catch (Exception ex) { _handleException(nameof(RestartRequestWatcher_Changed), ex, false); }
    }

    private void RestartRequestWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try { _postToUi(_onRestartRequestChanged, "RestartRequestWatcher.Renamed"); }
        catch (Exception ex) { _handleException(nameof(RestartRequestWatcher_Renamed), ex, false); }
    }

    // ── Docs watcher ──────────────────────────────────────────────────────

    public void ConfigureDocsWatcher(string docsPath)
    {
        DisposeDocsWatcher();
        _docsWatcher = new FileSystemWatcher(docsPath, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _docsWatcher.Created += DocsWatcher_Changed;
        _docsWatcher.Deleted += DocsWatcher_Changed;
        _docsWatcher.Renamed += DocsWatcher_Renamed;
        _docsWatcher.Changed += DocsWatcher_Changed;
        _docsWatcher.EnableRaisingEvents = true;
    }

    public void DisposeDocsWatcher()
    {
        if (_docsWatcher is null) return;
        _docsWatcher.EnableRaisingEvents = false;
        _docsWatcher.Created -= DocsWatcher_Changed;
        _docsWatcher.Deleted -= DocsWatcher_Changed;
        _docsWatcher.Renamed -= DocsWatcher_Renamed;
        _docsWatcher.Changed -= DocsWatcher_Changed;
        _docsWatcher.Dispose();
        _docsWatcher = null;
    }

    /// <summary>Pauses or resumes the docs watcher without fully disposing it.</summary>
    public void SetDocsWatcherActive(bool active)
    {
        if (_docsWatcher is not null)
            _docsWatcher.EnableRaisingEvents = active;
    }

    private void DocsWatcher_Changed(object sender, FileSystemEventArgs e) =>
        _onDocsChanged(e);

    private void DocsWatcher_Renamed(object sender, RenamedEventArgs e) =>
        _onDocsRenamed(e);

    // ── Code health markdown watcher ──────────────────────────────────────

    public void InitCodeHealthMdWatcher(string squadFolder)
    {
        DisposeCodeHealthMdWatcher();
        if (!Directory.Exists(squadFolder)) return;
        _codeHealthMdWatcher = new FileSystemWatcher(squadFolder, "code-health.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
        };
        _codeHealthMdWatcher.Changed += OnMaintenanceMdChanged;
        _codeHealthMdWatcher.Created += OnMaintenanceMdChanged;
    }

    public void DisposeCodeHealthMdWatcher()
    {
        if (_codeHealthMdWatcher is null) return;
        _codeHealthMdWatcher.EnableRaisingEvents = false;
        _codeHealthMdWatcher.Changed -= OnMaintenanceMdChanged;
        _codeHealthMdWatcher.Created -= OnMaintenanceMdChanged;
        _codeHealthMdWatcher.Dispose();
        _codeHealthMdWatcher = null;
    }

    private void OnMaintenanceMdChanged(object sender, FileSystemEventArgs e)
    {
        var timer = new System.Timers.Timer(300) { AutoReset = false };
        timer.Elapsed += (_, _) =>
        {
            timer.Dispose();
            _onCodeHealthMdChanged();
        };
        timer.Start();
    }

    // ── Feature category watcher ──────────────────────────────────────────

    public void ConfigureFeatureCategoryWatcher(string workspaceStateDirectory)
    {
        DisposeFeatureCategoryWatcher();
        Directory.CreateDirectory(workspaceStateDirectory);
        _featureCategoryDebounce = new System.Timers.Timer(300) { AutoReset = false };
        _featureCategoryDebounce.Elapsed += (_, _) =>
        {
            _featureCategoryDebounce?.Stop();
            _onFeatureCategoryChanged();
        };
        _featureCategoryWatcher = new FileSystemWatcher(workspaceStateDirectory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _featureCategoryWatcher.Changed += FeatureCategoryStore_Changed;
        _featureCategoryWatcher.Created += FeatureCategoryStore_Changed;
        _featureCategoryWatcher.Renamed += FeatureCategoryStore_Renamed;
    }

    public void DisposeFeatureCategoryWatcher()
    {
        _featureCategoryDebounce?.Stop();
        _featureCategoryDebounce?.Dispose();
        _featureCategoryDebounce = null;
        if (_featureCategoryWatcher is null) return;
        _featureCategoryWatcher.EnableRaisingEvents = false;
        _featureCategoryWatcher.Changed -= FeatureCategoryStore_Changed;
        _featureCategoryWatcher.Created -= FeatureCategoryStore_Changed;
        _featureCategoryWatcher.Renamed -= FeatureCategoryStore_Renamed;
        _featureCategoryWatcher.Dispose();
        _featureCategoryWatcher = null;
    }

    private void FeatureCategoryStore_Changed(object sender, FileSystemEventArgs e)
    {
        if (!IsFeatureCategoryStoreFile(e.Name)) return;
        _featureCategoryDebounce?.Stop();
        _featureCategoryDebounce?.Start();
    }

    private void FeatureCategoryStore_Renamed(object sender, RenamedEventArgs e) =>
        FeatureCategoryStore_Changed(sender, e);

    private static bool IsFeatureCategoryStoreFile(string? name) =>
        string.Equals(name, "commit-approvals.json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "feature-groups.json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "commit-category-cache.json", StringComparison.OrdinalIgnoreCase);

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        DisposeInboxWatcher();
        DisposeTeamFileWatcher();
        DisposeGitHeadWatcher();
        DisposeRestartRequestWatcher();
        DisposeDocsWatcher();
        DisposeCodeHealthMdWatcher();
        DisposeFeatureCategoryWatcher();
    }
}
