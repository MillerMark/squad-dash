using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Builds an <see cref="ApprovalReviewSnapshot"/> from a <see cref="Plan"/> and a
/// <see cref="PlanApprovalGate"/>.  Pure logic except for the injected
/// <see cref="GitCommandRunner"/> used to gather per-commit diff statistics.
/// </summary>
internal sealed class ApprovalReviewSnapshotBuilder
{
    private readonly GitCommandRunner _git;

    internal ApprovalReviewSnapshotBuilder(GitCommandRunner git)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
    }

    /// <summary>
    /// Builds a snapshot covering the gate boundary.
    /// </summary>
    /// <param name="plan">The current plan state.</param>
    /// <param name="gate">The gate under review.</param>
    /// <param name="previousCheckpointSha">
    /// Optional SHA of the last resolved checkpoint (previous gate or plan start).
    /// When provided, only commits after this SHA are included.
    /// </param>
    /// <param name="verificationResults">
    /// Optional mapping of commit SHA → verification passed (true/false/null).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<ApprovalReviewSnapshot> BuildAsync(
        Plan plan,
        PlanApprovalGate gate,
        string? previousCheckpointSha = null,
        IReadOnlyDictionary<string, bool?>? verificationResults = null,
        CancellationToken cancellationToken = default)
    {
        var afterSet = gate.AfterTaskIds.ToHashSet(StringComparer.Ordinal);

        // Tasks completed before this gate (the work under review).
        var completedTasks = plan.Tasks
            .Where(t => afterSet.Contains(t.TaskId) &&
                        t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial)
            .OrderBy(t => t.CompletedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.TaskId, StringComparer.Ordinal)
            .ToList();

        // Gather unique commit SHAs from completed tasks.
        var commitShas = completedTasks
            .Where(t => !string.IsNullOrEmpty(t.Commit))
            .Select(t => t.Commit!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Fetch per-commit changed files from git.
        var changedFilesByCommit = await GetChangedFilesByCommitAsync(
            commitShas, cancellationToken).ConfigureAwait(false);

        // Build review task entries.
        var reviewTasks = completedTasks.Select(task =>
        {
            var commits = new List<ReviewCommitEntry>();
            if (!string.IsNullOrEmpty(task.Commit))
            {
                var sha = task.Commit!;
                var shortSha = sha.Length > 7 ? sha[..7] : sha;
                var subject = GetCommitSubject(changedFilesByCommit, sha, task.Title ?? task.TaskId);
                var link = new CommitLink(shortSha, sha, subject);
                var files = changedFilesByCommit.TryGetValue(sha, out var f) ? f : [];
                bool? verified = verificationResults?.TryGetValue(sha, out var v) == true ? v : null;
                commits.Add(new ReviewCommitEntry(link, verified, files));
            }
            return new ReviewTaskEntry(task.TaskId, task.Title ?? task.TaskId, task.CompletionSummary, commits);
        }).ToList();

        // Downstream tasks (blocked by this gate).
        var beforeSet = gate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal);
        var downstreamTasks = plan.Tasks
            .Where(t => beforeSet.Contains(t.TaskId))
            .OrderBy(t => t.TaskId, StringComparer.Ordinal)
            .Select(t => new DownstreamTaskEntry(t.TaskId, t.Title ?? t.TaskId, t.Status))
            .ToList();

        // Flatten all changed files with deterministic ordering.
        var allChangedFiles = reviewTasks
            .SelectMany(t => t.Commits)
            .SelectMany(c => c.ChangedFiles)
            .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.CommitSha, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Plan progress.
        var completedCount = plan.Tasks.Count(t => t.Status == PlanTaskStatus.Complete);

        return new ApprovalReviewSnapshot(
            PlanId: plan.PlanId,
            PlanTitle: plan.Title,
            CompletedTaskCount: completedCount,
            TotalTaskCount: plan.Tasks.Count,
            CurrentStage: plan.LifecycleStatus,
            GateId: gate.GateId,
            GateReason: gate.Message,
            AfterTaskIds: gate.AfterTaskIds,
            BeforeTaskIds: gate.BeforeTaskIds,
            CompletedTasks: reviewTasks,
            DownstreamTasks: downstreamTasks,
            AllChangedFiles: allChangedFiles,
            IndependentWork: [],
            BuiltAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Updates an existing snapshot with independently completed work (tasks outside the gate
    /// boundary that completed during the early review window). Independent work is labeled
    /// separately and appended with deterministic ordering.
    /// </summary>
    internal async Task<ApprovalReviewSnapshot> UpdateWithIndependentWorkAsync(
        ApprovalReviewSnapshot snapshot,
        Plan currentPlan,
        PlanApprovalGate gate,
        CancellationToken cancellationToken = default)
    {
        var gateTaskIds = gate.AfterTaskIds
            .Concat(gate.BeforeTaskIds)
            .ToHashSet(StringComparer.Ordinal);

        var alreadyIncluded = snapshot.CompletedTasks
            .Select(t => t.TaskId)
            .Concat(snapshot.IndependentWork.Select(w => w.TaskId))
            .ToHashSet(StringComparer.Ordinal);

        // Independent tasks: completed, not part of this gate, not already in snapshot.
        var independentTasks = currentPlan.Tasks
            .Where(t => t.Status == PlanTaskStatus.Complete &&
                        !gateTaskIds.Contains(t.TaskId) &&
                        !alreadyIncluded.Contains(t.TaskId))
            .OrderBy(t => t.CompletedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.TaskId, StringComparer.Ordinal)
            .ToList();

        if (independentTasks.Count == 0)
            return snapshot;

        var commitShas = independentTasks
            .Where(t => !string.IsNullOrEmpty(t.Commit))
            .Select(t => t.Commit!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changedFilesByCommit = await GetChangedFilesByCommitAsync(
            commitShas, cancellationToken).ConfigureAwait(false);

        var newIndependentWork = independentTasks.Select(task =>
        {
            var commits = new List<ReviewCommitEntry>();
            if (!string.IsNullOrEmpty(task.Commit))
            {
                var sha = task.Commit!;
                var shortSha = sha.Length > 7 ? sha[..7] : sha;
                var subject = GetCommitSubject(changedFilesByCommit, sha, task.Title ?? task.TaskId);
                var link = new CommitLink(shortSha, sha, subject);
                var files = changedFilesByCommit.TryGetValue(sha, out var f) ? f : [];
                commits.Add(new ReviewCommitEntry(link, null, files));
            }
            return new IndependentWorkEntry(task.TaskId, task.Title ?? task.TaskId, task.CompletionSummary, commits);
        }).ToList();

        var mergedIndependentWork = snapshot.IndependentWork.Concat(newIndependentWork)
            .OrderBy(w => w.TaskId, StringComparer.Ordinal)
            .ToList();

        return snapshot with { IndependentWork = mergedIndependentWork };
    }

    // ── Git helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Gathers changed files per commit using <c>git show --numstat --format=...</c>.
    /// </summary>
    private async Task<Dictionary<string, List<ChangedFileEntry>>> GetChangedFilesByCommitAsync(
        IReadOnlyList<string> commitShas,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        if (commitShas.Count == 0) return result;

        // Batch all SHAs in one git call for efficiency.
        var shaArgs = string.Join(" ", commitShas);
        var args = $"show --no-walk --format=\"COMMIT:%H %s\" --diff-filter=AMDRC --numstat {shaArgs}";

        string stdout;
        try
        {
            stdout = await _git(args, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return result;
        }

        ParseShowOutput(stdout, result);
        return result;
    }

    /// <summary>
    /// Parses output from <c>git show --format="COMMIT:%H %s" --numstat</c>.
    /// </summary>
    internal static void ParseShowOutput(
        string stdout,
        Dictionary<string, List<ChangedFileEntry>> result)
    {
        string? currentSha = null;
        string? currentSubject = null;
        List<ChangedFileEntry>? currentFiles = null;

        foreach (var rawLine in stdout.AsSpan().EnumerateLines())
        {
            var line = rawLine.ToString();
            if (line.StartsWith("COMMIT:", StringComparison.Ordinal))
            {
                // Flush previous commit.
                if (currentSha is not null && currentFiles is not null)
                    result[currentSha] = currentFiles;

                var rest = line[7..];
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    currentSha = rest[..spaceIdx];
                    currentSubject = rest[(spaceIdx + 1)..].Trim();
                }
                else
                {
                    currentSha = rest.Trim();
                    currentSubject = null;
                }
                currentFiles = [];
            }
            else if (currentSha is not null && currentFiles is not null &&
                     line.Length > 0 && (char.IsAsciiDigit(line[0]) || line[0] == '-'))
            {
                // numstat line: insertions \t deletions \t filepath
                var parts = line.Split('\t', 3);
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[0], out var ins);
                    int.TryParse(parts[1], out var del);
                    var filePath = parts[2].Trim();
                    var status = InferStatus(ins, del, filePath);
                    var fileLink = new FileLink(filePath, currentSha);
                    currentFiles.Add(new ChangedFileEntry(
                        filePath, status, ins, del, currentSha, fileLink));
                }
            }
        }

        // Flush last commit.
        if (currentSha is not null && currentFiles is not null)
            result[currentSha] = currentFiles;
    }

    /// <summary>Infers file change status from numstat data (heuristic).</summary>
    private static FileChangeStatus InferStatus(int insertions, int deletions, string filePath)
    {
        // numstat alone cannot distinguish A/M/D reliably.
        // We use a simple heuristic: zero deletions = likely Added, zero insertions = likely Deleted.
        // The --diff-filter captures the authoritative status but numstat doesn't carry it per-line.
        if (deletions == 0 && insertions > 0) return FileChangeStatus.Added;
        if (insertions == 0 && deletions > 0) return FileChangeStatus.Deleted;
        if (filePath.Contains(" => ")) return FileChangeStatus.Renamed;
        return FileChangeStatus.Modified;
    }

    private static string GetCommitSubject(
        Dictionary<string, List<ChangedFileEntry>> changedFilesByCommit,
        string sha,
        string fallback)
    {
        // The subject is stored during parse; however, since we store ChangedFileEntry
        // not subjects, use the fallback. For proper subject extraction we'd need a second
        // data structure — but the CommitLink subject comes from the parse output.
        return fallback;
    }

    // ── Subject extraction (enriched parse) ───────────────────────────────────

    /// <summary>
    /// Parses git show output and returns both changed files and commit subjects.
    /// </summary>
    internal static (Dictionary<string, List<ChangedFileEntry>> Files,
                     Dictionary<string, string> Subjects)
        ParseShowOutputWithSubjects(string stdout)
    {
        var files = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        var subjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? currentSha = null;
        string? currentSubject = null;
        List<ChangedFileEntry>? currentFiles = null;

        foreach (var rawLine in stdout.AsSpan().EnumerateLines())
        {
            var line = rawLine.ToString();
            if (line.StartsWith("COMMIT:", StringComparison.Ordinal))
            {
                if (currentSha is not null)
                {
                    if (currentFiles is not null) files[currentSha] = currentFiles;
                    if (currentSubject is not null) subjects[currentSha] = currentSubject;
                }

                var rest = line[7..];
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    currentSha = rest[..spaceIdx];
                    currentSubject = rest[(spaceIdx + 1)..].Trim();
                }
                else
                {
                    currentSha = rest.Trim();
                    currentSubject = null;
                }
                currentFiles = [];
            }
            else if (currentSha is not null && currentFiles is not null &&
                     line.Length > 0 && (char.IsAsciiDigit(line[0]) || line[0] == '-'))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[0], out var ins);
                    int.TryParse(parts[1], out var del);
                    var filePath = parts[2].Trim();
                    var status = InferStatus(ins, del, filePath);
                    var fileLink = new FileLink(filePath, currentSha);
                    currentFiles.Add(new ChangedFileEntry(
                        filePath, status, ins, del, currentSha, fileLink));
                }
            }
        }

        if (currentSha is not null)
        {
            if (currentFiles is not null) files[currentSha] = currentFiles;
            if (currentSubject is not null) subjects[currentSha] = currentSubject;
        }

        return (files, subjects);
    }
}
