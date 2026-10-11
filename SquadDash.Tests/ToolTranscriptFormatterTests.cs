using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ToolTranscriptFormatterTests {
    internal const string AbsolutePlanPath =
        "C:/Users/Mark/AppData/Local/SquadDash/workspaces/eqSim-e67449ce1872/sdk-config/session-state/48f9b718-2683-468c-aa58-eba6261ce750/plan.md";
    private static readonly string PlanAddedLines =
        "+Problem: ...\n" + string.Join("\n", Enumerable.Repeat("+... (added line) ...", 16));
    internal static readonly string AbsolutePlanInput =
        $"*** Begin Patch\n*** Add File: {AbsolutePlanPath}\n{PlanAddedLines}\n*** End Patch";
    internal static readonly string AbsolutePlanOutput =
        $"diff --git a/{AbsolutePlanPath} b/{AbsolutePlanPath}\n" +
        $"create file mode 100644\nindex 0000000..0000000\n--- a/dev/null\n+++ b/{AbsolutePlanPath}\n" +
        $"@@ -1,0 +1,17 @@\n{PlanAddedLines}";

    [TestCase(null)]
    [TestCase("null")]
    [TestCase("")]
    [TestCase(" \t ")]
    [TestCase(AbsolutePlanPath)]
    public void ApplyPatch_AbsoluteWindowsNewFileUsesDestinationName(string? displayText) {
        var descriptor = new ToolTranscriptDescriptor("apply_patch", DisplayText: displayText);
        var args = JsonSerializer.Serialize(AbsolutePlanInput);
        var patch = ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, AbsolutePlanOutput, args);
        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, AbsolutePlanOutput, args),
                Is.EqualTo(new ToolEditDiffSummary("plan.md", 17, 0, true, false)));
            Assert.That(patch, Is.EqualTo(AbsolutePlanOutput), "Retain the actual output and known line locations.");
            Assert.That(ToolTranscriptFormatter.TryExtractEditTargetPath(patch), Is.EqualTo(AbsolutePlanPath));
            Assert.That(ToolTranscriptFormatter.TryExtractEditTargetPath(AbsolutePlanInput), Is.EqualTo(AbsolutePlanPath));
            Assert.That(Path.GetFileName(AbsolutePlanPath), Is.EqualTo("plan.md"));
        });
    }

    [TestCase("null")]
    [TestCase("")]
    [TestCase(" \t ")]
    public void EditSummary_AbsentDisplayTextFallsBackToPathOrOutput(string displayText) {
        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(
                new ToolTranscriptDescriptor("edit", DisplayText: displayText, Path: AbsolutePlanPath),
                "File updated.")?.DisplayName, Is.EqualTo("plan.md"));
            Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(
                new ToolTranscriptDescriptor("apply_patch", DisplayText: displayText),
                AbsolutePlanInput)?.DisplayName, Is.EqualTo("plan.md"));
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
                new ToolTranscriptDescriptor("apply_patch", DisplayText: displayText),
                "File created.", JsonSerializer.Serialize(AbsolutePlanInput)), Is.Not.Null);
        });
    }

    [TestCase("NULL")]
    [TestCase("Null")]
    public void EditSummary_NullPlaceholderGuardIsCaseSensitive(string displayText) {
        Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(
            new ToolTranscriptDescriptor("edit", DisplayText: displayText), "File updated.")?.DisplayName,
            Is.EqualTo(displayText));
    }

    [TestCase("--- a/dev/null\n+++ b/plan.md", "plan.md")]
    [TestCase("--- /dev/null\n+++ b/plan.md", "plan.md")]
    [TestCase("--- a/plan.md\n+++ b/dev/null", "plan.md")]
    [TestCase("--- a/old.md\n+++ b/plan.md", "plan.md")]
    public void EditTargetPath_PrefersDestinationAndSkipsNullDevice(string headers, string expected) {
        Assert.That(ToolTranscriptFormatter.TryExtractEditTargetPath(headers), Is.EqualTo(expected));
    }

    [TestCase("--- a/dev/null\n+++ b/plan.md\n@@ -0,0 +1 @@\n+added", true, false)]
    [TestCase("--- a/plan.md\n+++ b/dev/null\n@@ -1 +0,0 @@\n-removed", false, true)]
    public void EditSummary_PrefixedNullDeviceWithoutGitMetadata(string patch, bool isNew, bool isDeleted) {
        var descriptor = new ToolTranscriptDescriptor("apply_patch");
        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, patch), Is.EqualTo(patch));
            Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, patch),
                Is.EqualTo(new ToolEditDiffSummary("plan.md", isNew ? 1 : 0, isDeleted ? 1 : 0, isNew, isDeleted)));
        });
    }

    // Runtime success messages deliberately do not contain the captured change.
    internal static IEnumerable<TestCaseData> FileEditCaptures() {
        yield return new TestCaseData("edit", CapturedEditPatch, null, 3, 2, false);
        yield return new TestCaseData("apply_patch", CapturedEditPatch, null, 3, 2, false);
        yield return new TestCaseData("create",
            "--- /dev/null\n+++ b/src/file.cs\n@@ -0,0 +1,2 @@\n+first\n+last", null, 2, 0, true);
        yield return new TestCaseData("edit", "File src/file.cs updated with changes.",
            """{"path":"src/file.cs","old_str":"old\n","new_str":"new\nextra\n"}""", 2, 1, false);
        yield return new TestCaseData("create", "Created file src/file.cs with 11 characters",
            """{"path":"src/file.cs","file_text":"first\nlast\n"}""", 2, 0, true);
        yield return new TestCaseData("functions.create", "Created file src/file.cs with 0 characters",
            """{"path":"src/file.cs","file_text":""}""", 0, 0, true);
        yield return new TestCaseData("write_file", "File written.",
            """{"file_path":"src/file.cs","content":"first\n\nlast\n"}""", 0, 0, false);
        yield return new TestCaseData("write", "File written.",
            """{"path":"src/file.cs","content":"first\nlast"}""", 0, 0, false);
        yield return new TestCaseData("str_replace_editor", "File src/file.cs updated with changes.",
            """{"command":"str_replace","path":"src/file.cs","old_str":"old","new_str":"new"}""", 1, 1, false);
        yield return new TestCaseData("str_replace_editor", "Created file src/file.cs with 6 characters",
            """{"command":"create","path":"src/file.cs","file_text":"first\n"}""", 1, 0, true);
        const string update = "*** Begin Patch\n*** Update File: src/file.cs\n@@\n-old\n+new\n*** End Patch";
        yield return new TestCaseData("apply_patch", "Modified 1 file(s): src/file.cs",
            JsonSerializer.Serialize(update), 1, 1, false);
        yield return new TestCaseData("functions.apply_patch", "Modified 1 file(s): src/file.cs",
            JsonSerializer.Serialize(new { input = update }), 1, 1, false);
        const string add = "*** Begin Patch\n*** Add File: src/file.cs\n+first\n+\n+last\n*** End Patch";
        yield return new TestCaseData("apply_patch", "Added 1 file(s): src/file.cs",
            JsonSerializer.Serialize(new { patch = add }), 3, 0, true);
    }

    [TestCaseSource(nameof(FileEditCaptures))]
    public void CapturedArguments_SupplyPatchForSuccessOnlyOutput(
        string tool, string output, string? args, int added, int removed, bool isNew) {
        var descriptor = new ToolTranscriptDescriptor(tool);
        var patch = ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, output, args);
        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.IsEditToolName(tool), Is.True);
            Assert.That(patch, Is.Not.Null);
            Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output, args),
                Is.EqualTo(new ToolEditDiffSummary("file.cs", added, removed, isNew, false)));
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, patch), Is.EqualTo(patch),
                "Synthesized captures must themselves be complete, valid unified diffs.");
        });
    }

    [TestCase("create")]
    [TestCase("functions.create")]
    [TestCase("create_file")]
    public void CreateTool_KeepsItsFileIcon(string tool) {
        Assert.That(ToolTranscriptFormatter.ResolveToolIconKey(tool), Is.EqualTo("ToolIcon_create"));
    }

    [TestCase("Get-Content x")]
    [TestCase("Get-Content x -Raw")]
    [TestCase("Get-Content x -TotalCount 10")]
    [TestCase("Get-Content x -Tail 10")]
    [TestCase("Get-Content x -Head 10")]
    [TestCase("Get-Content x -Wait")]
    [TestCase("Get-Content x -Tail 10 -Wait")]
    [TestCase("gc x")]
    [TestCase("cat x")]
    [TestCase("type x")]
    [TestCase("  gEt-CoNtEnT x  ")]
    [TestCase("GC x; CAT y; TYPE z")]
    [TestCase("[System.IO.File]::ReadAllText('x')")]
    [TestCase("[System.IO.File]::ReadAllLines('x')")]
    [TestCase("[System.IO.File]::ReadAllBytes('x')")]
    [TestCase("[System.IO.File]::readalltext('x')")]
    [TestCase("Import-Csv x")]
    [TestCase("Import-Clixml x")]
    [TestCase("IMPORT-CLIXML x")]
    [TestCase("(Get-Content \"x\" -Raw).Substring(0,7000)")]
    [TestCase("Get-Content x | ConvertFrom-Json | Select-Object name")]
    [TestCase("Write-Output 'start'; Get-Content x; Get-Date")]
    public void ClassifyPowerShellFileIconKey_ReadsUseViewIcon(string command) {
        Assert.That(ToolTranscriptFormatter.ClassifyPowerShellFileIconKey(command),
            Is.EqualTo("ToolIcon_view"));
    }

    [TestCase("Set-Content x 'text'")]
    [TestCase("Add-Content x 'text'")]
    [TestCase("'text' | Out-File x")]
    [TestCase("'text' | Tee-Object x")]
    [TestCase("Get-Process | Export-Csv x")]
    [TestCase("Get-Process | Export-Clixml x")]
    [TestCase("New-Item x -ItemType File")]
    [TestCase("Set-Item x 'text'")]
    [TestCase("Clear-Content x")]
    [TestCase("sEt-CoNtEnT x 'text'")]
    [TestCase("[System.IO.File]::WriteAllText('x', 'text')")]
    [TestCase("[System.IO.File]::WriteAllLines('x', @('text'))")]
    [TestCase("[System.IO.File]::WriteAllBytes('x', [byte[]]@(1))")]
    [TestCase("[System.IO.File]::AppendAllText('x', 'text')")]
    [TestCase("[System.IO.File]::AppendAllLines('x', @('text'))")]
    [TestCase("[System.IO.File]::appendalltext('x', 'text')")]
    [TestCase("Write-Output 'text' > x")]
    [TestCase("Write-Output 'text' >> x")]
    [TestCase("Get-Process 2> x")]
    [TestCase("Get-Process *> x")]
    [TestCase("Get-Process 2>> x")]
    [TestCase("Get-Process *>> x")]
    [TestCase("Get-Content a | Set-Content b")]
    [TestCase("Get-Content a > b")]
    [TestCase("Get-Content a -Wait >> b")]
    [TestCase("Get-Content a; Add-Content b 'text'")]
    [TestCase("Set-Content b 'text'; Get-Content a")]
    public void ClassifyPowerShellFileIconKey_WritesTakePrecedence(string command) {
        Assert.That(ToolTranscriptFormatter.ClassifyPowerShellFileIconKey(command),
            Is.EqualTo("ToolIcon_edit"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n ")]
    [TestCase("Select-String -Path x -Pattern error")]
    [TestCase("findstr error x")]
    [TestCase("Get-Process")]
    [TestCase("dotnet build")]
    [TestCase("Get-Date; Write-Output 'hello'")]
    [TestCase("if (2 -gt 1 -and 3 -ge 2) { Write-Output 'yes' }")]
    [TestCase("Get-ContentMetadata x")]
    [TestCase("CustomGet-Content x")]
    [TestCase("Get-Content-Wrapper x")]
    [TestCase("Set-ContentMetadata x")]
    [TestCase("CustomSet-Content x")]
    [TestCase("Set-Content-Wrapper x")]
    [TestCase("WriteAllTextAsync x")]
    [TestCase("ReadAllTextAsync x")]
    [TestCase("my_ReadAllLines x")]
    [TestCase("my_WriteAllLines x")]
    [TestCase("gci x")]
    [TestCase("Get-Item catalog.txt")]
    [TestCase("Get-TypeData")]
    [TestCase("sc query service")]
    [TestCase("ac x")]
    public void ClassifyPowerShellFileIconKey_UnmatchedCommandsKeepShellIcon(string? command) {
        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.ClassifyPowerShellFileIconKey(command), Is.Null);
            Assert.That(ToolTranscriptFormatter.ResolveToolIconKey("powershell"),
                Is.EqualTo("ToolIcon_powershell"));
        });
    }

    [Test]
    public void CapturedArguments_PreferActualOutputWithKnownLineLocations() {
        var descriptor = new ToolTranscriptDescriptor("edit", Path: "src/file.cs");
        const string args = """{"path":"src/file.cs","old_str":"old","new_str":"new"}""";
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, CapturedEditPatch, args),
            Is.EqualTo(CapturedEditPatch));
    }

    [Test]
    public void CapturedArguments_CompleteInputStillWorksWhenOutputWasTruncated() {
        var descriptor = new ToolTranscriptDescriptor("create");
        const string args = """{"path":"src/file.cs","file_text":"first\nlast\n"}""";
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor,
            "[SquadDash truncated 100 characters from live tool output.]", args),
            Does.EndWith("+first\n+last"));
    }

    [Test]
    public void CapturedArguments_MultiFileAndMultiHunkSelectCorrectFile() {
        const string input = """
            *** Begin Patch
            *** Add File: other/file.cs
            +unrelated
            *** Update File: src/file.cs
            @@ first anchor
            -old
            +new
             context
            @@ second anchor
            -last
            +updated
            *** End of File
            *** End Patch
            """;
        var args = JsonSerializer.Serialize(new { input });
        var descriptor = new ToolTranscriptDescriptor("apply_patch", Path: @"D:\repo\src\file.cs");
        var patch = ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, "Modified 2 file(s)", args);
        Assert.Multiple(() => {
            Assert.That(patch, Does.Contain("-old\n+new\n context"));
            Assert.That(patch, Does.Contain("-last\n+updated"));
            Assert.That(patch, Does.Not.Contain("unrelated"));
            Assert.That(patch, Does.Contain("original line location unavailable"));
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, patch), Is.EqualTo(patch));
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
                new ToolTranscriptDescriptor("apply_patch", DisplayText: "file.cs"), "Modified 2 file(s)", args), Is.Null);
        });
    }

    [TestCase("create", """{"path":"src/file.cs"}""")]
    [TestCase("create", """{"path":"src/file.cs","file_text":"cut off""")]
    [TestCase("edit", """{"path":"src/file.cs","old_str":"old"}""")]
    [TestCase("str_replace_editor", """{"command":"view","path":"src/file.cs","file_text":"not an edit"}""")]
    [TestCase("powershell", """{"path":"src/file.cs","content":"not a file tool"}""")]
    [TestCase("write_powershell", """{"path":"src/file.cs","content":"not a file tool"}""")]
    [TestCase("apply_patch", """{"input":"*** Begin Patch\n*** Add File: src/file.cs\n+cut off"}""")]
    [TestCase("apply_patch", """{"input":"*** Begin Patch\n*** Delete File: src/file.cs\n*** End Patch"}""")]
    public void CapturedArguments_MissingOrIncompleteContentDoesNotInventDiff(string tool, string args) {
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor(tool), "Succeeded", args), Is.Null);
    }

    private static readonly string CapturedEditPatch = """
        diff --git a/src/file.cs b/src/file.cs
        index 0000000..1111111 100644
        --- a/src/file.cs
        +++ b/src/file.cs
        @@ -1,2 +1,3 @@
        -old
        +new
        +extra
         context
        @@ -10 +11 @@
        ---old prefix
        \ No newline at end of file
        +++new prefix
        \ No newline at end of file
        """.ReplaceLineEndings("\n");

    [TestCase("edit")]
    [TestCase("apply_patch")]
    [TestCase("APPLY_PATCH")]
    public void TryExtractEditUnifiedDiff_StripsToolChromeAndPreservesCompleteHunks(string toolName) {
        var output = "File updated successfully.\r\n```diff\r\n" +
            CapturedEditPatch.Replace("\n", "\r\n") + "\r\n```\r\nDone.";

        var patch = ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor(toolName, DisplayText: @"src\file.cs"), output);

        Assert.That(patch, Is.EqualTo(CapturedEditPatch));
    }

    [TestCase("edit")]
    [TestCase("apply_patch")]
    [TestCase("create")]
    public void TryExtractEditUnifiedDiff_NewFileRetainsAllAddedContent(string toolName) {
        const string patch = """
            diff --git a/src/new.cs b/src/new.cs
            new file mode 100644
            --- /dev/null
            +++ b/src/new.cs
            @@ -0,0 +1,3 @@
            +first
            +
            +last
            """;
        var descriptor = new ToolTranscriptDescriptor(toolName, DisplayText: @"src\new.cs");

        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, patch), Is.EqualTo(patch.ReplaceLineEndings("\n")));
            var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, patch);
            Assert.That(summary, Is.EqualTo(new ToolEditDiffSummary("new.cs", 3, 0, true, false)));
        });
    }

    [Test]
    public void TryExtractEditUnifiedDiff_DeletedFileUsesOldPath() {
        const string patch = """
            diff --git a/src/old.cs b/src/old.cs
            deleted file mode 100644
            --- a/src/old.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -first
            -last
            """;
        var descriptor = new ToolTranscriptDescriptor("apply_patch", DisplayText: @"src\old.cs");

        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, patch), Is.EqualTo(patch.ReplaceLineEndings("\n")));
            Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, patch),
                Is.EqualTo(new ToolEditDiffSummary("old.cs", 0, 2, false, true)));
        });
    }

    [TestCase(@"src\file.cs")]
    [TestCase(@"..\src\file.cs")]
    [TestCase(@"D:\repo\src\file.cs")]
    [TestCase("file.cs")]
    public void TryExtractEditUnifiedDiff_MultiFileOutputSelectsOnlyNamedFile(string displayPath) {
        var otherPatch = CapturedEditPatch.Replace("src/file.cs", "other/second.cs").Replace("+extra\n", "")
            .Replace("+1,3", "+1,2");
        var output = otherPatch + "\n" + CapturedEditPatch;
        var descriptor = new ToolTranscriptDescriptor("apply_patch", DisplayText: displayPath);

        Assert.Multiple(() => {
            Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, output), Is.EqualTo(CapturedEditPatch));
            var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);
            Assert.That(summary!.AddedLineCount, Is.EqualTo(5), "The tool summary still totals every edited file.");
            Assert.That(summary.RemovedLineCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void TryExtractCapturedEdits_MultiFileCallReturnsPerFileCountsAndLifecycle() {
        const string output = """
            diff --git a/src/ExpressionContracts.cs b/src/ExpressionContracts.cs
            --- a/src/ExpressionContracts.cs
            +++ b/src/ExpressionContracts.cs
            @@ -1 +1,2 @@
             existing
            +strict
            diff --git a/tests/ExpressionLanguageTests.cs b/tests/ExpressionLanguageTests.cs
            --- a/tests/ExpressionLanguageTests.cs
            +++ b/tests/ExpressionLanguageTests.cs
            @@ -1,2 +1 @@
             existing
            -unused
            diff --git a/tests/AuthoringSessionTests.cs b/tests/AuthoringSessionTests.cs
            new file mode 100644
            --- /dev/null
            +++ b/tests/AuthoringSessionTests.cs
            @@ -0,0 +1,2 @@
            +first
            +second
            """;

        var edits = ToolTranscriptFormatter.TryExtractCapturedEdits(
            new ToolTranscriptDescriptor("apply_patch", DisplayText: "src/ExpressionContracts.cs"), output);

        Assert.Multiple(() => {
            Assert.That(edits.Select(edit => edit.DisplayName), Is.EqualTo(new[] {
                "ExpressionContracts.cs", "ExpressionLanguageTests.cs", "AuthoringSessionTests.cs"
            }));
            Assert.That(edits.Select(edit => (edit.AddedLineCount, edit.RemovedLineCount)),
                Is.EqualTo(new[] { (1, 0), (0, 1), (2, 0) }));
            Assert.That(edits.Select(edit => edit.IsNewFile), Is.EqualTo(new[] { false, false, true }));
        });
    }

    [Test]
    public void EditSummary_MultiFileOutputUsesSelectedFileForLifecycleDecoration() {
        const string updated = """
            diff --git a/src/Program.cs b/src/Program.cs
            --- a/src/Program.cs
            +++ b/src/Program.cs
            @@ -1 +1 @@
            -old startup
            +new startup
            """;
        const string added = """
            diff --git a/src/SnapshotLinks.cs b/src/SnapshotLinks.cs
            new file mode 100644
            --- /dev/null
            +++ b/src/SnapshotLinks.cs
            @@ -0,0 +1 @@
            +new link builder
            """;
        const string deleted = """
            diff --git a/src/Viewer.razor b/src/Viewer.razor
            deleted file mode 100644
            --- a/src/Viewer.razor
            +++ /dev/null
            @@ -1 +0,0 @@
            -old viewer
            """;
        var output = updated.ReplaceLineEndings("\n") + "\n" + added.ReplaceLineEndings("\n") + "\n" +
            deleted.ReplaceLineEndings("\n");

        var updatedSummary = ToolTranscriptFormatter.TryBuildEditDiffSummary(
            new ToolTranscriptDescriptor("apply_patch", DisplayText: @"src\Program.cs"), output);
        var addedSummary = ToolTranscriptFormatter.TryBuildEditDiffSummary(
            new ToolTranscriptDescriptor("apply_patch", DisplayText: @"src\SnapshotLinks.cs"), output);
        var deletedSummary = ToolTranscriptFormatter.TryBuildEditDiffSummary(
            new ToolTranscriptDescriptor("apply_patch", DisplayText: @"src\Viewer.razor"), output);

        Assert.Multiple(() => {
            Assert.That(updatedSummary,
                Is.EqualTo(new ToolEditDiffSummary("Program.cs", 2, 2, false, false)));
            Assert.That(addedSummary,
                Is.EqualTo(new ToolEditDiffSummary("SnapshotLinks.cs", 2, 2, true, false)));
            Assert.That(deletedSummary,
                Is.EqualTo(new ToolEditDiffSummary("Viewer.razor", 2, 2, false, true)));
        });
    }

    [Test]
    public void TryExtractEditUnifiedDiff_MultiFileOutputWithoutDescriptorUsesFirstFile() {
        var output = CapturedEditPatch + "\n" + CapturedEditPatch.Replace("src/file.cs", "other/second.cs");
        var descriptor = new ToolTranscriptDescriptor("apply_patch");

        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, output), Is.EqualTo(CapturedEditPatch));
    }

    [Test]
    public void TryExtractEditUnifiedDiff_AmbiguousBasenameDoesNotLinkWrongFile() {
        var output = CapturedEditPatch + "\n" + CapturedEditPatch.Replace("src/file.cs", "other/file.cs");

        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("apply_patch", DisplayText: "file.cs"), output), Is.Null);
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("apply_patch", DisplayText: "file.cs", Path: @"D:\repo\src\file.cs"), output),
            Is.EqualTo(CapturedEditPatch));
    }

    [Test]
    public void TryExtractEditUnifiedDiff_AcceptsUnifiedHeadersWithoutGitHeader() {
        const string patch = "--- old.cs\tbefore\n+++ old.cs\tafter\n@@ -1 +1 @@\n-before\n+after";

        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("edit", DisplayText: "old.cs"), "Updated:\n" + patch + "\n\nDone."),
            Is.EqualTo(patch));
    }

    [Test]
    public void TryExtractEditUnifiedDiff_HandlesQuotedPathsAndDeletionTimestamps() {
        const string patch = "--- \"a/src/my file.cs\"\tbefore\n+++ /dev/null\tafter\n@@ -1 +0,0 @@\n-removed";

        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("edit", DisplayText: @"src\my file.cs"), patch), Is.EqualTo(patch));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("File updated successfully.")]
    [TestCase("*** Begin Patch\n*** Update File: src/file.cs\n@@\n-old\n+new\n*** End Patch")]
    [TestCase("--- a/src/file.cs\n+++ b/src/file.cs\n@@ -1,2 +1 @@\n-old\n+new")]
    [TestCase("--- a/src/file.cs\n+++ b/src/file.cs\n@@ -1 +1 @@\n-old")]
    [TestCase("--- a/src/file.cs\n+++ b/src/file.cs\n@@ -1 +1 @@\n-old\n+new\n+unexpected")]
    [TestCase("--- a/src/file.cs\n+++ b/src/file.cs\n@@ -1,9999999999999999 +1 @@\n-old\n+new")]
    public void TryExtractEditUnifiedDiff_MissingOrIncompleteCaptureFallsBackToPlainText(string? output) {
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("edit", DisplayText: @"src\file.cs"), output), Is.Null);
    }

    [TestCase("[SquadDash truncated 123 characters from live tool output. Use a narrower view if you need the omitted text.]")]
    [TestCase("\u2026 [123 chars truncated]")]
    public void TryExtractEditUnifiedDiff_TruncationNoticeRejectsEvenCompletePrefix(string note) {
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("edit", DisplayText: @"src\file.cs"), CapturedEditPatch + "\n\n" + note),
            Is.Null);
    }

    [Test]
    public void TryExtractEditUnifiedDiff_LiveLimiterCannotProducePartialEditLink() {
        var descriptor = new ToolTranscriptDescriptor("edit", DisplayText: @"src\file.cs");
        var retained = ToolTranscriptOutputLimiter.TrimForLiveTranscript(CapturedEditPatch, 100);

        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, retained), Is.Null);
    }

    [Test]
    public void TryBuildEditDiffSummary_CreateWithoutDiffKeepsOriginalPresentation() {
        Assert.That(ToolTranscriptFormatter.TryBuildEditDiffSummary(
            new ToolTranscriptDescriptor("create", DisplayText: "file.cs"), "File created successfully."), Is.Null);
        Assert.That(ToolTranscriptFormatter.TryExtractEditUnifiedDiff(
            new ToolTranscriptDescriptor("powershell", DisplayText: "file.cs"), CapturedEditPatch), Is.Null);
    }

    [Test]
    public void BuildRunningText_PrefersDescriptionForPowerShell() {
        var descriptor = new ToolTranscriptDescriptor(
            "powershell",
            Description: "Check git status and recent commits",
            Command: "git --no-pager status && git --no-pager log --oneline -5");

        var text = ToolTranscriptFormatter.BuildRunningText(descriptor);

        Assert.That(text, Is.EqualTo("Running PowerShell: Check git status and recent commits..."));
    }

    [Test]
    public void BuildCompletedText_UsesConciseContextWithoutRanPrefix() {
        var descriptor = new ToolTranscriptDescriptor(
            "report_intent",
            Intent: "Checking repo status");

        var text = ToolTranscriptFormatter.BuildCompletedText(descriptor, success: true);

        Assert.That(text, Is.EqualTo("Checking repo status."));
    }

    [Test]
    public void BuildFailurePreview_TruncatesAfterConfiguredLineLimit() {
        var text = string.Join("\n", Enumerable.Range(1, 22).Select(index => $"line {index}"));

        var preview = ToolTranscriptFormatter.BuildFailurePreview(text, maxLines: 20);

        Assert.Multiple(() => {
            Assert.That(preview, Does.Contain("line 20"));
            Assert.That(preview, Does.Not.Contain("line 21"));
            Assert.That(preview.EndsWith("...", StringComparison.Ordinal), Is.True);
        });
    }

    [Test]
    public void BuildDetailContent_IncludesToolPayloadTimingAndOutput() {
        var detail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("powershell", Description: "Check repo"),
            "{\n  \"command\": \"git status\"\n}",
            "fatal: not a git repository",
            new DateTimeOffset(2026, 4, 6, 14, 24, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 4, 6, 14, 24, 3, TimeSpan.FromHours(-4)),
            "Running git status",
            IsCompleted: true,
            Success: false);

        var content = ToolTranscriptFormatter.BuildDetailContent(detail);

        Assert.Multiple(() => {
            Assert.That(content, Does.Contain("[TOOL] powershell"));
            Assert.That(content, Does.Contain("\"command\": \"git status\""));
            Assert.That(content, Does.Contain("Started: 2026-04-06 14:24:00 -04:00"));
            Assert.That(content, Does.Contain("Finished: 2026-04-06 14:24:03 -04:00"));
            Assert.That(content, Does.Contain("Error Output"));
            Assert.That(content, Does.Contain("fatal: not a git repository"));
        });
    }

    [Test]
    public void BuildDetailContent_ShowsRunningStatus_WhenToolHasNotCompleted() {
        var detail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("powershell", Description: "Check repo"),
            "{\n  \"command\": \"git status\"\n}",
            null,
            new DateTimeOffset(2026, 4, 6, 14, 24, 0, TimeSpan.FromHours(-4)),
            null,
            "Running git status",
            IsCompleted: false,
            Success: false);

        var content = ToolTranscriptFormatter.BuildDetailContent(detail);

        Assert.Multiple(() => {
            Assert.That(content, Does.Contain("Finished: (still running)"));
            Assert.That(content, Does.Contain("Status: Running"));
            Assert.That(content, Does.Not.Contain("Status: Failed"));
        });
    }

    [Test]
    public void BuildAgentTurnStartMarker_UsesSingleLineTaskAndTimestamp() {
        var startedAt = new DateTimeOffset(2026, 4, 9, 14, 44, 57, TimeSpan.FromHours(-4));

        var marker = ToolTranscriptFormatter.BuildAgentTurnStartMarker(
            "Fix stale session after idle.\nInvestigate transcript persistence.",
            startedAt);

        Assert.That(
            marker,
            Is.EqualTo("Starting Fix stale session after idle. Investigate transcript persistence at 2026-04-09 14:44:57 -04:00"));
    }

    [Test]
    public void StripSystemNotifications_RemovesInternalControlBlocks() {
        var text = """
            Here is the result.

            <system_notification>
            Agent "explore-agent-cards" (explore) has completed successfully.
            Use read_agent with agent_id "explore-agent-cards" to retrieve the full results.
            </system_notification>

            Final summary.
            """;

        var stripped = ToolTranscriptFormatter.StripSystemNotifications(text);

        Assert.Multiple(() => {
            Assert.That(stripped, Does.Contain("Here is the result."));
            Assert.That(stripped, Does.Contain("Final summary."));
            Assert.That(stripped, Does.Not.Contain("system_notification"));
            Assert.That(stripped, Does.Not.Contain("read_agent"));
        });
    }

    [Test]
    public void BuildCompletedText_ForGlobReportsMatchedFileCount() {
        var descriptor = new ToolTranscriptDescriptor(
            "glob",
            DisplayText: "**/OpenAiModelInfo.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: true,
            outputText: @"C:\Repo\OpenAiModelInfo.cs");

        Assert.That(text, Is.EqualTo("🔎**/OpenAiModelInfo.cs -- 1 file found"));
    }

    [Test]
    public void BuildRunningText_ForViewUsesUntruncatedRelativePath() {
        var descriptor = new ToolTranscriptDescriptor(
            "view",
            DisplayText: @"..\Foundation\Engine\DevExpress.CodeRush.Foundation\OpenAI\ModelData\OpenAiModelData.cs");

        var text = ToolTranscriptFormatter.BuildRunningText(descriptor);

        Assert.That(text, Is.EqualTo(@"👀..\Foundation\Engine\DevExpress.CodeRush.Foundation\OpenAI\ModelData\OpenAiModelData.cs"));
    }

    [Test]
    public void TryBuildEditDiffSummary_CountsAddedAndRemovedLines() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"..\Foundation\Engine\OpenAiModelData.cs");
        var output = """
            diff --git a/file b/file
            index 0000000..1111111 100644
            --- a/file
            +++ b/file
            @@ -1,2 +1,4 @@
            -old line
            +new line
            +another line
             context
            """;

        var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);

        Assert.Multiple(() => {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.DisplayName, Is.EqualTo("OpenAiModelData.cs"));
            Assert.That(summary.AddedLineCount, Is.EqualTo(2));
            Assert.That(summary.RemovedLineCount, Is.EqualTo(1));
            Assert.That(summary.IsNewFile, Is.False);
            Assert.That(summary.IsDeletedFile, Is.False);
        });
    }

    [Test]
    public void TryBuildEditDiffSummary_DetectsNewFile() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"..\Foundation\Engine\MyNewFile.cs");
        var output = """
            diff --git a/dev/null b/file
            new file mode 100644
            --- /dev/null
            +++ b/file
            @@ -0,0 +1,2 @@
            +line one
            +line two
            """;

        var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);

        Assert.Multiple(() => {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.IsNewFile, Is.True);
            Assert.That(summary.AddedLineCount, Is.EqualTo(2));
            Assert.That(summary.RemovedLineCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryBuildEditDiffSummary_ForApplyPatch_ExtractsFileNameFromDiffOutput() {
        var descriptor = new ToolTranscriptDescriptor("apply_patch");
        var output = """
            diff --git a/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs b/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs
            index 0000000..1111111 100644
            --- a/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs
            +++ b/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs
            @@ -1,2 +1,3 @@
            -old line
            +new line
            +another line
            """;

        var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);

        Assert.Multiple(() => {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.DisplayName, Is.EqualTo("PlanViewerWindow.cs"));
            Assert.That(summary.AddedLineCount, Is.EqualTo(2));
            Assert.That(summary.RemovedLineCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryExtractEditTargetPath_ReadsFreeformApplyPatchHeader() {
        var patch = """
            *** Begin Patch
            *** Update File: D:\Drive\Source\SquadDash-public\SquadDash\PlanViewerWindow.cs
            @@
            -old line
            +new line
            *** End Patch
            """;

        var path = ToolTranscriptFormatter.TryExtractEditTargetPath(patch);

        Assert.That(path, Is.EqualTo(@"D:\Drive\Source\SquadDash-public\SquadDash\PlanViewerWindow.cs"));
    }

    [TestCase("edit")]
    [TestCase("apply_patch")]
    [TestCase("APPLY_PATCH")]
    public void IsEditToolName_RecognizesFileEditTools(string toolName) {
        Assert.That(ToolTranscriptFormatter.IsEditToolName(toolName), Is.True);
    }

    [Test]
    public void BuildCompletedText_ForFailedEdit_AppendsErrorReasonFromOutputText() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"SquadDash\MainWindow.xaml.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: "Multiple matches found\nCode: failure");

        Assert.That(text, Is.EqualTo(@"✏️SquadDash\MainWindow.xaml.cs failed -- Multiple matches found"));
    }

    [Test]
    public void BuildCompletedText_ForFailedEdit_OmitsReasonWhenOutputTextIsEmpty() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"SquadDash\MainWindow.xaml.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: null);

        Assert.That(text, Is.EqualTo(@"✏️SquadDash\MainWindow.xaml.cs failed."));
    }

    [Test]
    public void BuildCompletedText_ForFailedEdit_OmitsReasonWhenOutputIsOnlyCodeLine() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"SquadDash\MainWindow.xaml.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: "Code: failure");

        Assert.That(text, Is.EqualTo(@"✏️SquadDash\MainWindow.xaml.cs failed."));
    }

    [Test]
    public void BuildCompletedText_ForFailedGenericTool_AppendsErrorReasonFromOutputText() {
        var descriptor = new ToolTranscriptDescriptor(
            "read_agent",
            Description: "some-agent-id");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: "Agent not found\nCode: failure");

        Assert.That(text, Is.EqualTo("some-agent-id failed -- Agent not found."));
    }

    [Test]
    public void ResolveToolIconKey_ForRg_UsesGrepIcon() {
        var iconKey = ToolTranscriptFormatter.ResolveToolIconKey("rg");

        Assert.That(iconKey, Is.EqualTo("ToolIcon_grep"));
    }

    [Test]
    public void BuildRipGrepDisplayText_WithSingleScope_UsesPatternDashScope() {
        var text = ToolTranscriptFormatter.BuildRipGrepDisplayText(
            "class PlanTaskStatus|record PlanTaskStatus|…",
            [@"SquadDash"]);

        Assert.That(text, Is.EqualTo("class PlanTaskStatus|record PlanTaskStatus|… — SquadDash"));
    }

    [Test]
    public void BuildRipGrepDisplayText_WithMultipleScopes_UsesFirstScopeAndAdditionalCount() {
        var text = ToolTranscriptFormatter.BuildRipGrepDisplayText(
            "pattern",
            [@"SquadDash\Screens", @"SquadDash\Tests", @"SquadDash\Assets"]);

        Assert.That(text, Is.EqualTo("pattern — Screens +2"));
    }

    [Test]
    public void BuildRipGrepDisplayText_WithoutScopes_FallsBackToPatternOnly() {
        Assert.Multiple(() => {
            Assert.That(
                ToolTranscriptFormatter.BuildRipGrepDisplayText("pattern", null),
                Is.EqualTo("pattern"));
            Assert.That(
                ToolTranscriptFormatter.BuildRipGrepDisplayText("pattern", []),
                Is.EqualTo("pattern"));
        });
    }

    [Test]
    public void ClassifyToolActivityKind_ForRg_IsReading() {
        var activity = ToolTranscriptFormatter.ClassifyToolActivityKind("rg");

        Assert.That(activity, Is.EqualTo(SpinnerActivityKind.Reading));
    }

    [Test]
    public void ResolveToolIconKey_ForDelegateRosterAgent_UsesTaskIcon() {
        var iconKey = ToolTranscriptFormatter.ResolveToolIconKey("delegate_roster_agent");

        Assert.That(iconKey, Is.EqualTo("ToolIcon_task"));
    }

    [Test]
    public void ResolveToolIconKey_ForDelegateTemporaryAgent_UsesTaskIcon() {
        var iconKey = ToolTranscriptFormatter.ResolveToolIconKey("delegate_temporary_agent");

        Assert.That(iconKey, Is.EqualTo("ToolIcon_task"));
    }

    [Test]
    public void BuildRunningText_ForDelegateRosterAgent_UsesAgentIdentityOnly() {
        var descriptor = new ToolTranscriptDescriptor(
            "delegate_roster_agent",
            DisplayText: "@arjun-sen");

        var text = ToolTranscriptFormatter.BuildRunningText(descriptor);

        Assert.Multiple(() => {
            Assert.That(text, Is.EqualTo("🤖 @arjun-sen"));
            Assert.That(text, Does.Not.Contain("SQUADDASH_AGENT_ASSIGNMENT_JSON"));
            Assert.That(text, Does.Not.Contain("Delegate to"));
            Assert.That(text, Does.Not.Contain("Running"));
        });
    }

    [Test]
    public void BuildDetailContent_ForRgAndDelegateRosterAgent_PreservesToolNameAndArguments() {
        var rgDetail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("rg", DisplayText: "pattern — SquadDash"),
            "{\n  \"pattern\": \"pattern\",\n  \"paths\": [\"SquadDash\", \"SquadDash.Tests\"]\n}",
            null,
            new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 8, 19, 15, 0, 1, TimeSpan.FromHours(-4)),
            null,
            IsCompleted: true,
            Success: true);
        var delegateDetail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("delegate_roster_agent", DisplayText: "@arjun-sen"),
            "{\n  \"agent_handle\": \"arjun-sen\",\n  \"prompt\": \"SQUADDASH_AGENT_ASSIGNMENT_JSON:\\n{...}\\nassignment\"\n}",
            null,
            new DateTimeOffset(2026, 8, 19, 15, 1, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 8, 19, 15, 1, 1, TimeSpan.FromHours(-4)),
            null,
            IsCompleted: true,
            Success: true);

        var rgContent = ToolTranscriptFormatter.BuildDetailContent(rgDetail);
        var delegateContent = ToolTranscriptFormatter.BuildDetailContent(delegateDetail);

        Assert.Multiple(() => {
            Assert.That(rgContent, Does.Contain("[TOOL] rg"));
            Assert.That(rgContent, Does.Contain("\"pattern\": \"pattern\""));
            Assert.That(rgContent, Does.Contain("\"paths\": [\"SquadDash\", \"SquadDash.Tests\"]"));
            Assert.That(delegateContent, Does.Contain("[TOOL] delegate_roster_agent"));
            Assert.That(delegateContent, Does.Contain("\"agent_handle\": \"arjun-sen\""));
            Assert.That(delegateContent, Does.Contain("\"prompt\": \"SQUADDASH_AGENT_ASSIGNMENT_JSON"));
        });
    }
}
