namespace SquadDash.Tests;

[TestFixture]
internal sealed class TasksPanelParserTests {

    [Test]
    public void Parse_ReturnsEmptyOpenGroups_ForEmptyInput() {
        var result = TasksPanelParser.Parse([]);

        Assert.That(result.OpenGroups, Is.Empty);
    }

    [Test]
    public void Parse_CompletedItems_EmptyForEmptyInput() {
        var result = TasksPanelParser.Parse([]);

        Assert.That(result.CompletedItems, Is.Empty);
    }

    [Test]
    public void Parse_GroupsExistWithNoItems_WhenAllItemsAreChecked() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [x] Already done",
            "## 🟡 Mid Priority",
            "- [x] This too",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups, Has.Count.EqualTo(2));
            Assert.That(result.OpenGroups[0].Items, Is.Empty);
            Assert.That(result.OpenGroups[1].Items, Is.Empty);
            Assert.That(result.CompletedItems, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Parse_ReturnsSingleGroup_WithSingleItem() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Fix the crash",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups, Has.Count.EqualTo(1));
            Assert.That(result.OpenGroups[0].Emoji, Is.EqualTo("🔴"));
            Assert.That(result.OpenGroups[0].Label, Is.EqualTo("High Priority"));
            Assert.That(result.OpenGroups[0].Items, Has.Count.EqualTo(1));
            Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Fix the crash"));
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

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups, Has.Count.EqualTo(3));

            Assert.That(result.OpenGroups[0].Emoji, Is.EqualTo("🔴"));
            Assert.That(result.OpenGroups[0].Label, Is.EqualTo("High Priority"));
            Assert.That(result.OpenGroups[0].Items, Has.Count.EqualTo(2));

            Assert.That(result.OpenGroups[1].Emoji, Is.EqualTo("🟡"));
            Assert.That(result.OpenGroups[1].Label, Is.EqualTo("Mid Priority"));
            Assert.That(result.OpenGroups[1].Items, Has.Count.EqualTo(1));

            Assert.That(result.OpenGroups[2].Emoji, Is.EqualTo("🟢"));
            Assert.That(result.OpenGroups[2].Label, Is.EqualTo("Low Priority"));
            Assert.That(result.OpenGroups[2].Items, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Parse_StripsBoldWrapper_FromItemText() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] **Loop panel stop button** *(Owner: Lyra)*",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Loop panel stop button"));
    }

    [Test]
    public void Parse_StripsOwnerSuffix_FromItemText() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Some task *(Owner: Talia Rune)*",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Some task"));
    }

    [Test]
    public void Parse_ExtractsOwner_FromItemText() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Some task *(Owner: Talia Rune)*",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].Owner, Is.EqualTo("Talia Rune"));
    }

    [Test]
    public void Parse_ExtractsBoldAndOwner_Together() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] **Loop panel stop button** *(Owner: Lyra)*",
        ];

        var result = TasksPanelParser.Parse(lines);

        var item = result.OpenGroups[0].Items[0];
        Assert.Multiple(() => {
            Assert.That(item.Text,  Is.EqualTo("Loop panel stop button"));
            Assert.That(item.Owner, Is.EqualTo("Lyra"));
        });
    }

    [Test]
    public void Parse_IsUserOwned_True_WhenOwnerContainsYou() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Task *(Owner: You)*",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].IsUserOwned, Is.True);
    }

    [Test]
    public void Parse_IsUserOwned_False_WhenOwnerIsSomeoneElse() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Task *(Owner: Brady)*",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].IsUserOwned, Is.False);
    }

    [Test]
    public void Parse_CheckedItems_GoToCompletedItems_NotOpenGroups() {
        string[] lines = [
            "## 🟢 Low Priority",
            "- [x] Completed task",
            "- [ ] Open task",
            "- [x] Another done one",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups[0].Items,   Has.Count.EqualTo(1));
            Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Open task"));
            Assert.That(result.CompletedItems,         Has.Count.EqualTo(2));
            Assert.That(result.CompletedItems[0].Text, Is.EqualTo("Completed task"));
            Assert.That(result.CompletedItems[1].Text, Is.EqualTo("Another done one"));
        });
    }

    [Test]
    public void Parse_CompletedItems_HaveIsCheckedTrue() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [x] Done item",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.CompletedItems[0].IsChecked, Is.True);
    }

    [Test]
    public void Parse_OpenItems_HaveIsCheckedFalse() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Open item",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].IsChecked, Is.False);
    }

    [Test]
    public void Parse_CompletedItems_HaveEmojiFromTheirGroup() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [x] Done high",
            "## 🟢 Low Priority",
            "- [x] Done low",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.CompletedItems[0].Emoji, Is.EqualTo("🔴"));
            Assert.That(result.CompletedItems[1].Emoji, Is.EqualTo("🟢"));
        });
    }

    [Test]
    public void Parse_StopsAtDoneSection_ItemsAfterAreExcluded() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Item A",
            "## ✅ Done",
            "- [ ] Should be ignored",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups, Has.Count.EqualTo(1));
            Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Item A"));
        });
    }

    [Test]
    public void Parse_NonPriorityHeadingResetsGroup_ItemsNotCaptured() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Captured item",
            "## Notes",
            "- [ ] Should not be captured",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups, Has.Count.EqualTo(1));
            Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Captured item"));
        });
    }

    [Test]
    public void Parse_DuplicatePrioritySections_AreMergedIntoOneGroup() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] First mid item",
            "## 🟡 Mid Priority",
            "- [ ] Second mid item",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.OpenGroups, Has.Count.EqualTo(1));
            Assert.That(result.OpenGroups[0].Emoji, Is.EqualTo("🟡"));
            Assert.That(result.OpenGroups[0].Items, Has.Count.EqualTo(2));
            Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("First mid item"));
            Assert.That(result.OpenGroups[0].Items[1].Text, Is.EqualTo("Second mid item"));
        });
    }

    [Test]
    public void Parse_RawLine_MatchesOriginalTrimmedLine() {
        string[] lines = [
            "## 🔴 High Priority",
            "    - [ ] Indented task  ",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].RawLine, Is.EqualTo("    - [ ] Indented task"));
    }

    [Test]
    public void Parse_Description_CapturedFromIndentedLinesAfterItem() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] **Fix the crash** *(Owner: Arjun Sen)*",
            "  Crash occurs on startup when config is missing.",
            "  **Blocked by:** config migration PR.",
            "---",
            "- [ ] Other task",
        ];

        var result = TasksPanelParser.Parse(lines);
        var desc = result.OpenGroups[0].Items[0].Description;

        Assert.That(desc, Does.Contain("Crash occurs on startup"));
        Assert.That(desc, Does.Contain("Blocked by"));
    }

    [Test]
    public void Parse_Description_IsNull_WhenNoDescriptionLines() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Task with no description",
            "---",
            "- [ ] Next task",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].Description, Is.Null);
    }

    [Test]
    public void Parse_Description_DoesNotLeakIntoNextItem() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] **Task A** *(Owner: you)*",
            "  Description of A.",
            "---",
            "- [ ] **Task B** *(Owner: you)*",
            "  Description of B.",
        ];

        var result = TasksPanelParser.Parse(lines);
        var items  = result.OpenGroups[0].Items;

        Assert.Multiple(() => {
            Assert.That(items[0].Description, Does.Contain("Description of A"));
            Assert.That(items[0].Description, Does.Not.Contain("Description of B"));
            Assert.That(items[1].Description, Does.Contain("Description of B"));
        });
    }

    [Test]
    public void Parse_Description_StopsAtNextListItem_WhenNoSeparator() {
        string[] lines = [
            "## 🟡 Mid Priority",
            "- [ ] Task A",
            "  Some description.",
            "- [ ] Task B",
        ];

        var result = TasksPanelParser.Parse(lines);
        var items  = result.OpenGroups[0].Items;

        Assert.Multiple(() => {
            Assert.That(items[0].Description, Does.Contain("Some description"));
            Assert.That(items[1].Description, Is.Null);
        });
    }

    [Test]
    public void Parse_Description_StopsAtNextHeading() {
        string[] lines = [
            "## 🔴 High Priority",
            "- [ ] Task",
            "  Description text.",
            "## 🟡 Mid Priority",
            "- [ ] Other",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups[0].Items[0].Description, Does.Contain("Description text"));
        Assert.That(result.OpenGroups[0].Items[0].Description, Does.Not.Contain("Other"));
    }

    [Test]
    public void Parse_BlueCircle_TreatedAsLowPriority_SameAsGreenCircle() {
        string[] lines = [
            "## 🔵 Low Priority",
            "- [ ] Blue task",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups, Has.Count.EqualTo(1));
        Assert.That(result.OpenGroups[0].Emoji, Is.EqualTo("🟢"));
        Assert.That(result.OpenGroups[0].Items[0].Text, Is.EqualTo("Blue task"));
        Assert.That(result.OpenGroups[0].Items[0].Emoji, Is.EqualTo("🟢"));
    }

    [Test]
    public void Parse_BlueCircleAndGreenCircle_MergeIntoSingleLowPriorityGroup() {
        string[] lines = [
            "## 🟢 Low Priority",
            "- [ ] Green task",
            "## 🔵 Low Priority",
            "- [ ] Blue task",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.That(result.OpenGroups, Has.Count.EqualTo(1));
        Assert.That(result.OpenGroups[0].Emoji, Is.EqualTo("🟢"));
        Assert.That(result.OpenGroups[0].Items, Has.Count.EqualTo(2));
    }

    [Test]
    public void Parse_DecomposeBlock_CreatesFirstClassPlanGroupWithMetadata() {
        string[] lines = [
            "<!-- decompose-group: GODCLASS-20260725 | branch: codex/split-main-window -->",
            "**[GODCLASS-20260725] Split MainWindow responsibilities**",
            "> Extract cohesive services without breaking behavior.",
            "",
            "- [x] **[GODCLASS-20260725-001]** Add characterization tests",
            "  Group: GODCLASS-20260725 | Branch: codex/split-main-window | Priority: high",
            "  dependsOn: (none)",
            "",
            "- [ ] **[GODCLASS-20260725-002]** Extract transcript coordination",
            "  Group: GODCLASS-20260725 | Branch: codex/split-main-window | Priority: medium",
            "  dependsOn: GODCLASS-20260725-001",
            "",
            "## 🔴 High Priority",
            "- [ ] Ordinary task",
        ];

        var result = TasksPanelParser.Parse(lines);
        var planGroup = result.OpenGroups.Single(group => group.DecomposeGroupId is not null);
        var item = planGroup.Items.Single();

        Assert.Multiple(() => {
            Assert.That(planGroup.Label, Is.EqualTo("Plan · Split MainWindow responsibilities"));
            Assert.That(planGroup.DecomposeBranch, Is.EqualTo("codex/split-main-window"));
            Assert.That(item.Text, Is.EqualTo("Extract transcript coordination"));
            Assert.That(item.TaskId, Is.EqualTo("GODCLASS-20260725-002"));
            Assert.That(item.DependsOn, Is.EqualTo(new[] { "GODCLASS-20260725-001" }));
            Assert.That(item.Description, Does.Contain("Split MainWindow responsibilities"));
            Assert.That(result.CompletedItems.Single().TaskId, Is.EqualTo("GODCLASS-20260725-001"));
            Assert.That(result.DecomposeGroups["GODCLASS-20260725"].Tasks, Has.Count.EqualTo(2));
            Assert.That(result.OpenGroups.Any(group => group.Items.Any(task => task.Text == "Ordinary task")), Is.True);
        });
    }

    [Test]
    public void Parse_MultipleDecomposeBlocks_RemainSeparatePlanGroups() {
        string[] lines = [
            "<!-- decompose-group: FIRST-20260725 | branch: codex/first -->",
            "**[FIRST-20260725] First plan**",
            "> First summary.",
            "- [ ] **[FIRST-20260725-001]** First task",
            "  Group: FIRST-20260725 | Branch: codex/first | Priority: high",
            "  dependsOn: (none)",
            "<!-- decompose-group: SECOND-20260725 | branch: codex/second -->",
            "**[SECOND-20260725] Second plan**",
            "> Second summary.",
            "- [ ] **[SECOND-20260725-001]** Second task",
            "  Group: SECOND-20260725 | Branch: codex/second | Priority: high",
            "  dependsOn: (none)",
        ];

        var result = TasksPanelParser.Parse(lines);

        Assert.Multiple(() => {
            Assert.That(result.DecomposeGroups.Keys, Is.EquivalentTo(new[] { "FIRST-20260725", "SECOND-20260725" }));
            Assert.That(result.OpenGroups.Count(group => group.DecomposeGroupId is not null), Is.EqualTo(2));
        });
    }
}
