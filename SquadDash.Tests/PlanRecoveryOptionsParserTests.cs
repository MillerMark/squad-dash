using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryOptionsParserTests
{
    // ── TryParse ─────────────────────────────────────────────────────────────

    [Test]
    public void TryParse_ValidPayload_ReturnsTrue()
    {
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "PLANS-001",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": [
                { "id": "opt1", "label": "Retry", "description": "Retry task.", "action": "clean-retry", "viable": true }
              ]
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out var response);

        Assert.That(result, Is.True);
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.GroupId, Is.EqualTo("PLANS-001"));
        Assert.That(response.TaskId, Is.EqualTo("T1"));
        Assert.That(response.Revision, Is.EqualTo("rev-abc"));
        Assert.That(response.Options, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryParse_EmptyGroupId_ReturnsFalse()
    {
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": [
                { "id": "opt1", "label": "Retry", "description": "Retry task.", "action": "clean-retry", "viable": true }
              ]
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_EmptyOptions_ReturnsFalse()
    {
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "PLANS-001",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": []
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_InvalidAction_ReturnsFalse()
    {
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "PLANS-001",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": [
                { "id": "opt1", "label": "Magic", "description": "Do magic.", "action": "delete-repo", "viable": true }
              ]
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_ValidAllActions_ReturnsTrue()
    {
        var actions = new[] { "adopt-commit", "partial-adopt", "revert-and-retry", "clean-retry", "replan" };
        foreach (var action in actions)
        {
            var json = $$"""
                PLAN_RECOVERY_OPTIONS_JSON:
                {
                  "groupId": "PLANS-001",
                  "taskId": "T1",
                  "revision": "rev-abc",
                  "options": [
                    { "id": "opt1", "label": "Option", "description": "Desc.", "action": "{{action}}", "viable": true }
                  ]
                }
                """;

            var result = PlanRecoveryOptionsParser.TryParse(json, out var response);
            Assert.That(result, Is.True, $"Expected TryParse to return true for action '{action}'");
            Assert.That(response!.Options[0].Action, Is.EqualTo(action));
        }
    }

    [Test]
    public void TryParse_MissingMarker_ReturnsFalse()
    {
        var json = """
            {
              "groupId": "PLANS-001",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": [
                { "id": "opt1", "label": "Retry", "description": "Retry task.", "action": "clean-retry", "viable": true }
              ]
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_NullInput_ReturnsFalse()
    {
        var result = PlanRecoveryOptionsParser.TryParse(null, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_WithRecommendation_ParsesRecommendation()
    {
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "PLANS-001",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": [
                { "id": "opt1", "label": "Retry", "description": "Retry.", "action": "clean-retry", "viable": true }
              ],
              "recommendation": "opt1"
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out var response);

        Assert.That(result, Is.True);
        Assert.That(response!.Recommendation, Is.EqualTo("opt1"));
    }

    [Test]
    public void TryParse_WithSummary_ParsesSummary()
    {
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "PLANS-001",
              "taskId": "T1",
              "revision": "rev-abc",
              "options": [
                { "id": "opt1", "label": "Retry", "description": "Retry.", "action": "clean-retry", "viable": true }
              ],
              "summary": "Evidence suggests a clean retry is the safest path."
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out var response);

        Assert.That(result, Is.True);
        Assert.That(response!.Summary, Is.EqualTo("Evidence suggests a clean retry is the safest path."));
    }

    [Test]
    public void TryParse_UsesLastMarker()
    {
        // When multiple markers appear, the last one should be used.
        var json = """
            PLAN_RECOVERY_OPTIONS_JSON:
            { "groupId": "OLD", "taskId": "T0", "revision": "rev-old",
              "options": [{ "id": "x", "label": "X", "description": "Old.", "action": "replan", "viable": true }] }

            Some text in between.

            PLAN_RECOVERY_OPTIONS_JSON:
            {
              "groupId": "PLANS-002",
              "taskId": "T2",
              "revision": "rev-new",
              "options": [
                { "id": "opt2", "label": "Replan", "description": "Break it up.", "action": "replan", "viable": true }
              ]
            }
            """;

        var result = PlanRecoveryOptionsParser.TryParse(json, out var response);

        Assert.That(result, Is.True);
        Assert.That(response!.GroupId, Is.EqualTo("PLANS-002"));
    }

    // ── ValidateRecoveryViability ────────────────────────────────────────────

    [Test]
    public void ValidateRecoveryViability_AdoptCommit_ViableWhenCandidateExists()
    {
        var options = new List<PlanRecoveryOption>
        {
            new("adopt", "Adopt", "Adopt the commit.", "adopt-commit", false)
        };

        var result = PlanRecoveryOptionsParser.ValidateRecoveryViability(options, hasCandidateCommit: true, hasUncommittedWork: false);

        Assert.That(result[0].Viable, Is.True);
    }

    [Test]
    public void ValidateRecoveryViability_AdoptCommit_NotViableWhenNoCandidateCommit()
    {
        var options = new List<PlanRecoveryOption>
        {
            new("adopt", "Adopt", "Adopt the commit.", "adopt-commit", true)
        };

        var result = PlanRecoveryOptionsParser.ValidateRecoveryViability(options, hasCandidateCommit: false, hasUncommittedWork: false);

        Assert.That(result[0].Viable, Is.False);
    }

    [Test]
    public void ValidateRecoveryViability_Replan_AlwaysViable()
    {
        var options = new List<PlanRecoveryOption>
        {
            new("rp", "Replan", "Break it up.", "replan", false)
        };

        var result = PlanRecoveryOptionsParser.ValidateRecoveryViability(options, hasCandidateCommit: false, hasUncommittedWork: false);

        Assert.That(result[0].Viable, Is.True);
    }

    [Test]
    public void ValidateRecoveryViability_CleanRetry_AlwaysViable()
    {
        var options = new List<PlanRecoveryOption>
        {
            new("cr", "Clean retry", "Retry from scratch.", "clean-retry", false)
        };

        var result = PlanRecoveryOptionsParser.ValidateRecoveryViability(options, hasCandidateCommit: false, hasUncommittedWork: false);

        Assert.That(result[0].Viable, Is.True);
    }
}
