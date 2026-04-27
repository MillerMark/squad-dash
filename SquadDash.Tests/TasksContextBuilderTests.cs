namespace SquadDash.Tests;

[TestFixture]
internal sealed class TasksContextBuilderTests {

    // ── ParseOpenGroups ──────────────────────────────────────────────────────

    [Test]
    public void ParseOpenGroups_ReturnsEmptyList_ForEmptyInput() {
        var groups = TasksContextBuilder.ParseOpenGroups([]);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public void ParseOpenGroups_IgnoresLinesBeforeFirstHeading() {
        string[] lines = [
            "# SquadDash Task List",
            "Some preamble text.",
            "## 🔴 High Priority",
            "- [ ] Do the thing",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseOpenGroups_CreatesOneGroupPerHeading() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item A",
            "## 🟡 Mid Priority",
            "- [ ] Item B",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseOpenGroups_CollectsItemsUnderCorrectHeading() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item A",
            "- [ ] Item B",
            "## 🟡 Mid Priority",
            "- [ ] Item C",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.Multiple(() => {
            Assert.That(groups[0].Items, Is.EqualTo(new[] { "Item A", "Item B" }));
            Assert.That(groups[1].Items, Is.EqualTo(new[] { "Item C" }));
        });
    }

    [Test]
    public void ParseOpenGroups_DoesNotCollectCheckedItems() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [x] Already done",
            "- [ ] Still open",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups[0].Items, Is.EqualTo(new[] { "Still open" }));
    }

    [Test]
    public void ParseOpenGroups_StopsAtDoneSection() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item A",
            "## ✅ Done",
            "- [ ] Should be ignored",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Items, Is.EqualTo(new[] { "Item A" }));
    }

    [Test]
    public void ParseOpenGroups_StripsBoldMarkdownFromItemText() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] **squad loop support**",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups[0].Items[0], Is.EqualTo("squad loop support"));
    }

    [Test]
    public void ParseOpenGroups_StripsOwnerSuffixFromItemText() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] squad loop support *(Owner: Talia Rune)*",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups[0].Items[0], Is.EqualTo("squad loop support"));
    }

    [Test]
    public void ParseOpenGroups_HandlesIndentedCheckboxItems() {
        string[] lines = [
            "## 🔴 High Priority",
            "  - [ ] Indented item",
        ];

        var groups = TasksContextBuilder.ParseOpenGroups(lines);

        Assert.That(groups[0].Items, Is.EqualTo(new[] { "Indented item" }));
    }

    // ── Build ────────────────────────────────────────────────────────────────

    [Test]
    public void Build_ReturnsNull_ForEmptyInput() {
        var result = TasksContextBuilder.Build([]);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Build_ReturnsNull_WhenAllItemsAreChecked() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [x] Done item",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Build_ReturnsNull_WhenOnlyDoneSectionHasItems() {
        string[] lines = [
            "## ✅ Done",
            "- [ ] This should not count",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Build_IncludesHeadingAndItemInOutput() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Watch capability event parsing",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.Multiple(() => {
            Assert.That(result, Does.Contain("🔴 High Priority"));
            Assert.That(result, Does.Contain("Watch capability event parsing"));
        });
    }

    [Test]
    public void Build_CapsAtMaxInjectedItems_AndShowsFooterWithTotals() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item 1",
            "- [ ] Item 2",
            "- [ ] Item 3",
            "- [ ] Item 4",
            "- [ ] Item 5",
            "- [ ] Item 6",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.Multiple(() => {
            Assert.That(result, Does.Contain($"showing {TasksContextBuilder.MaxInjectedItems} of 6"));
            Assert.That(result, Does.Not.Contain("Item 6"));
        });
    }

    [Test]
    public void Build_FooterIsSingular_WhenExactlyOneOpenItem() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Only item",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.That(result, Does.Contain("1 open item —"));
        Assert.That(result, Does.Not.Contain("1 open items"));
    }

    [Test]
    public void Build_FooterIsPlural_ForMultipleItemsWithinCap() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Item A",
            "- [ ] Item B",
            "- [ ] Item C",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.That(result, Does.Contain("3 open items —"));
    }

    [Test]
    public void Build_OutputsItemsInFileOrder() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] First item",
            "## 🟡 Mid Priority",
            "- [ ] Second item",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.That(result!.IndexOf("First item", StringComparison.Ordinal),
            Is.LessThan(result.IndexOf("Second item", StringComparison.Ordinal)));
    }

    [Test]
    public void Build_PartialGroupAtCapBoundary_OmitsTrailingItemsFromThatGroup() {
        // 5-item cap; first group has 4, second has 2 → only 1 item from second group fits
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item 1",
            "- [ ] Item 2",
            "- [ ] Item 3",
            "- [ ] Item 4",
            "## 🟡 Mid Priority",
            "- [ ] Item 5",
            "- [ ] Item 6",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.Multiple(() => {
            Assert.That(result, Does.Contain("Item 5"));
            Assert.That(result, Does.Not.Contain("Item 6"));
        });
    }

    [Test]
    public void Build_DoesNotPrintHeading_WhenNoItemsFromThatGroupFitWithinCap() {
        // Fill cap entirely with first group so second group is not reached
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item 1",
            "- [ ] Item 2",
            "- [ ] Item 3",
            "- [ ] Item 4",
            "- [ ] Item 5",
            "## 🟡 Mid Priority",
            "- [ ] Item 6",
        ];

        var result = TasksContextBuilder.Build(lines);

        Assert.That(result, Does.Not.Contain("🟡 Mid Priority"));
    }
}
