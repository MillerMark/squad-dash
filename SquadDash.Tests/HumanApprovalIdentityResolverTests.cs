using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class HumanApprovalIdentityResolverTests
{
    [TearDown]
    public void TearDown() => HumanApprovalIdentityResolver.ClearCache();

    // ── FormatIdentity (pure formatting) ──────────────────────────────────────

    [Test]
    public void FormatIdentity_NameAndLogin_CombinesBoth()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity("Alice Smith", null, "alicesmith");
        Assert.That(result, Is.EqualTo("Alice Smith (@alicesmith)"));
    }

    [Test]
    public void FormatIdentity_LoginOnly_PrefixesAtSign()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity(null, "alice@example.com", "alicesmith");
        Assert.That(result, Is.EqualTo("@alicesmith"));
    }

    [Test]
    public void FormatIdentity_LoginWithAtPrefix_DoesNotDouble()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity("Alice", null, "@alice");
        Assert.That(result, Is.EqualTo("Alice (@alice)"));
    }

    [Test]
    public void FormatIdentity_NameOnly_ReturnsName()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity("Bob Jones", "bob@co.com", null);
        Assert.That(result, Is.EqualTo("Bob Jones"));
    }

    [Test]
    public void FormatIdentity_EmailOnly_ReturnsEmail()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity(null, "dev@example.com", null);
        Assert.That(result, Is.EqualTo("dev@example.com"));
    }

    [Test]
    public void FormatIdentity_NothingAvailable_FallsToUserName()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity(null, null, null);
        Assert.That(result, Is.EqualTo(Environment.UserName));
    }

    [Test]
    public void FormatIdentity_WhitespaceInputs_TreatedAsEmpty()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity("  ", "  ", "  ");
        Assert.That(result, Is.EqualTo(Environment.UserName));
    }

    [Test]
    public void FormatIdentity_TrimsWhitespace()
    {
        var result = HumanApprovalIdentityResolver.FormatIdentity("  Alice  ", null, "  alice  ");
        Assert.That(result, Is.EqualTo("Alice (@alice)"));
    }

    // ── ResolveAsync with injectable runner ───────────────────────────────────

    [Test]
    public async Task ResolveAsync_GitOnlyNoGh_UsesNameAlone()
    {
        var runner = new FakeCommandRunner(new Dictionary<string, string?>
        {
            ["git"] = "Mark Miller",
        });

        var result = await HumanApprovalIdentityResolver.ResolveAsync("C:\\test", runner);
        Assert.That(result, Is.EqualTo("Mark Miller"));
    }

    [Test]
    public async Task ResolveAsync_GitAndGh_CombinesIdentity()
    {
        var runner = new FakeCommandRunner(new Dictionary<string, string?>
        {
            ["git:user.name"] = "Mark Miller",
            ["git:user.email"] = "mark@example.com",
            ["gh"] = "MillerMark",
        });

        var result = await HumanApprovalIdentityResolver.ResolveAsync("C:\\test", runner);
        Assert.That(result, Is.EqualTo("Mark Miller (@MillerMark)"));
    }

    [Test]
    public async Task ResolveAsync_GhTimesOut_FallsBackToGit()
    {
        var runner = new TimeoutCommandRunner(timeoutExecutable: "gh",
            responses: new Dictionary<string, string?>
            {
                ["git:user.name"] = "Fallback User",
                ["git:user.email"] = "fallback@test.com",
            });

        var result = await HumanApprovalIdentityResolver.ResolveAsync("C:\\timeout-test", runner);
        Assert.That(result, Is.EqualTo("Fallback User"));
    }

    [Test]
    public async Task ResolveAsync_AllCommandsFail_FallsToEnvironmentUserName()
    {
        var runner = new FakeCommandRunner(new Dictionary<string, string?>());

        var result = await HumanApprovalIdentityResolver.ResolveAsync("C:\\empty", runner);
        Assert.That(result, Is.EqualTo(Environment.UserName));
    }

    [Test]
    public async Task ResolveAsync_CachesResultPerWorkspace()
    {
        var runner = new CountingCommandRunner("CachedUser");

        var first = await HumanApprovalIdentityResolver.ResolveAsync("C:\\cache-test", runner);
        var second = await HumanApprovalIdentityResolver.ResolveAsync("C:\\cache-test", runner);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("CachedUser"));
            Assert.That(second, Is.EqualTo("CachedUser"));
            Assert.That(runner.CallCount, Is.EqualTo(3), "Should only call runner once (3 commands for first resolve)");
        });
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    private sealed class FakeCommandRunner : IIdentityCommandRunner
    {
        private readonly Dictionary<string, string?> _responses;

        internal FakeCommandRunner(Dictionary<string, string?> responses) =>
            _responses = responses;

        public Task<string?> RunAsync(string executable, string workspace, CancellationToken cancellationToken, params string[] arguments)
        {
            // Try specific key first (e.g. "git:user.name"), then executable-only
            var specificKey = arguments.Length >= 2 ? $"{executable}:{arguments[^1]}" : executable;
            if (_responses.TryGetValue(specificKey, out var specific))
                return Task.FromResult(specific);
            _responses.TryGetValue(executable, out var result);
            return Task.FromResult(result);
        }
    }

    private sealed class TimeoutCommandRunner : IIdentityCommandRunner
    {
        private readonly string _timeoutExecutable;
        private readonly Dictionary<string, string?> _responses;

        internal TimeoutCommandRunner(string timeoutExecutable, Dictionary<string, string?> responses)
        {
            _timeoutExecutable = timeoutExecutable;
            _responses = responses;
        }

        public Task<string?> RunAsync(string executable, string workspace, CancellationToken cancellationToken, params string[] arguments)
        {
            if (string.Equals(executable, _timeoutExecutable, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<string?>(null); // simulates timeout/failure
            var specificKey = arguments.Length >= 2 ? $"{executable}:{arguments[^1]}" : executable;
            if (_responses.TryGetValue(specificKey, out var specific))
                return Task.FromResult(specific);
            _responses.TryGetValue(executable, out var result);
            return Task.FromResult(result);
        }
    }

    private sealed class CountingCommandRunner : IIdentityCommandRunner
    {
        private readonly string _name;
        internal int CallCount;

        internal CountingCommandRunner(string name) => _name = name;

        public Task<string?> RunAsync(string executable, string workspace, CancellationToken cancellationToken, params string[] arguments)
        {
            Interlocked.Increment(ref CallCount);
            if (executable == "git" && arguments.Length >= 2 && arguments[^1] == "user.name")
                return Task.FromResult<string?>(_name);
            return Task.FromResult<string?>(null);
        }
    }
}

[TestFixture]
internal sealed class ApprovalIdentitySerializationTests
{
    [Test]
    public void PlanApprovalGate_RoundTripsResolvedBy()
    {
        var gate = new PlanApprovalGate(
            "GATE-1", "Review checkpoint", ["A"], ["B"],
            PlanGateStatus.Approved,
            RequestedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            ResolvedAt: DateTimeOffset.UtcNow,
            ResolvedBy: "Alice Smith (@alice)");

        var json = JsonSerializer.Serialize(gate);
        var deserialized = JsonSerializer.Deserialize<PlanApprovalGate>(json);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.ResolvedBy, Is.EqualTo("Alice Smith (@alice)"));
            Assert.That(deserialized.ResolvedAt, Is.Not.Null);
            Assert.That(deserialized.Status, Is.EqualTo(PlanGateStatus.Approved));
        });
    }

    [Test]
    public void PlanApprovalGate_NullResolvedBy_OmittedFromJson()
    {
        var gate = new PlanApprovalGate(
            "GATE-1", "Review", ["A"], ["B"], PlanGateStatus.Pending);

        var json = JsonSerializer.Serialize(gate);

        Assert.That(json, Does.Not.Contain("resolvedBy"));
    }

    [Test]
    public void PlanApprovalGate_DeserializesWithMissingResolvedBy()
    {
        var json = """{"gateId":"G1","message":"Test","afterTaskIds":["A"],"beforeTaskIds":["B"],"status":"approved","resolvedAt":"2026-01-01T00:00:00+00:00"}""";

        var gate = JsonSerializer.Deserialize<PlanApprovalGate>(json);

        Assert.Multiple(() =>
        {
            Assert.That(gate, Is.Not.Null);
            Assert.That(gate!.ResolvedBy, Is.Null);
            Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Approved));
        });
    }

    [Test]
    public void PlanApprovalGate_ResolvedBy_SurvivesRoundTrip_WithSpecialCharacters()
    {
        var identity = "José García (@jgarcia)";
        var gate = new PlanApprovalGate(
            "GATE-1", "Check", ["A"], ["B"], PlanGateStatus.Approved,
            ResolvedBy: identity);

        var json = JsonSerializer.Serialize(gate);
        var deserialized = JsonSerializer.Deserialize<PlanApprovalGate>(json);

        Assert.That(deserialized!.ResolvedBy, Is.EqualTo(identity));
    }
}

