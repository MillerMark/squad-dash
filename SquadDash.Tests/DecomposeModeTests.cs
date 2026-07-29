using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

// ════════════════════════════════════════════════════════════════════════════
// TasksJsonParser
// ════════════════════════════════════════════════════════════════════════════

[TestFixture]
internal sealed class TasksJsonParserTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Wraps a JSON payload in a TASKS_JSON: marker block.</summary>
    private static string WrapJson(string json) => $"TASKS_JSON:\n{json}";

    private static string MinimalGroupJson(
        string groupId    = "PROJ-20240101",
        string groupTitle = "My Group",
        string branch     = "fix/test",
        string summary    = "Summary",
        string? tasksJson = null)
    {
        tasksJson ??= $$"""
            [
              {
                "id": "{{groupId}}-001",
                "title": "First task",
                "description": "First task",
                "dependsOn": [],
                "priority": "high"
              }
            ]
            """;

        return $$"""
            {
              "groupId":    "{{groupId}}",
              "groupTitle": "{{groupTitle}}",
              "branch":     "{{branch}}",
              "summary":    "{{summary}}",
              "tasks":      {{tasksJson}}
            }
            """;
    }

    // ── null / empty / whitespace ────────────────────────────────────────────

    [Test]
    public void TryParse_NullInput_ReturnsFalse()
    {
        var result = TasksJsonParser.TryParse(null!, out var group);
        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    [Test]
    public void TryParse_EmptyInput_ReturnsFalse()
    {
        var result = TasksJsonParser.TryParse(string.Empty, out var group);
        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    [Test]
    public void TryParse_WhitespaceOnly_ReturnsFalse()
    {
        var result = TasksJsonParser.TryParse("   \n\t  ", out var group);
        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    // ── marker absent ────────────────────────────────────────────────────────

    [Test]
    public void TryParse_NoMarker_ReturnsFalse()
    {
        var result = TasksJsonParser.TryParse(MinimalGroupJson(), out var group);
        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    /// <summary>
    /// Guard against false positives: a JSON object that contains the word TASKS_JSON
    /// inside a string value should not be misidentified.
    /// </summary>
    [Test]
    public void TryParse_MarkerInsideJsonStringValue_IsNotFalsePositive()
    {
        var text = """{ "note": "see TASKS_JSON: for details", "other": 1 }""";
        var result = TasksJsonParser.TryParse(text, out var group);
        // The marker is present but there is no { after it at the right position,
        // OR if there is, it cannot parse into a valid group → should return false.
        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Test]
    public void TryParse_ValidBlock_ReturnsTrueAndPopulatesGroup()
    {
        var text = WrapJson(MinimalGroupJson());
        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.True);
        Assert.That(group, Is.Not.Null);
        Assert.That(group!.GroupId,    Is.EqualTo("PROJ-20240101"));
        Assert.That(group.GroupTitle,  Is.EqualTo("My Group"));
        Assert.That(group.Branch,      Is.EqualTo("fix/test"));
        Assert.That(group.Summary,     Is.EqualTo("Summary"));
        Assert.That(group.Tasks,       Has.Count.EqualTo(1));
        Assert.That(group.Tasks[0].Id, Is.EqualTo("PROJ-20240101-001"));
        Assert.That(group.Tasks[0].Title, Is.EqualTo("First task"));
    }

    [Test]
    public void TryParse_ValidBlock_WithDependsOn_ReturnsTrueAndPreservesDeps()
    {
        const string groupId = "FEAT-20991231";
        var tasksJson = $$"""
            [
              { "id": "{{groupId}}-001", "title": "A", "description": "A", "dependsOn": [], "priority": "low" },
              { "id": "{{groupId}}-002", "title": "B", "description": "B", "dependsOn": ["{{groupId}}-001"], "priority": "high" }
            ]
            """;
        var text = WrapJson(MinimalGroupJson(groupId: groupId, tasksJson: tasksJson));
        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.True);
        Assert.That(group!.Tasks[1].DependsOn, Contains.Item($"{groupId}-001"));
    }

    [Test]
    public void TryParse_FencedJsonWithTrailingTranscriptProse_ReturnsTrue()
    {
        var text = $"""
            Done. Emitting the native decompose group now:

            TASKS_JSON:
            ```json
            {MinimalGroupJson()}
            ```

            What just happened: SquadDash will parse and start the group.
            """;

        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.True);
        Assert.That(group!.GroupId, Is.EqualTo("PROJ-20240101"));
    }

    [Test]
    public void TryParse_MultipleTasksJsonBlocks_UsesLastOne()
    {
        const string groupId1 = "FIRST-20000101";
        const string groupId2 = "FINAL-20991231";

        var text =
            WrapJson(MinimalGroupJson(groupId: groupId1)) + "\n\nSome intervening prose.\n\n" +
            WrapJson(MinimalGroupJson(groupId: groupId2));

        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.True);
        Assert.That(group!.GroupId, Is.EqualTo(groupId2), "Parser must use the LAST TASKS_JSON block");
    }

    [Test]
    public void TryParse_CrlfLineEndings_ParsesSuccessfully()
    {
        var raw = WrapJson(MinimalGroupJson()).Replace("\n", "\r\n");
        var result = TasksJsonParser.TryParse(raw, out var group);
        Assert.That(result, Is.True);
        Assert.That(group, Is.Not.Null);
    }

    // ── groupId validation ───────────────────────────────────────────────────

    [Test]
    public void TryParse_GroupIdLowercase_ReturnsFalse()
    {
        var text = WrapJson(MinimalGroupJson(groupId: "proj-20240101"));
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    [Test]
    public void TryParse_GroupIdMissingDigits_ReturnsFalse()
    {
        var text = WrapJson(MinimalGroupJson(groupId: "PROJ-2024"));
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_GroupIdMissingPrefix_ReturnsFalse()
    {
        // just 8 digits, no hyphen/prefix
        var text = WrapJson(MinimalGroupJson(groupId: "20240101"));
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_GroupIdNull_ReturnsFalse()
    {
        var json = """
            {
              "groupId": null,
              "groupTitle": "T",
              "branch": "main",
              "summary": "S",
              "tasks": [{ "id": "X-00000000-001", "description": "d", "dependsOn": [], "priority": "low" }]
            }
            """;
        var result = TasksJsonParser.TryParse(WrapJson(json), out var group);
        Assert.That(result, Is.False);
    }

    // ── task count ───────────────────────────────────────────────────────────

    [Test]
    public void TryParse_ExactlyTwentyFiveTasks_ReturnsTrue()
    {
        const string groupId = "PROJ-20240101";
        var tasksList = new System.Text.StringBuilder("[");
        for (int i = 1; i <= 25; i++)
            tasksList.Append($"{{ \"id\": \"{groupId}-{i:D3}\", \"title\": \"Task {i}\", \"description\": \"Task {i}\", \"dependsOn\": [], \"priority\": \"low\" }},");
        // Remove trailing comma and close array
        tasksList.Length--;
        tasksList.Append(']');

        var text = WrapJson(MinimalGroupJson(groupId: groupId, tasksJson: tasksList.ToString()));
        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.True);
        Assert.That(group!.Tasks, Has.Count.EqualTo(25));
    }

    [Test]
    public void TryParse_TwentySixTasks_ReturnsFalse()
    {
        const string groupId = "PROJ-20240101";
        var tasksList = new System.Text.StringBuilder("[");
        for (int i = 1; i <= 26; i++)
            tasksList.Append($"{{ \"id\": \"{groupId}-{i:D3}\", \"title\": \"Task {i}\", \"description\": \"Task {i}\", \"dependsOn\": [], \"priority\": \"low\" }},");
        tasksList.Length--;
        tasksList.Append(']');

        var text = WrapJson(MinimalGroupJson(groupId: groupId, tasksJson: tasksList.ToString()));
        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    [Test]
    public void TryParse_EmptyTasksArray_ReturnsFalse()
    {
        var text = WrapJson(MinimalGroupJson(tasksJson: "[]"));
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_MissingTaskTitle_ReturnsFalse()
    {
        const string tasksJson = """
            [{ "id": "PROJ-20240101-001", "description": "Detailed work", "dependsOn": [], "priority": "high" }]
            """;

        Assert.That(TasksJsonParser.TryParse(
            WrapJson(MinimalGroupJson(tasksJson: tasksJson)), out _), Is.False);
    }

    // ── dependsOn validation ─────────────────────────────────────────────────

    [Test]
    public void TryParse_DependsOnUnknownId_ReturnsFalse()
    {
        const string groupId = "PROJ-20240101";
        var tasksJson = $$"""
            [
              {
                "id": "{{groupId}}-001",
                "title": "Task 1",
                "description": "Task 1",
                "dependsOn": ["PROJ-20240101-999"],
                "priority": "low"
              }
            ]
            """;
        var text = WrapJson(MinimalGroupJson(groupId: groupId, tasksJson: tasksJson));
        var result = TasksJsonParser.TryParse(text, out var group);

        Assert.That(result, Is.False);
        Assert.That(group, Is.Null);
    }

    [Test]
    public void TryParse_DuplicateTaskIds_ReturnsFalse()
    {
        const string groupId = "PROJ-20240101";
        var tasksJson = $$"""
            [
              { "id": "{{groupId}}-001", "title": "A", "description": "A", "dependsOn": [], "priority": "high" },
              { "id": "{{groupId}}-001", "title": "B", "description": "B", "dependsOn": [], "priority": "low" }
            ]
            """;
        Assert.That(TasksJsonParser.TryParse(
            WrapJson(MinimalGroupJson(groupId: groupId, tasksJson: tasksJson)), out _), Is.False);
    }

    [Test]
    public void TryParse_EmptyRequiredGroupMetadata_ReturnsFalse()
    {
        Assert.That(TasksJsonParser.TryParse(
            WrapJson(MinimalGroupJson(groupTitle: "", branch: "", summary: "")), out _), Is.False);
    }

    // ── task ID format ───────────────────────────────────────────────────────

    [Test]
    public void TryParse_TaskIdEmptyString_ReturnsFalse()
    {
        var json = """
            {
              "groupId": "PROJ-20240101",
              "groupTitle": "T",
              "branch": "main",
              "summary": "S",
              "tasks": [{ "id": "", "description": "d", "dependsOn": [], "priority": "low" }]
            }
            """;
        var result = TasksJsonParser.TryParse(WrapJson(json), out var group);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_TaskIdMismatchesGroupId_ReturnsFalse()
    {
        const string groupId = "PROJ-20240101";
        var tasksJson = """
            [{ "id": "OTHER-20240101-001", "description": "d", "dependsOn": [], "priority": "low" }]
            """;
        var text = WrapJson(MinimalGroupJson(groupId: groupId, tasksJson: tasksJson));
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }

    // ── malformed JSON ───────────────────────────────────────────────────────

    [Test]
    public void TryParse_TruncatedJson_ReturnsFalse()
    {
        var text = "TASKS_JSON:\n{ \"groupId\": \"PROJ-20240101\", \"tasks\": [";
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var text = "TASKS_JSON:\n{ groupId: PROJ-20240101 }";
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_MarkerWithNoBrace_ReturnsFalse()
    {
        var result = TasksJsonParser.TryParse("TASKS_JSON:\nno braces here", out var group);
        Assert.That(result, Is.False);
    }

    /// <summary>Braces appearing before the TASKS_JSON marker must not be used.</summary>
    [Test]
    public void TryParse_BraceBeforeMarker_ReturnsFalse()
    {
        var text = "some { brace } here\nTASKS_JSON:\nno braces after the marker";
        var result = TasksJsonParser.TryParse(text, out var group);
        Assert.That(result, Is.False);
    }
}

[TestFixture]
internal sealed class DecomposeDecisionParserTests
{
    [TestCase("add-to-backlog")]
    [TestCase("execute-new-branch")]
    [TestCase("execute-active-branch")]
    public void TryParse_ValidDecision_ReturnsDecision(string action)
    {
        var text = $"DECOMPOSE_DECISION_JSON:\n```json\n{{\"groupId\":\"PLAN-20260725\",\"revision\":\"abc123\",\"action\":\"{action}\",\"branch\":\"feature/plan\"}}\n```";
        Assert.That(DecomposeDecisionParser.TryParse(text, out var decision), Is.True);
        Assert.That(decision!.Action, Is.EqualTo(action));
        Assert.That(decision.Revision, Is.EqualTo("abc123"));
    }

    [Test]
    public void TryParse_UnknownAction_IsRejected()
    {
        Assert.That(DecomposeDecisionParser.TryParse(
            "DECOMPOSE_DECISION_JSON:\n{\"groupId\":\"PLAN-20260725\",\"revision\":\"abc123\",\"action\":\"run-anything\"}", out _), Is.False);
    }

    [Test]
    public void TryParse_MissingRevision_IsRejected() =>
        Assert.That(DecomposeDecisionParser.TryParse(
            "DECOMPOSE_DECISION_JSON:\n{\"groupId\":\"PLAN-20260725\",\"action\":\"add-to-backlog\"}", out _), Is.False);
}

[TestFixture]
internal sealed class PendingDecomposePlanStoreTests
{
    [Test]
    public void SaveLoadAndDelete_RoundTripsRevisionedPlan()
    {
        var squadFolder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pending-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var group = new DecomposedTaskGroup("PLAN-20260725", "Plan", "feature/plan", "Summary",
                [new DecomposedSubTask("PLAN-20260725-001", "First", [], "high")]);
            var store = new PendingDecomposePlanStore(squadFolder);
            var saved = store.Save(group);
            var loaded = store.Load(group.GroupId);

            Assert.That(loaded, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(loaded!.Revision, Is.EqualTo(saved.Revision));
                Assert.That(loaded.CreatedAt, Is.Not.Null);
                Assert.That(loaded.Group.GroupId, Is.EqualTo(group.GroupId));
                Assert.That(loaded.Group.Tasks.Select(t => t.Id), Is.EqualTo(group.Tasks.Select(t => t.Id)));
            });
            Assert.That(store.LoadAll(), Has.Count.EqualTo(1));
            store.Delete(group.GroupId);
            Assert.That(store.Load(group.GroupId), Is.Null);
        }
        finally
        {
            if (Directory.Exists(squadFolder)) Directory.Delete(squadFolder, recursive: true);
        }
    }

    [Test]
    public void ComputeRevision_DeliveryMetadataDoesNotChangeApprovalRevision()
    {
        var group = new DecomposedTaskGroup("PLAN-20260725", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260725-001", "First", [], "high")]);

        Assert.That(
            PendingDecomposePlanStore.ComputeRevision(group with { Delivery = "inbox" }),
            Is.EqualTo(PendingDecomposePlanStore.ComputeRevision(group)));
    }

    [Test]
    public void ComputeRevision_TaskTitleChangesApprovalRevision()
    {
        var group = new DecomposedTaskGroup("PLAN-20260725", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260725-001", "First", [], "high", "Extract first component")]);

        var renamed = group with
        {
            Tasks = [group.Tasks[0] with { Title = "Extract a different component" }]
        };

        Assert.That(PendingDecomposePlanStore.ComputeRevision(renamed),
            Is.Not.EqualTo(PendingDecomposePlanStore.ComputeRevision(group)));
    }

    [Test]
    public void Load_AcceptsRevisionProducedBeforeDeliveryFieldWasAdded()
    {
        var squadFolder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "legacy-pending-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string groupJson = """
                {"groupId":"PLAN-20260725","groupTitle":"Plan","branch":"feature/plan","summary":"Summary","tasks":[{"id":"PLAN-20260725-001","description":"First","dependsOn":[],"priority":"high"}]}
                """;
            var legacyRevision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(groupJson)))
                .ToLowerInvariant()[..16];
            var folder = Path.Combine(squadFolder, "tmp", "decompose");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "PLAN-20260725.json"),
                $$"""
                  {"Revision":"{{legacyRevision}}","Group":{{groupJson}}}
                  """);

            var loaded = new PendingDecomposePlanStore(squadFolder).Load("PLAN-20260725");

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Revision, Is.EqualTo(legacyRevision));
            Assert.That(loaded.CreatedAt, Is.Null,
                "Plans saved by older builds remain identifiable as legacy approval contracts.");
        }
        finally
        {
            if (Directory.Exists(squadFolder)) Directory.Delete(squadFolder, recursive: true);
        }
    }
}

[TestFixture]
internal sealed class DecomposePlanInboxTests
{
    [Test]
    public void BuildMessage_CreatesHighPriorityPlanAttachmentAndHostActions()
    {
        var group = new DecomposedTaskGroup("PLAN-20260725", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260725-001", "First", [], "high")]);
        var pending = new PendingDecomposePlan("abc123", group);

        var message = DecomposePlanInbox.BuildMessage(pending, DateTimeOffset.Parse("2026-07-25T12:00:00Z"), false);

        Assert.Multiple(() =>
        {
            Assert.That(message.Priority, Is.EqualTo("high"));
            Assert.That(message.Attachments, Has.Count.EqualTo(1));
            Assert.That(message.Attachments[0].Type, Is.EqualTo(DecomposePlanInbox.AttachmentType));
            Assert.That(message.Attachments[0].PlanGroupId, Is.EqualTo(group.GroupId));
            Assert.That(message.Actions.Select(a => a.Label), Is.EqualTo(new[]
                { "Add to Backlog", "Execute in feature/plan Branch", "Execute in Active Branch" }));
            Assert.That(message.Actions.All(a => a.RouteMode == DecomposePlanInbox.ActionRouteMode), Is.True);
        });

        Assert.That(DecomposePlanInbox.TryReadSnapshot(message.Attachments[0], out var restored), Is.True);
        Assert.That(restored!.Group.GroupTitle, Is.EqualTo(group.GroupTitle));
        Assert.That(DecomposeDecisionParser.TryParse(message.Actions[1].Prompt, out var decision), Is.True);
        Assert.That(decision!.Action, Is.EqualTo("execute-new-branch"));
        Assert.That(decision.Branch, Is.EqualTo(group.Branch));
    }

    [Test]
    public void BuildMessage_ProposedBranchAlreadyActive_OmitsDuplicateBranchAction()
    {
        var group = new DecomposedTaskGroup("PLAN-20260725", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260725-001", "First", [], "high", "First task")]);
        var pending = new PendingDecomposePlan("abc123", group);

        var message = DecomposePlanInbox.BuildMessage(
            pending,
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            explicitlyRequested: false,
            activeBranch: "feature/plan");

        Assert.That(message.Actions.Select(action => action.Label), Is.EqualTo(new[]
            { "Add to Backlog", "Execute in Active Branch" }));
    }

    [Test]
    public void TasksJsonDeliveryInbox_IsParsedAndRecognized()
    {
        const string response = """
            TASKS_JSON:
            {
              "groupId": "PLAN-20260725",
              "groupTitle": "Plan",
              "branch": "feature/plan",
              "summary": "Summary",
              "delivery": "inbox",
              "tasks": [{ "id": "PLAN-20260725-001", "title": "First", "description": "First", "dependsOn": [], "priority": "high" }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(response, out var group), Is.True);
        Assert.That(DecomposePlanInbox.RequestsInboxDelivery(group!), Is.True);
    }

    [Test]
    public void InboxStoreExists_FindsArchivedPlanReminder()
    {
        var squadFolder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "plan-inbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new InboxStore(squadFolder);
            store.Save(new InboxMessage
            {
                Id = "plan-reminder",
                Subject = "Plan",
                Timestamp = DateTimeOffset.UtcNow,
            });
            store.Archive("plan-reminder");

            Assert.Multiple(() =>
            {
                Assert.That(store.GetById("plan-reminder"), Is.Null);
                Assert.That(store.Exists("plan-reminder"), Is.True);
                Assert.That(store.Exists("plan-reminder", includeArchive: false), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(squadFolder)) Directory.Delete(squadFolder, recursive: true);
        }
    }

    [Test]
    public void ResponseAddressesPlan_DistinguishesApprovalRevisionAndUnrelatedResponse()
    {
        var group = new DecomposedTaskGroup("PLAN-20260725", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260725-001", "First", [], "high")]);
        var pending = new PendingDecomposePlan("abc123", group);

        Assert.Multiple(() =>
        {
            Assert.That(DecomposePlanInbox.ResponseAddressesPlan(pending,
                "DECOMPOSE_DECISION_JSON:\n{\"groupId\":\"PLAN-20260725\",\"revision\":\"abc123\",\"action\":\"add-to-backlog\"}"), Is.True);
            Assert.That(DecomposePlanInbox.ResponseAddressesPlan(pending,
                "DECOMPOSE_DECISION_JSON:\n{\"groupId\":\"PLAN-20260725\",\"revision\":\"stale\",\"action\":\"add-to-backlog\"}"), Is.False);
            Assert.That(DecomposePlanInbox.ResponseAddressesPlan(pending, """
                TASKS_JSON:
                {
                  "groupId": "PLAN-20260725",
                  "groupTitle": "Revised plan",
                  "branch": "feature/revised-plan",
                  "summary": "Revised",
                  "tasks": [{ "id": "PLAN-20260725-001", "title": "Revised first", "description": "Revised first", "dependsOn": [], "priority": "high" }]
                }
                """), Is.True);
            Assert.That(DecomposePlanInbox.ResponseAddressesPlan(pending, "I wrote some unrelated tests."), Is.False);
        });
    }
}

// ════════════════════════════════════════════════════════════════════════════
// DecomposedTasksWriter
// ════════════════════════════════════════════════════════════════════════════

[TestFixture]
internal sealed class DecomposedTasksWriterTests
{
    private string _tasksFile = null!;

    [SetUp]
    public void SetUp()
    {
        _tasksFile = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"tasks_{Guid.NewGuid():N}.md");
    }

    [TearDown]
    public void TearDown()
    {
        try { File.Delete(_tasksFile); } catch { /* best-effort */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeGroup(
        string groupId    = "PROJ-20240101",
        string branch     = "fix/test",
        string summary    = "Test summary",
        IReadOnlyList<DecomposedSubTask>? tasks = null)
    {
        tasks ??= new[]
        {
            new DecomposedSubTask($"{groupId}-001", "First task",  null!, "high"),
            new DecomposedSubTask($"{groupId}-002", "Second task", new[] { $"{groupId}-001" }, "low"),
        };
        return new DecomposedTaskGroup(groupId, "My Group Title", branch, summary, tasks);
    }

    // ── WriteGroup ───────────────────────────────────────────────────────────

    [Test]
    public void WriteGroup_CreatesFileWithCorrectHeaderAndPendingMarkers()
    {
        var writer = new DecomposedTasksWriter();
        var group  = MakeGroup();

        writer.WriteGroup(_tasksFile, group);

        Assert.That(File.Exists(_tasksFile), Is.True);
        var content = File.ReadAllText(_tasksFile);

        Assert.That(content, Does.Contain($"<!-- decompose-group: {group.GroupId} | branch: {group.Branch} -->"));
        Assert.That(content, Does.Contain($"**[{group.GroupId}] {group.GroupTitle}**"));
        Assert.That(content, Does.Contain($"> {group.Summary}"));
        Assert.That(content, Does.Contain("- [ ] **[PROJ-20240101-001]** First task"));
        Assert.That(content, Does.Contain("- [ ] **[PROJ-20240101-002]** Second task"));
        Assert.That(content, Does.Not.Contain("[!]"));
    }

    [Test]
    public void WriteGroup_TaskLine_ContainsGroupBranchAndPriority()
    {
        var writer = new DecomposedTasksWriter();
        var group  = MakeGroup();
        writer.WriteGroup(_tasksFile, group);

        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain($"Group: {group.GroupId} | Branch: {group.Branch} | Priority: high"));
    }

    [Test]
    public void WriteGroup_TaskWithDependencies_ShowsDependsOn()
    {
        var writer = new DecomposedTasksWriter();
        var group  = MakeGroup();
        writer.WriteGroup(_tasksFile, group);

        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain("dependsOn: PROJ-20240101-001"));
    }

    [Test]
    public void WriteGroup_TaskWithNoDependencies_ShowsNone()
    {
        var writer = new DecomposedTasksWriter();
        var group  = MakeGroup();
        writer.WriteGroup(_tasksFile, group);

        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain("dependsOn: (none)"));
    }

    [Test]
    public void WriteGroup_ExplicitTitleAndDescription_RoundTripIndependently()
    {
        var task = new DecomposedSubTask(
            "PROJ-20240101-001",
            "Move watcher lifecycle and event routing out of MainWindow.",
            [],
            "high",
            "Extract WorkspaceFileWatcherCoordinator");
        var group = MakeGroup(tasks: [task]);

        new DecomposedTasksWriter().WriteGroup(_tasksFile, group);
        var content = File.ReadAllText(_tasksFile);
        var restored = TasksPanelParser.Parse(File.ReadAllLines(_tasksFile))
            .DecomposeGroups[group.GroupId].Tasks.Single();

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("**[PROJ-20240101-001]** Extract WorkspaceFileWatcherCoordinator"));
            Assert.That(content, Does.Contain("description: Move watcher lifecycle and event routing out of MainWindow."));
            Assert.That(restored.Title, Is.EqualTo(task.Title));
            Assert.That(restored.Description, Is.EqualTo(task.Description));
        });
    }

    [Test]
    public void WriteGroup_PrependsToExistingContent()
    {
        const string existing = "# existing content\n\nsome task\n";
        File.WriteAllText(_tasksFile, existing);

        var writer = new DecomposedTasksWriter();
        writer.WriteGroup(_tasksFile, MakeGroup());

        var content = File.ReadAllText(_tasksFile);
        // New group block must appear before the existing content.
        var newGroupPos  = content.IndexOf("<!-- decompose-group:", StringComparison.Ordinal);
        var existingPos  = content.IndexOf("# existing content",   StringComparison.Ordinal);
        Assert.That(newGroupPos, Is.LessThan(existingPos));
    }

    [Test]
    public void WriteGroup_SameGroupTwice_DoesNotDuplicateBlock()
    {
        var writer = new DecomposedTasksWriter();
        var group = MakeGroup();
        writer.WriteGroup(_tasksFile, group);
        writer.WriteGroup(_tasksFile, group);
        var content = File.ReadAllText(_tasksFile);
        Assert.That(CountOccurrences(content, $"<!-- decompose-group: {group.GroupId} |"), Is.EqualTo(1));
    }

    // ── WriteGroupFailed ─────────────────────────────────────────────────────

    [Test]
    public void WriteGroupFailed_WritesFailedMarkersAndFailureNote()
    {
        var writer = new DecomposedTasksWriter();
        writer.WriteGroupFailed(_tasksFile, MakeGroup());

        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain("[!]"));
        Assert.That(content, Does.Contain("(Failed — see inbox for details.)"));
        Assert.That(content, Does.Not.Contain("- [ ]"));
    }

    // ── MarkTaskFailed ───────────────────────────────────────────────────────

    [Test]
    public void MarkTaskFailed_PendingTask_ReplacesMarkerAndInsertsNote()
    {
        var writer = new DecomposedTasksWriter();
        writer.WriteGroup(_tasksFile, MakeGroup());

        writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-001");

        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain("- [!] **[PROJ-20240101-001]**"));
        Assert.That(content, Does.Contain("(Failed — see inbox for details.)"));
        // The other pending task must remain unchanged.
        Assert.That(content, Does.Contain("- [ ] **[PROJ-20240101-002]**"));
    }

    [Test]
    public void MarkTaskFailed_AlreadyFailedTask_DoesNotAddDuplicateNote()
    {
        var writer = new DecomposedTasksWriter();
        writer.WriteGroup(_tasksFile, MakeGroup());
        writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-001");

        // Call a second time — no duplicate note should appear.
        writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-001");

        var content = File.ReadAllText(_tasksFile);
        var noteCount = CountOccurrences(content, "(Failed — see inbox for details.)");
        // Each failed task should have exactly one note. Task -001 is the one we failed twice.
        // -002 was not failed, so total notes == 1.
        Assert.That(noteCount, Is.EqualTo(1));
    }

    [Test]
    public void MarkTaskFailed_TaskIdNotFound_FileUnchanged()
    {
        var writer = new DecomposedTasksWriter();
        writer.WriteGroup(_tasksFile, MakeGroup());
        var before = File.ReadAllText(_tasksFile);

        writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-999");

        var after = File.ReadAllText(_tasksFile);
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void MarkTaskFailed_AlreadyMarkedWithBang_NoChangeToMarker()
    {
        // Write as failed from the start; [!] marker is already there.
        var writer = new DecomposedTasksWriter();
        writer.WriteGroupFailed(_tasksFile, MakeGroup());

        // MarkTaskFailed searches for "- [ ] **[...]**" — won't find [!] lines.
        writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-001");

        var content = File.ReadAllText(_tasksFile);
        // No pending-marker accidentally appears.
        Assert.That(content, Does.Not.Contain("- [ ] **[PROJ-20240101-001]**"));
    }

    [Test]
    public void MarkTaskFailed_FileDoesNotExist_DoesNotThrow()
    {
        var writer = new DecomposedTasksWriter();
        Assert.DoesNotThrow(() => writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-001"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static int CountOccurrences(string source, string token)
    {
        int count = 0;
        int idx   = 0;
        while ((idx = source.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += token.Length;
        }
        return count;
    }
}

// ════════════════════════════════════════════════════════════════════════════
// CodeHealthGroupRunner (cycle detection + lifecycle)
// ════════════════════════════════════════════════════════════════════════════

[TestFixture]
internal sealed class CodeHealthGroupRunnerTests
{
    private string _tasksFile = null!;

    [SetUp]
    public void SetUp()
    {
        _tasksFile = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"tasks_{Guid.NewGuid():N}.md");
    }

    [TearDown]
    public void TearDown()
    {
        try { File.Delete(_tasksFile); } catch { /* best-effort */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeLinearGroup(string groupId = "PROJ-20240101")
    {
        var tasks = new[]
        {
            new DecomposedSubTask($"{groupId}-001", "Step 1", Array.Empty<string>(), "high"),
            new DecomposedSubTask($"{groupId}-002", "Step 2", new[] { $"{groupId}-001" }, "low"),
            new DecomposedSubTask($"{groupId}-003", "Step 3", new[] { $"{groupId}-002" }, "low"),
        };
        return new DecomposedTaskGroup(groupId, "Linear Group", "fix/test", "Three-step chain", tasks);
    }

    private static DecomposedTaskGroup MakeDirectCycleGroup(string groupId = "PROJ-20240101")
    {
        // A → B, B → A
        var tasks = new[]
        {
            new DecomposedSubTask($"{groupId}-001", "A", new[] { $"{groupId}-002" }, "low"),
            new DecomposedSubTask($"{groupId}-002", "B", new[] { $"{groupId}-001" }, "low"),
        };
        return new DecomposedTaskGroup(groupId, "Cycle Group", "fix/test", "Direct cycle", tasks);
    }

    private static DecomposedTaskGroup MakeIndirectCycleGroup(string groupId = "PROJ-20240101")
    {
        // A → B → C → A
        var tasks = new[]
        {
            new DecomposedSubTask($"{groupId}-001", "A", new[] { $"{groupId}-003" }, "low"),
            new DecomposedSubTask($"{groupId}-002", "B", new[] { $"{groupId}-001" }, "low"),
            new DecomposedSubTask($"{groupId}-003", "C", new[] { $"{groupId}-002" }, "low"),
        };
        return new DecomposedTaskGroup(groupId, "Indirect Cycle", "fix/test", "Three-way cycle", tasks);
    }

    private static DecomposedTaskGroup MakeNoDepsGroup(string groupId = "PROJ-20240101")
    {
        // All tasks have empty dependsOn — trivially acyclic.
        var tasks = new[]
        {
            new DecomposedSubTask($"{groupId}-001", "A", Array.Empty<string>(), "low"),
            new DecomposedSubTask($"{groupId}-002", "B", Array.Empty<string>(), "low"),
        };
        return new DecomposedTaskGroup(groupId, "Parallel Group", "fix/test", "No deps", tasks);
    }

    // ── Kahn's cycle detection ────────────────────────────────────────────────

    [Test]
    public void TryStartGroup_LinearDag_ReturnsTrueAndWritesPendingTasks()
    {
        var writer  = new DecomposedTasksWriter();
        var runner  = new CodeHealthGroupRunner(writer, _tasksFile);
        var group   = MakeLinearGroup();

        var result  = runner.TryStartGroup(group, out var errorJson);

        Assert.That(result, Is.True);
        Assert.That(errorJson, Is.Null);
        Assert.That(File.Exists(_tasksFile), Is.True);
        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain("- [ ]"));
        Assert.That(content, Does.Not.Contain("[!]"));
    }

    [Test]
    public void TryStartGroup_EmptyDependsOn_ReturnsTrueAndWritesPendingTasks()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group  = MakeNoDepsGroup();

        var result = runner.TryStartGroup(group, out var errorJson);

        Assert.That(result, Is.True);
        Assert.That(errorJson, Is.Null);
    }

    [Test]
    public void TryStartGroup_DirectCycle_ReturnsFalseAndWritesFailedTasks()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group  = MakeDirectCycleGroup();

        var result = runner.TryStartGroup(group, out var errorJson);

        Assert.That(result, Is.False);
        Assert.That(errorJson, Is.Not.Null);
        Assert.That(File.ReadAllText(_tasksFile), Does.Contain("[!]"));
    }

    [Test]
    public void TryStartGroup_DirectCycle_InboxJsonContainsGroupId()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group  = MakeDirectCycleGroup("CYCLE-20240101");

        runner.TryStartGroup(group, out var errorJson);

        Assert.That(errorJson, Does.Contain("CYCLE-20240101"));
        Assert.That(errorJson, Does.Contain("INBOX_MESSAGE_JSON:"));
    }

    [Test]
    public void TryStartGroup_IndirectCycle_ReturnsFalse()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group  = MakeIndirectCycleGroup();

        var result = runner.TryStartGroup(group, out _);

        Assert.That(result, Is.False);
    }

    // ── step tracking / host-owned failure ───────────────────────────────────

    [Test]
    public void MarkCurrentStepFailed_WithCurrentStep_MarksTaskFailed()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        runner.TryStartGroup(MakeLinearGroup(), out _);

        runner.SetCurrentStep("PROJ-20240101-001");
        runner.MarkCurrentStepFailed();

        var content = File.ReadAllText(_tasksFile);
        Assert.That(content, Does.Contain("- [!] **[PROJ-20240101-001]**"));
    }

    [Test]
    public void MarkCurrentStepFailed_AfterClearCurrentStep_DoesNothing()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        runner.TryStartGroup(MakeLinearGroup(), out _);
        var before = File.ReadAllText(_tasksFile);

        runner.SetCurrentStep("PROJ-20240101-001");
        runner.ClearCurrentStep();
        runner.MarkCurrentStepFailed();

        var after = File.ReadAllText(_tasksFile);
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void MarkCurrentStepFailed_WithNoCurrentStep_DoesNotThrow()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);

        Assert.DoesNotThrow(() => runner.MarkCurrentStepFailed());
    }

    [Test]
    public void TrackFirstEligibleStep_AfterDependencyCompletes_TracksNextTask()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group = MakeLinearGroup();
        runner.TryStartGroup(group, out _);
        var content = File.ReadAllText(_tasksFile).Replace(
            "- [ ] **[PROJ-20240101-001]**",
            "- [x] **[PROJ-20240101-001]**",
            StringComparison.Ordinal);
        File.WriteAllText(_tasksFile, content);

        runner.TrackFirstEligibleStep(group.GroupId);
        runner.MarkCurrentStepFailed();

        Assert.That(
            File.ReadAllText(_tasksFile),
            Does.Contain("- [!] **[PROJ-20240101-002]**"));
    }

    [Test]
    public void TrackFirstEligibleStep_MissingGroup_ReturnsMissing()
    {
        var runner = new CodeHealthGroupRunner(new DecomposedTasksWriter(), _tasksFile);

        var state = runner.TrackFirstEligibleStep("MISSING-20240101");

        Assert.That(state, Is.EqualTo(DecomposeGroupExecutionState.Missing));
    }

    [Test]
    public void TrackFirstEligibleStep_FailedPrerequisite_ReturnsBlocked()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group = MakeLinearGroup();
        runner.TryStartGroup(group, out _);
        writer.MarkTaskFailed(_tasksFile, "PROJ-20240101-001");

        var state = runner.TrackFirstEligibleStep(group.GroupId);

        Assert.That(state, Is.EqualTo(DecomposeGroupExecutionState.Blocked));
    }

    [Test]
    public void TrackFirstEligibleStep_AllTasksComplete_ReturnsComplete()
    {
        var writer = new DecomposedTasksWriter();
        var runner = new CodeHealthGroupRunner(writer, _tasksFile);
        var group = MakeLinearGroup();
        runner.TryStartGroup(group, out _);
        var content = File.ReadAllText(_tasksFile).Replace("- [ ]", "- [x]", StringComparison.Ordinal);
        File.WriteAllText(_tasksFile, content);

        var state = runner.TrackFirstEligibleStep(group.GroupId);

        Assert.That(state, Is.EqualTo(DecomposeGroupExecutionState.Complete));
    }
}

