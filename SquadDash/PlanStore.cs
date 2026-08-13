using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>
/// Durable, workspace-scoped store for canonical <see cref="Plan"/> objects.
/// Plans are persisted under <c>.squad/plans/{PlanId}.json</c> with atomic writes.
/// Thread-safe; all file I/O is guarded by an instance lock.
/// Does not depend on any UI classes.
/// </summary>
internal sealed class PlanStore
{
    private const string PlansSubfolder = "plans";

    private readonly string _plansFolder;
    private readonly object _sync = new();

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal PlanStore(string squadFolderPath)
    {
        _plansFolder = Path.Combine(squadFolderPath, PlansSubfolder);
    }

    /// <summary>
    /// Atomically persists <paramref name="plan"/> and returns it unchanged.
    /// Creates the plans folder on first use.
    /// </summary>
    internal Plan Save(Plan plan)
    {
        lock (_sync)
        {
            EnsureDirectory();
            var path = GetPlanPath(plan.PlanId);
            JsonFileStorage.AtomicWrite(path, plan, WriteOptions);
            return plan;
        }
    }

    /// <summary>
    /// Loads and validates the plan with the given <paramref name="planId"/>.
    /// Returns <see langword="null"/> when absent, corrupt, or identity-mismatched.
    /// </summary>
    internal Plan? Load(string planId)
    {
        lock (_sync)
        {
            return LoadInternal(planId);
        }
    }

    /// <summary>
    /// Resolves a transcript reference that may name either a plan or one of its tasks.
    /// Exact plan identity wins when both identities exist. Returns <see langword="null"/>
    /// when the reference is absent or matches tasks in more than one plan.
    /// </summary>
    internal Plan? LoadByPlanOrTaskReference(string referenceId)
    {
        lock (_sync)
        {
            var exactPlan = LoadInternal(referenceId);
            if (exactPlan is not null) return exactPlan;
            if (!Directory.Exists(_plansFolder)) return null;

            Plan? containingPlan = null;
            foreach (var path in Directory.EnumerateFiles(_plansFolder, "*.json"))
            {
                var planId = Path.GetFileNameWithoutExtension(path);
                var plan = LoadInternal(planId);
                if (plan is null || !plan.Tasks.Any(task =>
                        string.Equals(task.TaskId, referenceId, StringComparison.Ordinal)))
                    continue;

                if (containingPlan is not null)
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"Plan/task reference '{referenceId}' is ambiguous across plans " +
                        $"'{containingPlan.PlanId}' and '{plan.PlanId}'.");
                    return null;
                }

                containingPlan = plan;
            }

            return containingPlan;
        }
    }

    /// <summary>
    /// Loads all plans found in the plans folder.
    /// Silently skips any file that fails to parse or fails identity validation.
    /// </summary>
    internal IReadOnlyList<Plan> LoadAll()
    {
        lock (_sync)
        {
            if (!Directory.Exists(_plansFolder)) return [];
            var plans = new List<Plan>();
            foreach (var path in Directory.EnumerateFiles(_plansFolder, "*.json"))
            {
                var planId = Path.GetFileNameWithoutExtension(path);
                var plan = LoadInternal(planId);
                if (plan is not null) plans.Add(plan);
            }
            return plans;
        }
    }

    /// <summary>
    /// Permanently removes the plan file for <paramref name="planId"/>.
    /// Safe to call when the file does not exist.
    /// </summary>
    internal void Delete(string planId)
    {
        lock (_sync)
        {
            var path = GetPlanPath(planId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when a persisted plan with
    /// <paramref name="planId"/> exists on disk.
    /// </summary>
    internal bool Exists(string planId)
    {
        lock (_sync)
        {
            return File.Exists(GetPlanPath(planId));
        }
    }

    // ─── Internal helpers ────────────────────────────────────────────────────

    private Plan? LoadInternal(string planId)
    {
        var path = GetPlanPath(planId);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            var plan = JsonSerializer.Deserialize<Plan>(json, ReadOptions);
            if (plan is null ||
                !string.Equals(plan.PlanId, planId, StringComparison.Ordinal))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"Plan '{planId}' failed identity validation.");
                return null;
            }
            if (!PlanLifecycleStatus.All.Contains(plan.LifecycleStatus))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"Plan '{planId}' contains unknown lifecycle status '{plan.LifecycleStatus}'.");
                return null;
            }
            return plan;
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Plan '{planId}' contains invalid JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Plan '{planId}' could not be read: {ex.Message}");
            return null;
        }
    }

    private void EnsureDirectory() => Directory.CreateDirectory(_plansFolder);

    private string GetPlanPath(string planId) =>
        Path.Combine(_plansFolder, planId + ".json");
}
