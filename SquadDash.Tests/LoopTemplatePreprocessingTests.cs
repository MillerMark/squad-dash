using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for loop-template preprocessing:
///   Task 1 — plain <c>{{key}}</c> substitution via <see cref="LoopMdParser.BuildMergedBody"/>
///   Task 2 — <c>{{#if}}</c>/<c>{{#unless}}</c> conditional block preprocessing via
///             <see cref="LoopMdParser.PreprocessConditionals"/>
/// </summary>
[TestFixture]
internal sealed class LoopTemplatePreprocessingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LoopMdConfig MakeConfig(string instructions, params LoopOption[] options)
        => new(IntervalMinutes: 1, TimeoutMinutes: 5, Description: "", Instructions: instructions,
               Options: options.Length > 0 ? options : null);

    private static LoopOption Opt(string key, string value, string type = "string")
        => new(key, value, type, Label: null, Hint: null, Choices: null);

    private static LoopOption GroupOpt(string key)
        => new(key, RawValue: "", Type: "group", Label: null, Hint: null, Choices: null);

    private static string FindRepoFile(params string[] pathSegments)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            var candidate = dir.FullName;
            foreach (var segment in pathSegments)
                candidate = Path.Combine(candidate, segment);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        Assert.Fail($"Could not find repo file: {string.Join(Path.DirectorySeparatorChar, pathSegments)}");
        return string.Empty;
    }

    // ── Task 1: plain {{key}} substitution ───────────────────────────────────

    [Test]
    public void BuildMergedBody_KnownKey_IsSubstituted()
    {
        var config = MakeConfig("Value is {{foo}}", Opt("foo", "bar"));
        Assert.That(LoopMdParser.BuildMergedBody(config), Is.EqualTo("Value is bar"));
    }

    [Test]
    public void BuildMergedBody_UnknownKey_LeftAsIs()
    {
        var config = MakeConfig("Value is {{unknown}}", Opt("foo", "bar"));
        Assert.That(LoopMdParser.BuildMergedBody(config), Is.EqualTo("Value is {{unknown}}"));
    }

    [Test]
    public void BuildMergedBody_GroupTypeOption_TokenLeftAsIs()
    {
        // Group-type options are UI-only headers — their (empty) RawValue must NOT
        // be substituted; the {{key}} token must survive unchanged.
        var config = MakeConfig("Header: {{section_header}}", GroupOpt("section_header"));
        Assert.That(LoopMdParser.BuildMergedBody(config),
            Is.EqualTo("Header: {{section_header}}"));
    }

    [Test]
    public void BuildMergedBody_IterationToken_UnaffectedByOptionSubstitution()
    {
        // {{iteration}} is a system variable resolved by LoopController, not by
        // BuildMergedBody.  It must survive the option-substitution pass untouched.
        var config = MakeConfig("Iter: {{iteration}}", Opt("foo", "bar"));
        Assert.That(LoopMdParser.BuildMergedBody(config), Does.Contain("{{iteration}}"));
    }

    // ── Task 2: {{#if}} conditional blocks ───────────────────────────────────

    [Test]
    public void PreprocessConditionals_IfTrue_IncludesContent()
    {
        var options = new[] { Opt("mode", "fast") };
        var text = "{{#if mode == \"fast\"}}\nGo fast!\n{{/if}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Go fast!"));
        Assert.That(result, Does.Not.Contain("{{#if"));
        Assert.That(result, Does.Not.Contain("{{/if}}"));
    }

    [Test]
    public void PreprocessConditionals_IfFalse_RemovesBlockAndDelimiters()
    {
        var options = new[] { Opt("mode", "slow") };
        var text = "before\n{{#if mode == \"fast\"}}\nGo fast!\n{{/if}}\nafter";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("Go fast!"));
        Assert.That(result, Does.Not.Contain("{{#if"));
        Assert.That(result, Does.Not.Contain("{{/if}}"));
        Assert.That(result, Does.Contain("before"));
        Assert.That(result, Does.Contain("after"));
    }

    // ── Task 2: {{#unless}} conditional blocks ───────────────────────────────

    [Test]
    public void PreprocessConditionals_UnlessTrue_RemovesBlock()
    {
        // Condition: skip == "true" is TRUE → #unless block is excluded
        var options = new[] { Opt("skip", "true") };
        var text = "{{#unless skip == \"true\"}}\nDo work!\n{{/unless}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("Do work!"));
        Assert.That(result, Does.Not.Contain("{{#unless"));
        Assert.That(result, Does.Not.Contain("{{/unless}}"));
    }

    [Test]
    public void PreprocessConditionals_UnlessFalse_IncludesContent()
    {
        // Condition: skip == "true" is FALSE → #unless block is included
        var options = new[] { Opt("skip", "false") };
        var text = "{{#unless skip == \"true\"}}\nDo work!\n{{/unless}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Do work!"));
        Assert.That(result, Does.Not.Contain("{{#unless"));
        Assert.That(result, Does.Not.Contain("{{/unless}}"));
    }

    // ── Mixed: conditionals + substitution ───────────────────────────────────

    [Test]
    public void BuildMergedBody_IfBlockContainingToken_TokenSubstituted()
    {
        // Conditionals run first, then substitution.
        // Tokens inside an included block must be substituted in the second pass.
        var config = MakeConfig(
            "{{#if mode == \"fast\"}}\nSpeed: {{speed}}\n{{/if}}",
            Opt("mode", "fast"),
            Opt("speed", "100mph"));

        var result = LoopMdParser.BuildMergedBody(config);

        Assert.That(result, Does.Contain("Speed: 100mph"));
        Assert.That(result, Does.Not.Contain("{{speed}}"));
        Assert.That(result, Does.Not.Contain("{{#if"));
    }

    [Test]
    public void BuildMergedBody_IfFalseBlockContainingToken_TokenNotPresent()
    {
        // Tokens inside an excluded block must never appear in output.
        var config = MakeConfig(
            "{{#if mode == \"fast\"}}\nSpeed: {{speed}}\n{{/if}}\ndone",
            Opt("mode", "slow"),
            Opt("speed", "100mph"));

        var result = LoopMdParser.BuildMergedBody(config);

        Assert.That(result, Does.Not.Contain("Speed:"));
        Assert.That(result, Does.Not.Contain("{{speed}}"));
        Assert.That(result, Does.Contain("done"));
    }

    // ── [**FILTER**] in BuildMergedBody ──────────────────────────────────────

    [Test]
    public void BuildMergedBody_FilterPlaceholder_ReplacedFromExtraSubs()
    {
        var config = MakeConfig("Task filter: [**FILTER**]");
        var extra  = new Dictionary<string, string> { ["[**FILTER**]"] = "Only bugs." };
        Assert.That(LoopMdParser.BuildMergedBody(config, extra),
            Is.EqualTo("Task filter: Only bugs."));
    }

    [Test]
    public void BuildMergedBody_FilterPlaceholder_NoExtraSubs_LeftAsIs()
    {
        var config = MakeConfig("Task filter: [**FILTER**]");
        Assert.That(LoopMdParser.BuildMergedBody(config),
            Is.EqualTo("Task filter: [**FILTER**]"));
    }

    [Test]
    public void BuildMergedBody_FilterPlaceholder_ExtraSubsWithoutFilterKey_LeftAsIs()
    {
        var config = MakeConfig("Task filter: [**FILTER**]");
        var extra  = new Dictionary<string, string> { ["iteration"] = "1" };
        Assert.That(LoopMdParser.BuildMergedBody(config, extra),
            Is.EqualTo("Task filter: [**FILTER**]"));
    }

    [Test]
    public void FilteredTasksTemplate_StopLoopOnlyForNoActionableTasks()
    {
        var template = File.ReadAllText(
            FindRepoFile("SquadDash", "Assets", "loop-filtered-tasks.md"));

        Assert.That(template, Does.Contain("Do **not** stop the loop after a successful task"));
        Assert.That(template, Does.Contain("Only emit `stop_loop` in this no-actionable-task case"));
        Assert.That(template, Does.Contain("Do **not** append `HOST_COMMAND_JSON` after completing a task"));
        Assert.That(template, Does.Not.Contain("then stop. The next iteration"));
    }

    // ── BuildFilterInstruction ────────────────────────────────────────────────

    [Test]
    public void BuildFilterInstruction_NullOrEmpty_ReturnsNoFilterMessage()
    {
        Assert.That(LoopMdParser.BuildFilterInstruction(null),
            Is.EqualTo("No filter — process any unchecked task not owned by User."));
        Assert.That(LoopMdParser.BuildFilterInstruction(""),
            Is.EqualTo("No filter — process any unchecked task not owned by User."));
        Assert.That(LoopMdParser.BuildFilterInstruction("   "),
            Is.EqualTo("No filter — process any unchecked task not owned by User."));
    }

    [Test]
    public void BuildFilterInstruction_MentionOnly_ReturnsOwnerFilter()
    {
        var result = LoopMdParser.BuildFilterInstruction("@orion");
        Assert.That(result, Does.Contain("**@orion**"));
        Assert.That(result, Does.Contain("`*(Owner: orion)*`"));
        Assert.That(result, Does.Not.Contain("description or content"));
    }

    [Test]
    public void BuildFilterInstruction_KeywordOnly_ReturnsKeywordFilter()
    {
        var result = LoopMdParser.BuildFilterInstruction("authentication");
        Assert.That(result, Does.Contain("**authentication**"));
        Assert.That(result, Does.Not.Contain("**@"));
    }

    [Test]
    public void BuildFilterInstruction_MentionAndKeyword_ReturnsBothClauses()
    {
        var result = LoopMdParser.BuildFilterInstruction("@orion login bug");
        Assert.That(result, Does.Contain("**@orion**"));
        Assert.That(result, Does.Contain("**login bug**"));
    }

    [Test]
    public void BuildFilterInstruction_MultipleMentions_AllIncluded()
    {
        var result = LoopMdParser.BuildFilterInstruction("@alice @bob");
        Assert.That(result, Does.Contain("**@alice**"));
        Assert.That(result, Does.Contain("**@bob**"));
    }

    [Test]
    public void BuildFilterInstruction_DuplicateMention_DeduplicatedInOutput()
    {
        var result = LoopMdParser.BuildFilterInstruction("@alice @alice");
        // "**@alice**" should appear exactly once
        int count = 0;
        int idx   = 0;
        while ((idx = result.IndexOf("**@alice**", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx++;
        }
        Assert.That(count, Is.EqualTo(1), "duplicate mention should be de-duplicated");
    }

    // ── Bare boolean {{#if key}} form ─────────────────────────────────────────

    [Test]
    public void PreprocessConditionals_BareBoolIfTrue_IncludesContent()
    {
        var options = new[] { Opt("includePullRequests", "true") };
        var text = "{{#if includePullRequests}}\nAlso include pull requests.\n{{/if}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Also include pull requests."));
        Assert.That(result, Does.Not.Contain("{{#if"));
        Assert.That(result, Does.Not.Contain("{{/if}}"));
    }

    [Test]
    public void PreprocessConditionals_BareBoolIfFalse_RemovesBlock()
    {
        var options = new[] { Opt("includePullRequests", "false") };
        var text = "before\n{{#if includePullRequests}}\nAlso include pull requests.\n{{/if}}\nafter";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("Also include pull requests."));
        Assert.That(result, Does.Not.Contain("{{#if"));
        Assert.That(result, Does.Contain("before"));
        Assert.That(result, Does.Contain("after"));
    }

    [Test]
    public void PreprocessConditionals_BareBoolUnlessFalse_IncludesContent()
    {
        var options = new[] { Opt("includePullRequests", "false") };
        var text = "{{#unless includePullRequests}}\nPRs not included.\n{{/unless}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("PRs not included."));
        Assert.That(result, Does.Not.Contain("{{#unless"));
    }

    [Test]
    public void PreprocessConditionals_BareBoolUnlessTrue_RemovesBlock()
    {
        var options = new[] { Opt("includePullRequests", "true") };
        var text = "{{#unless includePullRequests}}\nPRs not included.\n{{/unless}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("PRs not included."));
        Assert.That(result, Does.Not.Contain("{{#unless"));
    }

    // ── Inline same-line {{#if key}}text{{/if}} form ──────────────────────────

    [Test]
    public void PreprocessConditionals_InlineIfTrue_IncludesContent()
    {
        var options = new[] { Opt("includePullRequests", "true") };
        var text = "Query issues.{{#if includePullRequests}} Also include pull requests.{{/if}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Query issues. Also include pull requests."));
        Assert.That(result, Does.Not.Contain("{{#if"));
        Assert.That(result, Does.Not.Contain("{{/if}}"));
    }

    [Test]
    public void PreprocessConditionals_InlineIfFalse_RemovesContent()
    {
        var options = new[] { Opt("includePullRequests", "false") };
        var text = "Summary: issues{{#if includePullRequests}} and pull requests{{/if}}.";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Summary: issues."));
        Assert.That(result, Does.Not.Contain("pull requests"));
        Assert.That(result, Does.Not.Contain("{{#if"));
    }

    [Test]
    public void PreprocessConditionals_InlineNegatedIfTrue_RemovesContent()
    {
        // {{#if !key}} — key is truthy, so negated form excludes the block
        var options = new[] { Opt("includePullRequests", "true") };
        var text = "{{#if !includePullRequests}}Pull requests are not included.{{/if}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("Pull requests are not included."));
        Assert.That(result, Does.Not.Contain("{{#if"));
    }

    [Test]
    public void PreprocessConditionals_InlineNegatedIfFalse_IncludesContent()
    {
        // {{#if !key}} — key is falsy, so negated form includes the block
        var options = new[] { Opt("includePullRequests", "false") };
        var text = "{{#if !includePullRequests}}Pull requests are not included.{{/if}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Pull requests are not included."));
        Assert.That(result, Does.Not.Contain("{{#if"));
    }

    [Test]
    public void PreprocessConditionals_InlineUnlessTrue_RemovesContent()
    {
        var options = new[] { Opt("includePullRequests", "true") };
        var text = "{{#unless includePullRequests}}PRs not included.{{/unless}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("PRs not included."));
    }

    [Test]
    public void PreprocessConditionals_InlineUnlessFalse_IncludesContent()
    {
        var options = new[] { Opt("includePullRequests", "false") };
        var text = "{{#unless includePullRequests}}PRs not included.{{/unless}}";
        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("PRs not included."));
    }

    [Test]
    public void PreprocessConditionals_IssuePrTrackerTemplate_InlineAndBlockForms()
    {
        // Mirrors the actual maintenance.md "Issue & PR Tracker" pattern
        var options = new[] { Opt("includePullRequests", "false") };
        var text =
            "Query the GitHub API.{{#if includePullRequests}} Also include pull requests.{{/if}}" +
            "{{#if !includePullRequests}}Pull requests are not included in this report.{{/if}}\n" +
            "\n" +
            "- item{{#if includePullRequests}}- Also search for pull requests{{/if}}\n" +
            "\n" +
            "1. **Summary**: Total count of new issues{{#if includePullRequests}} and pull requests{{/if}}\n" +
            "{{#if includePullRequests}}\n3. **New Pull Requests** (if any):\n{{/if}}\n" +
            "{{#if !includePullRequests}}\n3. **Suggested Next Steps**: Brief recommendations\n{{/if}}";

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Pull requests are not included in this report."));
        Assert.That(result, Does.Contain("3. **Suggested Next Steps**"));
        Assert.That(result, Does.Not.Contain("Also include pull requests."));
        Assert.That(result, Does.Not.Contain("and pull requests"));
        Assert.That(result, Does.Not.Contain("3. **New Pull Requests**"));
        Assert.That(result, Does.Not.Contain("{{#if"));
        Assert.That(result, Does.Not.Contain("{{/if}}"));
    }
}