// ════════════════════════════════════════════════════════════════════════════
// LoopMdParser.BuildFilterInstruction — decompose branch
// ════════════════════════════════════════════════════════════════════════════

[TestFixture]
internal sealed class BuildFilterInstructionDecomposeTests
{
    // ── no-filter cases ───────────────────────────────────────────────────────

    [Test]
    public void BuildFilterInstruction_NullInput_ReturnsNoFilterInstruction()
    {
        var result = LoopMdParser.BuildFilterInstruction(null);
        Assert.That(result, Does.Contain("No filter"));
    }

    [Test]
    public void BuildFilterInstruction_EmptyString_ReturnsNoFilterInstruction()
    {
        var result = LoopMdParser.BuildFilterInstruction(string.Empty);
        Assert.That(result, Does.Contain("No filter"));
    }

    [Test]
    public void BuildFilterInstruction_WhitespaceOnly_ReturnsNoFilterInstruction()
    {
        var result = LoopMdParser.BuildFilterInstruction("   ");
        Assert.That(result, Does.Contain("No filter"));
    }

    // ── decompose group ID branch ─────────────────────────────────────────────

    [Test]
    public void BuildFilterInstruction_ValidGroupId_ReturnsDecomposeInstruction()
    {
        var result = LoopMdParser.BuildFilterInstruction("FEAT-20240101");

        Assert.That(result, Does.Contain("FEAT-20240101"));
        Assert.That(result, Does.Contain("decompose group"));
        Assert.That(result, Does.Contain("tasks.md"));
        Assert.That(result, Does.Contain("[ ]"));
        Assert.That(result, Does.Contain("dependsOn"));
    }

