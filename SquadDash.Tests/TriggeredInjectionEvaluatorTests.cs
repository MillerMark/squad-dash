using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TriggeredInjectionEvaluatorTests {

    private static readonly TriggeredPromptInjection _alphaInjection = new(
        Id:            "test:alpha",
        Pattern:       @"\balpha\b",
        InjectionText: "Alpha guidance for {workspaceFolder}.");

    private static readonly TriggeredPromptInjection _betaInjection = new(
        Id:            "test:beta",
        Pattern:       @"\bbeta\b",
        InjectionText: "Beta guidance, no vars.");

    private static readonly IReadOnlyDictionary<string, string> _vars =
        new Dictionary<string, string> { ["workspaceFolder"] = @"C:\MyProject" };

    private static readonly IReadOnlyDictionary<string, string> _emptyVars =
        new Dictionary<string, string>();

    // ── Matching ─────────────────────────────────────────────────────────────

    [Test]
    public void Evaluate_NoMatch_ReturnsEmpty() {
        var result = TriggeredInjectionEvaluator.Evaluate(
            "hello world",
            [_alphaInjection],
            _emptyVars);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Evaluate_SingleMatch_ReturnsOne() {
        var result = TriggeredInjectionEvaluator.Evaluate(
            "Please add an alpha feature",
            [_alphaInjection, _betaInjection],
            _emptyVars);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Injection.Id, Is.EqualTo("test:alpha"));
    }

    [Test]
    public void Evaluate_BothMatch_ReturnsBoth() {
        var result = TriggeredInjectionEvaluator.Evaluate(
            "alpha and beta work together",
            [_alphaInjection, _betaInjection],
            _emptyVars);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void Evaluate_IsCaseInsensitive() {
        var result = TriggeredInjectionEvaluator.Evaluate(
            "ALPHA test",
            [_alphaInjection],
            _emptyVars);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void Evaluate_EmptyPrompt_ReturnsEmpty() {
        var result = TriggeredInjectionEvaluator.Evaluate("", [_alphaInjection], _vars);
        Assert.That(result, Is.Empty);
    }

    // ── Variable substitution ─────────────────────────────────────────────

    [Test]
    public void Evaluate_SubstitutesWorkspaceFolder() {
        var result = TriggeredInjectionEvaluator.Evaluate(
            "alpha feature please",
            [_alphaInjection],
            _vars);

        Assert.That(result[0].ResolvedText, Does.Contain(@"C:\MyProject"));
        Assert.That(result[0].ResolvedText, Does.Not.Contain("{workspaceFolder}"));
    }

    [Test]
    public void Evaluate_UnknownVariable_LeftAsPlaceholder() {
        // If no value for a variable is provided, the placeholder stays — better than crashing.
        var result = TriggeredInjectionEvaluator.Evaluate(
            "alpha test",
            [_alphaInjection],
            _emptyVars);

        Assert.That(result[0].ResolvedText, Does.Contain("{workspaceFolder}"));
    }

    // ── BuildVariables ────────────────────────────────────────────────────

    [Test]
    public void BuildVariables_WithFolder_ContainsEntry() {
        var vars = TriggeredInjectionEvaluator.BuildVariables(@"C:\Foo");
        Assert.That(vars, Does.ContainKey("workspaceFolder"));
        Assert.That(vars["workspaceFolder"], Is.EqualTo(@"C:\Foo"));
    }

    [Test]
    public void BuildVariables_NullFolder_ReturnsEmpty() {
        var vars = TriggeredInjectionEvaluator.BuildVariables(null);
        Assert.That(vars, Is.Empty);
    }

    // ── Malformed regex ───────────────────────────────────────────────────

    [Test]
    public void Evaluate_MalformedPattern_SkipsGracefully() {
        var bad = new TriggeredPromptInjection("test:bad", "[invalid(regex", "Bad injection.");
        Assert.DoesNotThrow(() => {
            var result = TriggeredInjectionEvaluator.Evaluate("some prompt", [bad], _emptyVars);
            Assert.That(result, Is.Empty);
        });
    }

    // ── Built-in: tasks ───────────────────────────────────────────────────

    [TestCase("I need to add a new task")]
    [TestCase("Can you create a task list for me?")]
    [TestCase("Add a task to the backlog")]
    [TestCase("Show me the todos")]
    [TestCase("I want a checklist")]
    [TestCase("create a to-do")]
    public void BuiltIn_Tasks_FiresOnTaskRelatedPrompts(string prompt) {
        var vars = TriggeredInjectionEvaluator.BuildVariables(@"C:\Source\MyProject");
        var result = TriggeredInjectionEvaluator.Evaluate(
            prompt,
            BuiltInPromptInjections.All,
            vars);

        Assert.That(result, Has.Count.EqualTo(1), $"Expected tasks injection to fire for: {prompt}");
        Assert.That(result[0].Injection.Id, Is.EqualTo("builtin:tasks-guidance"));
    }

    [TestCase("What is the weather today?")]
    [TestCase("Approve the last commit")]
    [TestCase("Show me the transcript")]
    public void BuiltIn_Tasks_DoesNotFireOnUnrelatedPrompts(string prompt) {
        var vars = TriggeredInjectionEvaluator.BuildVariables(@"C:\Source\MyProject");
        var result = TriggeredInjectionEvaluator.Evaluate(
            prompt,
            BuiltInPromptInjections.All,
            vars);

        Assert.That(result, Is.Empty, $"Expected tasks injection NOT to fire for: {prompt}");
    }

    [Test]
    public void BuiltIn_Tasks_InjectsCorrectFilePath() {
        var vars = TriggeredInjectionEvaluator.BuildVariables(@"C:\Source\CodeRush");
        var result = TriggeredInjectionEvaluator.Evaluate(
            "create a new task list",
            BuiltInPromptInjections.All,
            vars);

        Assert.That(result[0].ResolvedText, Does.Contain(@"C:\Source\CodeRush\.squad\tasks.md"));
    }
}
