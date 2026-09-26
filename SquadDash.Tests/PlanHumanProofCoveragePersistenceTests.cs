using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanProofCoveragePersistenceTests
{
    [Test]
    public void Store_RoundTripsCompleteCorrelationStateAndRetryAttempt()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        var store = new PlanHumanProofCoverageCandidateStore(squadFolder);
        var pending = CreatePending("check-a", attempt: 0);

        store.Save(pending);
        var loaded = store.Load(out var error);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.CheckId, Is.EqualTo(pending.CheckId));
            Assert.That(loaded.CandidateHash, Is.EqualTo(pending.CandidateHash));
            Assert.That(loaded.RejectedCandidate, Is.EqualTo(pending.RejectedCandidate));
            Assert.That(loaded.AdditionalDiagnostics, Is.EqualTo(pending.AdditionalDiagnostics));
            Assert.That(loaded.Attempt, Is.Zero);
            Assert.That(loaded.Group?.GroupId, Is.EqualTo("PLAN-1"));
            Assert.That(loaded.Group?.Tasks.Single().Id, Is.EqualTo("PLAN-1-001"));
        });

        store.Save(pending with { Attempt = 1, Correction = "Return only the required envelope." });
        loaded = store.Load(out error);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(loaded!.Attempt, Is.EqualTo(1));
            Assert.That(loaded.Correction, Is.EqualTo("Return only the required envelope."));
        });
    }

    [Test]
    public void Store_RejectsCandidateThatNoLongerMatchesDurableCorrelation()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        var store = new PlanHumanProofCoverageCandidateStore(squadFolder);
        var pending = CreatePending("check-a", attempt: 0);
        store.Save(pending);

        var candidatePath = Path.Combine(
            squadFolder, "tmp", "decompose", "preflight", pending.CheckId + ".txt");
        File.WriteAllText(candidatePath, pending.RejectedCandidate + "\nchanged");

        var loaded = store.Load(out var error);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Null);
            Assert.That(error, Does.Contain("did not match its transaction state"));
        });
    }

    [Test]
    public void Delete_ForOlderCompletion_DoesNotEraseNewerPendingTransaction()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        var store = new PlanHumanProofCoverageCandidateStore(squadFolder);
        var older = CreatePending("check-old", attempt: 0);
        var newer = CreatePending("check-new", attempt: 0);
        store.Save(older);
        store.Save(newer);

        store.Delete(older.CheckId);
        var loaded = store.Load(out var error);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(loaded?.CheckId, Is.EqualTo(newer.CheckId));
            Assert.That(File.Exists(Path.Combine(
                squadFolder, "tmp", "decompose", "preflight", newer.CheckId + ".txt")), Is.True);
        });
    }

    [Test]
    public void Delete_ForCurrentCompletion_RemovesTransactionAndCandidate()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        var store = new PlanHumanProofCoverageCandidateStore(squadFolder);
        var pending = CreatePending("check-a", attempt: 0);
        store.Save(pending);

        store.Delete(pending.CheckId);
        var loaded = store.Load(out var error);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Null);
            Assert.That(error, Is.Null);
            Assert.That(File.Exists(Path.Combine(
                squadFolder, "tmp", "decompose", "preflight", pending.CheckId + ".txt")), Is.False);
        });
    }

    private static PendingPlanHumanProofCoverageReview CreatePending(string checkId, int attempt)
    {
        var group = new DecomposedTaskGroup(
            "PLAN-1",
            "Durable preflight",
            "feature/durable-preflight",
            "Prove restart recovery.",
            [new DecomposedSubTask(
                "PLAN-1-001",
                "Persist and restore the semantic preflight correlation.",
                [],
                "high",
                Title: "Persist semantic preflight")]);
        var candidate = "TASKS_JSON:\n" + System.Text.Json.JsonSerializer.Serialize(group);
        return new PendingPlanHumanProofCoverageReview(
            checkId,
            PlanHumanProofCoverageReview.ComputeCandidateHash(candidate),
            group,
            candidate,
            ["existing structural diagnostic"],
            attempt);
    }
}
