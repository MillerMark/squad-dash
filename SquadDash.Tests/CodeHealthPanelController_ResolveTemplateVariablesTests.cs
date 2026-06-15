using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="CodeHealthPanelController.ResolveAndMarkTemplateVariables"/>.
/// Pure string-manipulation tests — no WPF STA thread required.
/// </summary>
[TestFixture]
internal sealed class CodeHealthPanelController_ResolveTemplateVariablesTests {

    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private const char S = CodeHealthPanelController.HighlightSentinel;

    private CodeHealthStateStore MakeStore(Func<string, string, Task<int>>? commitCounter = null) =>
        new(_workspace.RootPath, commitCounter: commitCounter);

    // ── Null / empty inputs ───────────────────────────────────────────────────

    [Test]
    public void NullInstructions_ReturnsEmpty() {
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            null, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void EmptyInstructions_ReturnsEmpty() {
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            string.Empty, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    // ── No template variables ─────────────────────────────────────────────────

    [Test]
    public void NoVariables_ReturnsUnchanged() {
        const string text = "Review all the changes in the workspace.";
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    // ── {{last_reviewed_sha}} ─────────────────────────────────────────────────

    [Test]
    public void LastReviewedSha_NullStore_SubstitutesNone() {
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "Since {{last_reviewed_sha}}", "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo($"Since {S}(none){S}"));
    }

    [Test]
    public void LastReviewedSha_StoreHasNoRecord_SubstitutesNone() {
        var store = MakeStore();
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "SHA: {{last_reviewed_sha}}", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"SHA: {S}(none){S}"));
    }

    [Test]
    public void LastReviewedSha_StoreHasValue_SubstitutesWrappedSha() {
        var store = MakeStore();
        store.RecordRun("t1", "deadbeef1234");
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "SHA: {{last_reviewed_sha}}", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"SHA: {S}deadbeef1234{S}"));
    }

    [Test]
    public void LastReviewedSha_StoreHasEmptyString_SubstitutesNone() {
        var store = MakeStore();
        store.RecordRun("t1", commitSha: "");
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "{{last_reviewed_sha}}", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"{S}(none){S}"));
    }

    // ── {{new_commit_count}} ──────────────────────────────────────────────────

    [Test]
    public void NewCommitCount_NullWorkspacePath_SubstitutesPending() {
        var store = MakeStore();
        store.RecordRun("t1", "abc");
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} commits", "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo($"{S}(pending){S} commits"));
    }

    [Test]
    public void NewCommitCount_EmptyWorkspacePath_SubstitutesPending() {
        var store = MakeStore();
        store.RecordRun("t1", "abc");
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} commits", "t1", store, workspacePath: string.Empty);
        Assert.That(result, Is.EqualTo($"{S}(pending){S} commits"));
    }

    [Test]
    public void NewCommitCount_NullStore_SubstitutesUnknown() {
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} commits", "t1", stateStore: null, workspacePath: @"C:\repo");
        Assert.That(result, Is.EqualTo($"{S}(unknown){S} commits"));
    }

    [Test]
    public void NewCommitCount_WithWorkspaceAndStore_SubstitutesCount() {
        var store = MakeStore(commitCounter: (_, _) => Task.FromResult(7));
        store.RecordRun("t1", "abc123");
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "{{new_commit_count}} new commits", "t1", store, workspacePath: @"C:\repo");
        Assert.That(result, Is.EqualTo($"{S}7{S} new commits"));
    }

    // ── Multiple variables ────────────────────────────────────────────────────

    [Test]
    public void MultipleVariables_BothResolved() {
        var store = MakeStore(commitCounter: (_, _) => Task.FromResult(3));
        store.RecordRun("t1", "feedcafe");
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            "Since {{last_reviewed_sha}} ({{new_commit_count}} commits)",
            "t1", store, workspacePath: @"C:\repo");
        Assert.That(result, Is.EqualTo($"Since {S}feedcafe{S} ({S}3{S} commits)"));
    }

    // ── Conditional / unknown variables not resolved ──────────────────────────

    [Test]
    public void ConditionalIfBlock_NotResolved() {
        const string text = "{{#if condition}}some text{{/if}}";
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    [Test]
    public void UnknownVariable_LeftAsIs() {
        const string text = "Deploy to {{environment}} branch {{branch}}";
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    [Test]
    public void SlashVariable_LeftAsIs() {
        const string text = "End {{/if}}";
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            text, "t1", stateStore: null, workspacePath: null);
        Assert.That(result, Is.EqualTo(text));
    }

    // ── Mixed known and unknown ───────────────────────────────────────────────

    [Test]
    public void KnownAndUnknownVariables_OnlyKnownResolved() {
        var store = MakeStore();
        store.RecordRun("t1", "c0ffee");
        const string template = "Branch {{branch}}, since {{last_reviewed_sha}}, count {{new_commit_count}}";
        var result = CodeHealthPanelController.ResolveAndMarkTemplateVariables(
            template, "t1", store, workspacePath: null);
        Assert.That(result, Is.EqualTo(
            $"Branch {{{{branch}}}}, since {S}c0ffee{S}, count {S}(pending){S}"));
    }
}