[TestFixture]
internal sealed class ApprovalResolvedTooltipPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 14, 30, 0, TimeSpan.Zero);

    [Test]
    public void Build_NullGate_ShowsLocationOnly()
    {
        var text = ApprovalResolvedTooltipPresentation.Build(null, "this stage milestone", Now);
        Assert.That(text, Is.EqualTo("Human approval was granted this stage milestone."));
    }

    [Test]
    public void Build_WithResolvedBy_IncludesIdentity()
    {
        var gate = MakeGate(resolvedBy: "Alice (@alice)");

        var text = ApprovalResolvedTooltipPresentation.Build(gate, "before this task began", Now);

        Assert.That(text, Does.Contain("Approved by Alice (@alice)."));
    }

    [Test]
    public void Build_WithResolvedAt_IncludesRelativeTime()
    {
        var gate = MakeGate(resolvedAt: Now.AddMinutes(-5));

        var text = ApprovalResolvedTooltipPresentation.Build(gate, "after this task completed", Now);

        Assert.That(text, Does.Contain("5 minutes ago"));
    }

    [Test]
    public void Build_WithNote_IncludesNote()
    {
        var gate = MakeGate(resolutionNote: "LGTM");

        var text = ApprovalResolvedTooltipPresentation.Build(gate, "here", Now);

        Assert.That(text, Does.Contain("Note: LGTM"));
    }

    [Test]
    public void Build_FullGate_ShowsAllSections()
    {
        var gate = MakeGate(
            resolvedBy: "Bob (@bob)",
            resolvedAt: Now.AddMinutes(-2),
            resolutionNote: "Ship it");

        var text = ApprovalResolvedTooltipPresentation.Build(gate, "this ALL join", Now);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Human approval was granted this ALL join."));
            Assert.That(text, Does.Contain("Approved by Bob (@bob)."));
            Assert.That(text, Does.Contain("2 minutes ago"));
            Assert.That(text, Does.Contain("Note: Ship it"));
        });
    }

    [Test]
    public void Build_UsesStatusTimingPresentation_ForRelativeTime()
    {
        var resolvedAt = Now.AddHours(-2).AddMinutes(-15);
        var gate = MakeGate(resolvedAt: resolvedAt);

        var text = ApprovalResolvedTooltipPresentation.Build(gate, "here", Now);
        var expectedTiming = StatusTimingPresentation.FormatRelativeTimestamp(resolvedAt, Now);

        Assert.That(text, Does.Contain(expectedTiming));
    }

    private static PlanApprovalGate MakeGate(
        string? resolvedBy = null,
        DateTimeOffset? resolvedAt = null,
        string? resolutionNote = null) =>
        new("GATE-1", "Check", ["A"], ["B"], PlanGateStatus.Approved,
            ResolvedAt: resolvedAt, ResolvedBy: resolvedBy, ResolutionNote: resolutionNote);
}

