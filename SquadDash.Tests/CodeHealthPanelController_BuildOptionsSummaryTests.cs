using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Unit tests for <see cref="CodeHealthPanelController.BuildOptionsSummary"/>.
/// These tests exercise the pure static method directly — no WPF controls are required.
/// </summary>
[TestFixture]
internal sealed class CodeHealthPanelController_BuildOptionsSummaryTests {

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CodeHealthOption Checkbox(string key, bool isChecked, string? label = null) =>
        new(key, isChecked ? "true" : "false", "checkbox", label, null, null);

    private static CodeHealthOption Radio(string key, string selectedValue, string[]? choiceValues = null) {
        var choices = new List<CodeHealthOptionChoice>();
        foreach (var v in choiceValues ?? [selectedValue])
            choices.Add(new CodeHealthOptionChoice { Value = v });
        return new(key, selectedValue, "radio", null, null, choices);
    }

    private static CodeHealthOption FreeText(string key, string value) =>
        new(key, value, "string", null, null, null);

    // ── Null / empty ──────────────────────────────────────────────────────────

    [Test]
    public void NullInput_ReturnsEmptyString() {
        var result = CodeHealthPanelController.BuildOptionsSummary(null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void EmptyList_ReturnsEmptyString() {
        var result = CodeHealthPanelController.BuildOptionsSummary(new List<CodeHealthOption>());
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    // ── Checkbox ──────────────────────────────────────────────────────────────

    [Test]
    public void CheckboxTrue_IncludedInOutput() {
        var options = new List<CodeHealthOption> { Checkbox("verbose", isChecked: true, label: "Verbose") };
        var result  = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo("Verbose"));
    }

    [Test]
    public void CheckboxTrue_NoLabel_UsesKey() {
        var options = new List<CodeHealthOption> { Checkbox("verbose", isChecked: true) };
        var result  = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo("verbose"));
    }

    [Test]
    public void CheckboxFalse_ExcludedFromOutput() {
        var options = new List<CodeHealthOption> { Checkbox("verbose", isChecked: false, label: "Verbose") };
        var result  = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void CheckboxTrue_ValueOne_IncludedInOutput() {
        var opt    = new CodeHealthOption("flag", "1", "checkbox", "Flag", null, null);
        var result = CodeHealthPanelController.BuildOptionsSummary([opt]);
        Assert.That(result, Is.EqualTo("Flag"));
    }

    // ── Radio (choices) ───────────────────────────────────────────────────────

    [Test]
    public void RadioWithSelectedValue_ShowsSelectedValue() {
        var options = new List<CodeHealthOption> {
            Radio("scope", "staged", ["all", "staged", "unstaged"])
        };
        var result = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo("staged"));
    }

    [Test]
    public void RadioWithEmptyValue_ExcludedFromOutput() {
        var options = new List<CodeHealthOption> {
            Radio("scope", string.Empty, ["all", "staged"])
        };
        var result = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    // ── Free-text ─────────────────────────────────────────────────────────────

    [Test]
    public void FreeTextWithValue_ShowsValue() {
        var options = new List<CodeHealthOption> { FreeText("branch", "main") };
        var result  = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo("main"));
    }

    [Test]
    public void FreeTextEmpty_ExcludedFromOutput() {
        var options = new List<CodeHealthOption> { FreeText("branch", string.Empty) };
        var result  = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    // ── Mixed ─────────────────────────────────────────────────────────────────

    [Test]
    public void MixedList_AllActiveValuesJoinedWithComma() {
        var options = new List<CodeHealthOption> {
            Checkbox("verbose", isChecked: true,  label: "Verbose"),
            Checkbox("dry-run", isChecked: false, label: "Dry Run"),
            Radio("scope", "staged", ["all", "staged"]),
            FreeText("target", "src/"),
        };
        var result = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo("Verbose, staged, src/"));
    }

    [Test]
    public void MixedList_OnlyActiveOptionsContribute() {
        var options = new List<CodeHealthOption> {
            Checkbox("flag-a", isChecked: false, label: "Flag A"),
            FreeText("note", ""),
            Radio("mode", "fast", ["fast", "slow"]),
        };
        var result = CodeHealthPanelController.BuildOptionsSummary(options);
        Assert.That(result, Is.EqualTo("fast"));
    }
}

