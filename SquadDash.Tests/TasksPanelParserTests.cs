namespace SquadDash.Tests;

[TestFixture]
internal sealed class TasksPanelParserTests {

    [Test]
    public void Parse_ReturnsEmptyList_ForEmptyInput() {
        var groups = TasksPanelParser.Parse([]);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public void Parse_GroupsExistWithNoItems_WhenAllItemsAreChecked() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [x] Already done",
            "## 🟡 Mid Priority",
            "- [x] This too",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].Items, Is.Empty);
            Assert.That(groups[1].Items, Is.Empty);
        });
    }

    [Test]
    public void Parse_ReturnsSingleGroup_WithSingleItem() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Fix the crash",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Emoji, Is.EqualTo("🔴"));
            Assert.That(groups[0].Label, Is.EqualTo("High Priority"));
            Assert.That(groups[0].Items, Is.EqualTo(new[] { "Fix the crash" }));
        });
    }

    [Test]
    public void Parse_ReturnsThreeGroups_WithCorrectEmojisLabelsAndItems() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item A",
            "- [ ] Item B",
            "## 🟡 Mid Priority",
            "- [ ] Item C",
            "## 🟢 Low Priority",
            "- [ ] Item D",
            "- [ ] Item E",
            "- [ ] Item F",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(groups, Has.Count.EqualTo(3));

            Assert.That(groups[0].Emoji, Is.EqualTo("🔴"));
            Assert.That(groups[0].Label, Is.EqualTo("High Priority"));
            Assert.That(groups[0].Items, Has.Count.EqualTo(2));

            Assert.That(groups[1].Emoji, Is.EqualTo("🟡"));
            Assert.That(groups[1].Label, Is.EqualTo("Mid Priority"));
            Assert.That(groups[1].Items, Has.Count.EqualTo(1));

            Assert.That(groups[2].Emoji, Is.EqualTo("🟢"));
            Assert.That(groups[2].Label, Is.EqualTo("Low Priority"));
            Assert.That(groups[2].Items, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Parse_StripsBoldWrapper_FromItemText() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] **Loop panel stop button** *(Owner: Lyra)*",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.That(groups[0].Items[0], Is.EqualTo("Loop panel stop button"));
    }

    [Test]
    public void Parse_StripsOwnerSuffix_FromItemText() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Some task *(Owner: Talia Rune)*",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.That(groups[0].Items[0], Is.EqualTo("Some task"));
    }

    [Test]
    public void Parse_StopsAtDoneSection_ItemsAfterAreExcluded() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item A",
            "## ✅ Done",
            "- [ ] Should be ignored",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Items, Is.EqualTo(new[] { "Item A" }));
        });
    }

    [Test]
    public void Parse_OnlyIncludesUncheckedItems_IgnoresChecked() {
        string[] lines = [
            "## 🟢 Low Priority",
            "- [x] Completed task",
            "- [ ] Open task",
            "- [x] Another done one",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.That(groups[0].Items, Is.EqualTo(new[] { "Open task" }));
    }

    [Test]
    public void Parse_NonPriorityHeadingResetsGroup_ItemsNotCaptured() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Captured item",
            "## Notes",
            "- [ ] Should not be captured",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Items, Is.EqualTo(new[] { "Captured item" }));
        });
    }

    [Test]
    public void Parse_DuplicatePrioritySections_BecomeDistinctGroups() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] First mid item",
            "## 🟡 Mid Priority",
            "- [ ] Second mid item",
        ];

        var groups = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].Emoji, Is.EqualTo("🟡"));
            Assert.That(groups[0].Items, Is.EqualTo(new[] { "First mid item" }));
            Assert.That(groups[1].Emoji, Is.EqualTo("🟡"));
            Assert.That(groups[1].Items, Is.EqualTo(new[] { "Second mid item" }));
        });
    }
}