    [Test]
    public void BuildFilterInstruction_ValidGroupIdWithMultipleLetters_ReturnsDecomposeInstruction()
    {
        var result = LoopMdParser.BuildFilterInstruction("MYPROJECT-20991231");
        Assert.That(result, Does.Contain("MYPROJECT-20991231"));
        Assert.That(result, Does.Contain("decompose group"));
    }

    [Test]
    public void BuildFilterInstruction_ValidGroupId_DoesNotReturnNormalFilterText()
    {
        var result = LoopMdParser.BuildFilterInstruction("FEAT-20240101");
        // Normal filter instructions contain "Only process tasks"
        Assert.That(result, Does.Not.Contain("Only process tasks"));
    }

    // ── cases that must NOT trigger the decompose branch ─────────────────────

    [Test]
    public void BuildFilterInstruction_LowercaseGroupId_DoesNotTriggerDecomposeBranch()
    {
        var result = LoopMdParser.BuildFilterInstruction("feat-20240101");
        // All-lowercase does not match ^[A-Z]+-\d{8}$ so falls through.
        Assert.That(result, Does.Not.Contain("decompose group"));
    }

    [Test]
    public void BuildFilterInstruction_PlainKeyword_ReturnsKeywordFilter()
    {
        var result = LoopMdParser.BuildFilterInstruction("authentication");
        Assert.That(result, Does.Contain("authentication"));
        Assert.That(result, Does.Not.Contain("decompose group"));
    }

    [Test]
    public void BuildFilterInstruction_AgentMention_ReturnsAgentFilter()
    {
        var result = LoopMdParser.BuildFilterInstruction("@argus-weld");
        Assert.That(result, Does.Contain("argus-weld"));
        Assert.That(result, Does.Not.Contain("decompose group"));
    }

    [Test]
    public void BuildFilterInstruction_PartialGroupIdPattern_DoesNotTriggerDecomposeBranch()
    {
        // Has uppercase prefix and hyphen but only 4 digits — must not match.
        var result = LoopMdParser.BuildFilterInstruction("PROJ-2024");
        Assert.That(result, Does.Not.Contain("decompose group"));
    }

    [Test]
    public void BuildFilterInstruction_GroupIdWithTrailingText_DoesNotTriggerDecomposeBranch()
    {
        // The pattern requires the full string to match (anchored ^...$).
        var result = LoopMdParser.BuildFilterInstruction("FEAT-20240101 extra");
        Assert.That(result, Does.Not.Contain("decompose group"));
    }
}

