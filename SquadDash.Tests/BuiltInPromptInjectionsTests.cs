using System.Text.RegularExpressions;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="BuiltInPromptInjections"/>.
///
/// A minimal fake evaluator (local static helper) is used instead of the real
/// <see cref="TriggeredInjectionEvaluator"/> so the tests stay isolated to the
/// injection definitions themselves — pattern, id, and injection text — rather than
/// coupling to evaluator implementation details.
/// </summary>
[TestFixture]
internal sealed class BuiltInPromptInjectionsTests {

    // ── Fake evaluator ────────────────────────────────────────────────────────
    // A stripped-down evaluator that only does pattern matching.
    // Variable substitution is exercised in TriggeredInjectionEvaluatorTests; we
    // don't repeat it here.

    private static bool Matches(TriggeredPromptInjection injection, string prompt)
        => Regex.IsMatch(prompt, injection.Pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Resolve(TriggeredPromptInjection injection, string workspaceFolder)
        => injection.InjectionText.Replace("{workspaceFolder}", workspaceFolder,
            StringComparison.OrdinalIgnoreCase);

    // ── Catalogue structure ───────────────────────────────────────────────────

    [Test]
    public void All_IsNotEmpty() {
        Assert.That(BuiltInPromptInjections.All, Is.Not.Empty);
    }

    [Test]
    public void All_ContainsTasksInjection() {
        Assert.That(
            BuiltInPromptInjections.All,
            Has.One.Matches<TriggeredPromptInjection>(i => i.Id == "builtin:tasks-guidance"));
    }

    [Test]
    public void Plans_PreservesTopLevelTasksJsonAfterPlanningDelegation() {
        Assert.Multiple(() => {
            Assert.That(BuiltInPromptInjections.Plans.InjectionText,
                Does.Contain("prefer the workspace's active planning specialist"));
            Assert.That(BuiltInPromptInjections.Plans.InjectionText,
                Does.Contain("shipped fallback Planning Architect charter"));
            Assert.That(BuiltInPromptInjections.Plans.InjectionText,
                Does.Contain("Coordinator must validate the complete candidate against the live protocol"));
            Assert.That(BuiltInPromptInjections.Plans.InjectionText,
                Does.Contain("exact task and validation ID grammar"));
            Assert.That(BuiltInPromptInjections.Plans.InjectionText,
                Does.Contain("complete rejected candidate and exact diagnostics"));
        });
    }

    [Test]
    public void All_HasNoEntriesWithDuplicateIds() {
        var ids = BuiltInPromptInjections.All.Select(i => i.Id).ToList();
        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void All_HasNoEntriesWithNullOrBlankPattern() {
        Assert.That(
            BuiltInPromptInjections.All,
            Has.None.Matches<TriggeredPromptInjection>(i => string.IsNullOrWhiteSpace(i.Pattern)));
    }

    [Test]
    public void All_HasNoEntriesWithNullOrBlankInjectionText() {
        Assert.That(
            BuiltInPromptInjections.All,
            Has.None.Matches<TriggeredPromptInjection>(i => string.IsNullOrWhiteSpace(i.InjectionText)));
    }

    [Test]
    public void All_PatternsAreValidRegularExpressions() {
        // Every pattern must compile and match without throwing.
        Assert.Multiple(() => {
            foreach (var injection in BuiltInPromptInjections.All) {
                Assert.DoesNotThrow(
                    () => Regex.IsMatch("probe", injection.Pattern, RegexOptions.IgnoreCase),
                    $"Pattern for '{injection.Id}' failed to compile.");
            }
        });
    }

    // ── Tasks injection — identity ────────────────────────────────────────────

    [Test]
    public void Tasks_HasStableId() {
        Assert.That(BuiltInPromptInjections.Tasks.Id, Is.EqualTo("builtin:tasks-guidance"));
    }

    [Test]
    public void Tasks_PatternIsNotEmpty() {
        Assert.That(BuiltInPromptInjections.Tasks.Pattern, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Tasks_InjectionTextIsNotEmpty() {
        Assert.That(BuiltInPromptInjections.Tasks.InjectionText, Is.Not.Null.And.Not.Empty);
    }

    // ── Tasks injection — pattern matching (via fake evaluator) ───────────────

    [TestCase("I need to add a new task")]
    [TestCase("create a task list for this sprint")]
    [TestCase("add a todo")]
    [TestCase("show me the todos")]
    [TestCase("what's on the backlog")]
    [TestCase("make a checklist")]
    [TestCase("add a task-list item")]
    // Voice-dictation variants where compound words are split by speech recognition
    [TestCase("add a to do item for me")]
    [TestCase("show me the to dos")]
    [TestCase("update the back log")]
    [TestCase("create a check list")]
    [TestCase("add a to-do")]
    public void Tasks_PatternFires_OnTaskRelatedPrompts(string prompt) {
        Assert.That(
            Matches(BuiltInPromptInjections.Tasks, prompt),
            Is.True,
            $"Expected Tasks pattern to match: \"{prompt}\"");
    }

    [TestCase("approve the last commit")]
    [TestCase("show me the transcript")]
    [TestCase("what is the weather today?")]
    [TestCase("restart the agent")]
    [TestCase("summarise the conversation")]
    [TestCase("")] // empty prompt
    public void Tasks_PatternDoesNotFire_OnUnrelatedPrompts(string prompt) {
        Assert.That(
            Matches(BuiltInPromptInjections.Tasks, prompt),
            Is.False,
            $"Expected Tasks pattern NOT to match: \"{prompt}\"");
    }

    // ── Tasks injection — content assertions (via fake resolver) ─────────────

    [Test]
    public void Tasks_InjectionText_ContainsExpectedFilePath_AfterResolution() {
        const string folder = @"C:\Source\MyProject";
        var resolved = Resolve(BuiltInPromptInjections.Tasks, folder);

        Assert.That(resolved, Does.Contain(@"C:\Source\MyProject\.squad\tasks.md"));
    }

    [Test]
    public void Tasks_InjectionText_ContainsAllPrioritySections() {
        Assert.Multiple(() => {
            Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
                Does.Contain("## 🔴 High Priority"),
                "Missing high-priority section");
            Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
                Does.Contain("## 🟡 Mid Priority"),
                "Missing mid-priority section");
            Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
                Does.Contain("## 🟢 Low Priority"),
                "Missing low-priority section");
        });
    }

    [Test]
    public void Tasks_InjectionText_ContainsDoneSection() {
        Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
            Does.Contain("## ✅ Done"));
    }

    [Test]
    public void Tasks_InjectionText_ContainsCheckboxFormat() {
        // The task-line format must be documented precisely in the injection text.
        Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
            Does.Contain("- [ ]"));
    }

    [Test]
    public void Tasks_InjectionText_DocumentsCompletedTaskFormat() {
        Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
            Does.Contain("- [x]"));
    }

    [Test]
    public void Tasks_InjectionText_ContainsWorkspaceFolderPlaceholder() {
        // The raw text must carry the placeholder so substitution can happen at runtime.
        Assert.That(BuiltInPromptInjections.Tasks.InjectionText,
            Does.Contain("{workspaceFolder}"));
    }

    [Test]
    public void Tasks_ResolvedInjectionText_DoesNotContainPlaceholder_AfterSubstitution() {
        var resolved = Resolve(BuiltInPromptInjections.Tasks, @"C:\Foo");
        Assert.That(resolved, Does.Not.Contain("{workspaceFolder}"));
    }

    // ── Edge: Tasks is the same object exposed in All ─────────────────────────

    [Test]
    public void Tasks_IsReferencedInAll() {
        Assert.That(BuiltInPromptInjections.All,
            Contains.Item(BuiltInPromptInjections.Tasks));
    }
}