[TestFixture]
internal sealed class ApprovalReworkClearsAttributionTests
{
    [Test]
    public void ApplyGateReworkRequested_ClearsResolvedBy()
    {
        var plan = MakePlanWithApprovedGate("Alice (@alice)");
        var reworkPlan = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GATE-1", ["A"], "Please fix the bug");

        var gate = reworkPlan.ApprovalGates[0];
        Assert.Multiple(() =>
        {
            Assert.That(gate.ResolvedBy, Is.Null);
            Assert.That(gate.ResolvedAt, Is.Null);
            Assert.That(gate.ResolutionNote, Is.Null);
            Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Pending));
        });
    }

    [Test]
    public void ApplyGateReworkRequested_ClearsTimestampFields()
    {
        var plan = MakePlanWithApprovedGate("Bob (@bob)");
        var reworkPlan = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GATE-1", ["A"], "Rework needed");

        var gate = reworkPlan.ApprovalGates[0];
        Assert.Multiple(() =>
        {
            Assert.That(gate.RequestedAt, Is.Null);
            Assert.That(gate.NotifiedAt, Is.Null);
            Assert.That(gate.ReworkCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyGateApproved_SetsResolvedByAndTimestamp()
    {
        var plan = MakePlanAwaitingApproval();
        var approvedPlan = PlanStoreUpdater.ApplyGateApproved(
            plan, "GATE-1", "Looks good!", "Carol (@carol)");

        var gate = approvedPlan.ApprovalGates[0];
        Assert.Multiple(() =>
        {
            Assert.That(gate.ResolvedBy, Is.EqualTo("Carol (@carol)"));
            Assert.That(gate.ResolvedAt, Is.Not.Null);
            Assert.That(gate.ResolutionNote, Is.EqualTo("Looks good!"));
            Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Approved));
        });
    }

    private static Plan MakePlanWithApprovedGate(string resolvedBy)
    {
        var tasks = new[]
        {
            new PlanTask("A", "Task A", "Do A", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("B", "Task B", "Do B", ["A"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            "GATE-1", "Review A", ["A"], ["B"], PlanGateStatus.AwaitingApproval,
            RequestedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            NotifiedAt: DateTimeOffset.UtcNow.AddMinutes(-29),
            ResolvedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ResolvedBy: resolvedBy,
            ResolutionNote: "Approved");
        return new Plan("PLAN-1", "rev1", "manual", PlanLifecycleStatus.AwaitingApproval,
            "Test Plan", "main", "A test", tasks, [gate], new PlanProgress(1, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static Plan MakePlanAwaitingApproval()
    {
        var tasks = new[]
        {
            new PlanTask("A", "Task A", "Do A", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("B", "Task B", "Do B", ["A"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            "GATE-1", "Review A", ["A"], ["B"], PlanGateStatus.AwaitingApproval,
            RequestedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        return new Plan("PLAN-1", "rev1", "manual", PlanLifecycleStatus.AwaitingApproval,
            "Test Plan", "main", "A test", tasks, [gate], new PlanProgress(1, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }
}
