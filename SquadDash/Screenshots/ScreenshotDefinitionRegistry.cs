using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Screenshots;

// ─────────────────────────────────────────────────────────────────────────────
//  ScreenshotDefinitionRegistry
//
//  Manages the authoritative list of ScreenshotDefinition entries persisted in
//  docs/screenshots/definitions.json.
//
//  Usage
//  ─────
//  1. Load (or create) the registry at startup:
//
//       var registry = await ScreenshotDefinitionRegistry.LoadAsync(screenshotsDir, ct);
//
//  2. Upsert a definition after an interactive capture:
//
//       registry.AddOrUpdate(definition);
//       await registry.SaveAsync(ct);
//
//  3. Read all definitions for the command-line re-capture runner:
//
//       foreach (var def in registry.All) { ... }
//
//  Thread safety
//  ─────────────
//  Not thread-safe.  All calls must be made from the same thread (WPF dispatcher
//  or a single-threaded async context).  Concurrent reads are safe; concurrent
//  writes are not.
//
//  Missing file behaviour
//  ──────────────────────
//  When definitions.json does not exist, LoadAsync returns an empty registry
//  and does NOT throw.  The file is only created when SaveAsync is called.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Loads, persists, and provides access to the collection of
/// <see cref="ScreenshotDefinition"/> entries stored in
/// <c>definitions.json</c> inside the screenshots directory.
/// </summary>
/// <remarks>
/// Use <see cref="LoadAsync"/> to obtain an instance.  The registry is
/// NOT a singleton — callers that need a shared instance must manage
/// lifetime themselves.
/// </remarks>
public sealed class ScreenshotDefinitionRegistry
{
    // ── Serialisation options ──────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Instance state ─────────────────────────────────────────────────────

    private readonly string _filePath;
    private readonly List<ScreenshotDefinition> _definitions;

    // ── Constructor ────────────────────────────────────────────────────────

    private ScreenshotDefinitionRegistry(string filePath, List<ScreenshotDefinition> definitions)
    {
        _filePath    = filePath;
        _definitions = definitions;
    }

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the definition registry from
    /// <c>{screenshotsDirectory}/definitions.json</c>.
    /// </summary>
    /// <param name="screenshotsDirectory">
    ///   Absolute or relative path to the screenshots directory
    ///   (e.g. <c>docs/screenshots</c>).
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    ///   A registry populated from the file, or an empty registry when
    ///   <c>definitions.json</c> does not exist.
    /// </returns>
    /// <exception cref="JsonException">
    ///   Thrown when the file exists but its contents cannot be parsed.
    /// </exception>
    public static async Task<ScreenshotDefinitionRegistry> LoadAsync(
        string            screenshotsDirectory,
        CancellationToken ct = default)
    {
        var filePath = Path.Combine(screenshotsDirectory, "definitions.json");

        if (!File.Exists(filePath))
            return new ScreenshotDefinitionRegistry(filePath, []);

        await using var stream      = File.OpenRead(filePath);
        var             definitions = await JsonSerializer.DeserializeAsync<List<ScreenshotDefinition>>(
                                          stream, s_readOptions, ct)
                                      ?? [];

        return new ScreenshotDefinitionRegistry(filePath, definitions);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// All registered definitions, in the order they appear in
    /// <c>definitions.json</c>.
    /// </summary>
    public IReadOnlyList<ScreenshotDefinition> All => _definitions;

    /// <summary>
    /// Returns the definition with the given <paramref name="name"/>
    /// (case-insensitive), or <c>null</c> if none is registered.
    /// </summary>
    /// <param name="name">Kebab-case screenshot name to look up.</param>
    public ScreenshotDefinition? TryGet(string name) =>
        _definitions.FirstOrDefault(d =>
            d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the definition whose <see cref="ScreenshotDefinition.DocImagePath"/>
    /// resolves to <paramref name="fullDocImagePath"/> (case-insensitive), or
    /// <c>null</c> if none is registered.
    /// </summary>
    /// <param name="fullDocImagePath">Absolute path to the doc image file.</param>
    /// <param name="screenshotsDirectory">
    ///   Directory used to resolve relative <see cref="ScreenshotDefinition.DocImagePath"/>
    ///   values.
    /// </param>
    public ScreenshotDefinition? TryGetByDocImagePath(string fullDocImagePath, string screenshotsDirectory)
    {
        foreach (var def in _definitions)
        {
            if (string.IsNullOrWhiteSpace(def.DocImagePath)) continue;
            var resolved = Path.GetFullPath(
                Path.IsPathRooted(def.DocImagePath)
                    ? def.DocImagePath
                    : Path.Combine(screenshotsDirectory, def.DocImagePath));
            if (string.Equals(resolved, fullDocImagePath, StringComparison.OrdinalIgnoreCase))
                return def;
        }
        return null;
    }


    /// </summary>
    /// <remarks>
    /// Emits a <see cref="Console.Error"/> warning when
    /// <see cref="ScreenshotDefinition.Description"/> is empty.
    /// Does not persist — call <see cref="SaveAsync"/> to write changes.
    /// </remarks>
    /// <param name="definition">The definition to add or replace.</param>
    public void AddOrUpdate(ScreenshotDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            Console.Error.WriteLine(
                $"[ScreenshotDefinitionRegistry] Warning: definition '{definition.Name}' " +
                "has an empty description.  Add a description before committing baselines.");
        }

        // When the incoming definition has a DocImagePath, remove any stale entry that
        // points to the same doc image but has a different name.  This prevents old
        // dark-theme definitions from surviving when the user recaptures in light (or
        // vice-versa) and the auto-suggested name differs between captures.
        if (!string.IsNullOrWhiteSpace(definition.DocImagePath))
        {
            _definitions.RemoveAll(d =>
                !d.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.DocImagePath, definition.DocImagePath, StringComparison.OrdinalIgnoreCase));
        }

        var idx = _definitions.FindIndex(d =>
            d.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase));

        if (idx >= 0)
            _definitions[idx] = definition;
        else
            _definitions.Add(definition);
    }

    /// <summary>
    /// Writes the current definitions back to
    /// <c>{screenshotsDirectory}/definitions.json</c>, pretty-printed.
    /// Creates the file (and parent directories) if they do not exist.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Open(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, _definitions, s_writeOptions, ct);
    }
}
