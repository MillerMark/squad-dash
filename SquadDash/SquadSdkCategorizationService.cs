namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Categorizes uncategorized commits by spawning a fresh <see cref="SquadSdkProcess"/>
/// and asking the AI to return JSON category assignments.
/// Runs entirely in the background — never touches the main conversation bridge.
/// </summary>
internal sealed class SquadSdkCategorizationService
{
    private readonly IWorkspacePaths _paths;

    public SquadSdkCategorizationService(IWorkspacePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Sends a batch of commits to AI and returns SHA → group assignments.
    /// </summary>
    /// <param name="commits">List of (sha, description) pairs to categorize.</param>
    /// <param name="groups">Current groups, their usage counts, and starter provenance.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<(string Sha, string Group)>> CategorizeAsync(
        IReadOnlyList<(string Sha, string Description)> commits,
        IReadOnlyList<FeatureGroupUsage> groups,
        CancellationToken ct = default)
    {
        if (commits.Count == 0) return [];

        var prompt = BuildPrompt(commits, groups);
        var responseText = await RunPromptAndCollectResponseAsync(prompt, ct).ConfigureAwait(false);
        return ParseResponse(responseText);
    }

    internal static string BuildPrompt(
        IReadOnlyList<(string Sha, string Description)> commits,
        IReadOnlyList<FeatureGroupUsage> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a commit categorizer. Analyze the git commits below and assign each to the most appropriate feature group.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a JSON array with this exact format and no other text:");
        sb.AppendLine("[{\"sha\":\"<sha>\",\"group\":\"<group>\"}]");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Use \"Bug Fixes\" for cross-cutting fixes only when no established feature category fits; keep feature-specific fixes with their established feature.");
        sb.AppendLine("- Use \"Developer Experience\" for cross-cutting build, test, or tooling changes only when no established feature category fits.");
        sb.AppendLine("- Keep the sha field exactly as given (do not modify).");
        sb.AppendLine();

        FeatureGroupPromptBuilder.AppendCategorizationGuidance(sb, groups);

        sb.AppendLine("Commits to categorize:");
        foreach (var (sha, desc) in commits)
            sb.AppendLine($"{sha}: {desc}");

        return sb.ToString();
    }

    private async Task<string> RunPromptAndCollectResponseAsync(string prompt, CancellationToken ct)
    {
        var responseSb = new StringBuilder();
        await using var sdk = new SquadSdkProcess(_paths);

        sdk.EventReceived += (_, evt) =>
        {
            if (string.Equals(evt.Type, "response_delta", StringComparison.Ordinal) &&
                evt.Chunk is not null)
            {
                lock (responseSb)
                    responseSb.Append(evt.Chunk);
            }
        };

        await sdk.RunPromptAsync(prompt, _paths.ApplicationRoot).ConfigureAwait(false);

        string text;
        lock (responseSb)
            text = responseSb.ToString();
        return text;
    }

    /// <summary>
    /// Extracts the JSON array from the AI's response text.
    /// The AI may wrap it in markdown fences; this strips those.
    /// </summary>
    internal static IReadOnlyList<(string Sha, string Group)> ParseResponse(string responseText)
    {
        var start = responseText.IndexOf('[');
        var end   = responseText.LastIndexOf(']');
        if (start < 0 || end <= start) return [];

        var json = responseText[start..(end + 1)];
        try
        {
            var items = JsonSerializer.Deserialize<List<CategorizationResultItem>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items is null) return [];

            var results = new List<(string, string)>(items.Count);
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.Sha) && !string.IsNullOrWhiteSpace(item.Group))
                    results.Add((item.Sha!, item.Group!));
            }
            return results;
        }
        catch
        {
            return [];
        }
    }

    private sealed class CategorizationResultItem
    {
        public string? Sha   { get; set; }
        public string? Group { get; set; }
    }
}
