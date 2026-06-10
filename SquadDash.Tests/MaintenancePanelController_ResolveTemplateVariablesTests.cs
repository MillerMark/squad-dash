using System;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="MaintenancePanelController.ResolveAndMarkTemplateVariables"/>.
/// Pure string-manipulation tests — no WPF STA thread required.
/// </summary>
[TestFixture]
internal sealed class MaintenancePanelController_ResolveTemplateVariablesTests {

    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private const char S = MaintenancePanelController.HighlightSentinel;

    private MaintenanceStateStore MakeStore(Func<string, string, int>? commitCounter = null) =>
        new(_workspace.RootPath, commitCounter: commitCounter);

    // ── Null / empty inputs ───────────────────────────────────────────────────

    [Test]
    public void NullInstructions_ReturnsEmpty() {
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            null, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void EmptyInstructions_ReturnsEmpty() {
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            string.Empty, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    // ── No template variables ─────────────────────────────────────────────────

    [Test]
    public void NoVariables_ReturnsUnchanged() {
        const string text = "Review all the changes in the workspace.";
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    // ── {{last_reviewed_sha}} ─────────────────────────────────────────────────

    [Test]
    public void LastReviewedSha_NullStore_SubstitutesNone() {
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "Since {{last_reviewed_sha}}", "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo($"Since {S}(none){S}"));
    }

    [Test]
    public void LastReviewedSha_StoreHasNoRecord_SubstitutesNone() {
        var store = MakeStore();
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "SHA: {{last_reviewed_sha}}", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"SHA: {S}(none){S}"));
    }

    [Test]
    public void LastReviewedSha_StoreHasValue_SubstitutesWrappedSha() {
        var store = MakeStore();
        store.RecordRun("t1", "deadbeef1234");
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "SHA: {{last_reviewed_sha}}", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"SHA: {S}deadbeef1234{S}"));
    }

    [Test]
    public void LastReviewedSha_StoreHasEmptyString_SubstitutesNone() {
        var store = MakeStore();
        store.RecordRun("t1", commitSha: "");
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "{{last_reviewed_sha}}", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"{S}(none){S}"));
    }

    // ── {{new_commit_count}} ──────────────────────────────────────────────────

    [Test]
    public void NewCommitCount_NullWorkspacePath_SubstitutesPending() {
        var store = MakeStore();
        store.RecordRun("t1", "abc");
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} commits", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"{S}(pending){S} commits"));
    }

    [Test]
    public void NewCommitCount_EmptyWorkspacePath_SubstitutesPending() {
        var store = MakeStore();
        store.RecordRun("t1", "abc");
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} commits", "t1", store, workspacePath: string.Empty);
        Assert.That(result, Is.EqualTo($"{S}(pending){S} commits"));
    }

    [Test]
    public void NewCommitCount_NullStore_SubstitutesUnknown() {
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} commits", "t1", stateStore: null, workspacePath: @"C:\repo");
        Assert.That(result, Is.EqualTo($"{S}(unknown){S} commits"));
    }

    [Test]
    public void NewCommitCount_WithWorkspaceAndStore_SubstitutesCount() {
        var store = MakeStore(commitCounter: (_, _) => 7);
        store.RecordRun("t1", "abc123");
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} new commits", "t1", store, workspacePath: @"C:\repo");
        Assert.That(result, Is.EqualTo($"{S}7{S} new commits"));
    }

    // ── Multiple variables ────────────────────────────────────────────────────

    [Test]
    public void MultipleVariables_BothResolved() {
        var store = MakeStore(commitCounter: (_, _) => 3);
        store.RecordRun("t1", "feedcafe");
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            "Since {{last_reviewed_sha}} ({{new_commit_count}} commits)",
            "t1", store, workspacePath: @"C:\repo");
        Assert.That(result, Is.EqualTo($"Since {S}feedcafe{S} ({S}3{S} commits)"));
    }

    // ── Conditional / unknown variables not resolved ──────────────────────────

    [Test]
    public void ConditionalIfBlock_NotResolved() {
        const string text = "{{#if condition}}some text{{/if}}";
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    [Test]
    public void UnknownVariable_LeftAsIs() {
        const string text = "Deploy to {{environment}} branch {{branch}}";
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    [Test]
    public void SlashVariable_LeftAsIs() {
        const string text = "End {{/if}}";
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    // ── Mixed known and unknown ───────────────────────────────────────────────

    [Test]
    public void KnownAndUnknownVariables_OnlyKnownResolved() {
        var store = MakeStore();
        store.RecordRun("t1", "c0ffee");
        const string template = "Branch {{branch}}, since {{last_reviewed_sha}}, count {{new_commit_count}}";
        var result = MaintenancePanelController.ResolveAndMarkTemplateVariables(
            template, "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo(
            $"Branch {{{{branch}}}}, since {S}c0ffee{S}, count {S}(pending){S}"));
    }
}
