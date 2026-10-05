using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed class ButtonConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// Local-only buttons.json store. Pass the state directory supplied by workspace coordination
/// (WorkspaceConversationStore.GetWorkspaceStateDirectory, using SquadDashPaths.WorkspaceStateDirectory's layout).
/// All operations reload under a named mutex; there is no cached list or whole-list editor save.
/// IO/access/JSON/validation/conflict errors are traced and thrown. UI callers must display them,
/// and only publish refreshed state after an operation returns successfully.
/// </summary>
internal sealed class ButtonsStore {
    private readonly string _stateDirectory;
    private readonly string _mutexName;

    public string FilePath { get; }

    public ButtonsStore(string workspaceStateDirectory) {
        _stateDirectory = ValidateStateDirectory(workspaceStateDirectory);
        FilePath = Path.Combine(_stateDirectory, "buttons.json");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant())));
        _mutexName = $@"Local\SquadDash.Buttons.{hash}";
    }

    public IReadOnlyList<ButtonDefinition> Load() =>
        Execute("Load", () => ReadCore().AsReadOnly());

    public ButtonDefinition Create(ButtonContent content) => Execute("Create", () => {
        ValidateContent(content);
        var items = ReadCore();
        var item = new ButtonDefinition(Guid.NewGuid(), content, items.Count, 1);
        items.Add(item);
        SaveCore(items);
        return item;
    });

    /// <summary>Preserves the current sort order and every other item's fields.</summary>
    public ButtonDefinition Update(Guid id, long expectedContentRevision, ButtonContent content) =>
        Execute("Update", () => {
            ValidateContent(content);
            var items = ReadCore();
            var index = FindCurrent(items, id, expectedContentRevision);
            var updated = items[index] with {
                Content = content,
                ContentRevision = checked(items[index].ContentRevision + 1)
            };
            items[index] = updated;
            SaveCore(items);
            return updated;
        });

    public void Delete(Guid id, long expectedContentRevision) => Execute("Delete", () => {
        var items = ReadCore();
        items.RemoveAt(FindCurrent(items, id, expectedContentRevision));
        SaveCore(items.Select((item, index) => item with { SortOrder = index }).ToList());
        return true;
    });

    /// <summary>
    /// Atomically applies a complete ID permutation. expectedOrder is the ID order shown when
    /// the drag began. Concurrent edits are retained; stale order/membership changes are rejected.
    /// </summary>
    public IReadOnlyList<ButtonDefinition> Reorder(IReadOnlyList<Guid> expectedOrder, IReadOnlyList<Guid> newOrder) =>
        Execute("Reorder", () => {
            ArgumentNullException.ThrowIfNull(expectedOrder);
            ArgumentNullException.ThrowIfNull(newOrder);
            var items = ReadCore();
            if (!items.Select(item => item.Id).SequenceEqual(expectedOrder))
                throw new ButtonConflictException("Buttons changed while reordering. Reload the panel and try again.");
            if (newOrder.Count != items.Count || newOrder.Distinct().Count() != items.Count ||
                !newOrder.ToHashSet().SetEquals(items.Select(item => item.Id)))
                throw new ArgumentException("The new order must contain each existing button ID exactly once.", nameof(newOrder));

            var byId = items.ToDictionary(item => item.Id);
            var reordered = newOrder.Select((id, index) => byId[id] with { SortOrder = index }).ToList();
            SaveCore(reordered);
            return reordered.AsReadOnly();
        });

    internal static string ValidateStateDirectory(string workspaceStateDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceStateDirectory);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceStateDirectory));
        var workspaceStates = Path.GetFullPath(Path.Combine(SquadDashPaths.AppData, "workspaces"));
        if (!string.Equals(Path.GetDirectoryName(normalized), workspaceStates, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Buttons require the AppData per-workspace state directory supplied by workspace coordination.",
                nameof(workspaceStateDirectory));
        return normalized;
    }

    internal static void ValidateContent(ButtonContent content) {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Caption);
        ArgumentNullException.ThrowIfNull(content.Tooltip);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.CommandText);
        if (!Enum.IsDefined(content.CommandType))
            throw new ArgumentException("Unsupported button command type.", nameof(content));
    }

    private T Execute<T>(string operation, Func<T> action) {
        try {
            using var mutex = MutexLease.Acquire(_mutexName);
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or InvalidOperationException or OverflowException
                                   or System.Security.SecurityException or TimeoutException) {
            SquadDashTrace.Write("ButtonsStore", $"{operation} failed for '{FilePath}': {ex.Message}");
            throw;
        }
    }

    private List<ButtonDefinition> ReadCore() {
        string json;
        try {
            json = File.ReadAllText(FilePath);
        }
        catch (FileNotFoundException) {
            return [];
        }
        catch (DirectoryNotFoundException) {
            return [];
        }

        var document = JsonSerializer.Deserialize<ButtonsDocument>(json)
            ?? throw new InvalidDataException("Button storage contains null, not a buttons document.");
        if (document.SchemaVersion != 1 || document.Items is null)
            throw new InvalidDataException("Unsupported or invalid buttons document. Existing storage was not changed.");

        var ids = new HashSet<Guid>();
        foreach (var item in document.Items) {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.ContentRevision < 1)
                throw new InvalidDataException("Button storage contains an invalid or duplicate identity/revision.");
            try {
                ValidateContent(item.Content);
            }
            catch (ArgumentException ex) {
                throw new InvalidDataException("Button storage contains invalid content.", ex);
            }
        }

        var ordered = document.Items.OrderBy(item => item.SortOrder).ToList();
        if (ordered.Where((item, index) => item.SortOrder != index).Any())
            throw new InvalidDataException("Button storage contains invalid sort orders.");
        return ordered;
    }

    private void SaveCore(List<ButtonDefinition> items) {
        Directory.CreateDirectory(_stateDirectory);
        JsonFileStorage.AtomicWritePreservingPrevious(FilePath, new ButtonsDocument(1, items));
    }

    private static int FindCurrent(List<ButtonDefinition> items, Guid id, long expectedContentRevision) {
        var index = items.FindIndex(item => item.Id == id);
        if (index < 0 || items[index].ContentRevision != expectedContentRevision)
            throw new ButtonConflictException("This button was edited or deleted elsewhere. Reload it before saving.");
        return index;
    }

    private sealed record ButtonsDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] List<ButtonDefinition> Items);
}
