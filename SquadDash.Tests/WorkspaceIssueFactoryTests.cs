namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceIssueFactoryTests {
    // ── CreateSimulatedRuntimeErrorMessage ─────────────────────────────────

    [Test]
    public void CreateSimulatedRuntimeErrorMessage_CopilotAuthRequired_ContainsAuthKeyword() {
        var message = WorkspaceIssueFactory.CreateSimulatedRuntimeErrorMessage(
            DeveloperRuntimeIssueSimulation.CopilotAuthRequired);

        Assert.That(message, Does.Contain("Session was not created with authentication info"));
    }

    [Test]
    public void CreateSimulatedRuntimeErrorMessage_BundledSdkRepair_ContainsParsingKeyword() {
        var message = WorkspaceIssueFactory.CreateSimulatedRuntimeErrorMessage(
            DeveloperRuntimeIssueSimulation.BundledSdkRepair);

        Assert.That(message, Does.Contain("Error parsing:"));
    }

    [Test]
    public void CreateSimulatedRuntimeErrorMessage_BuildTempFiles_ContainsAccessDeniedKeywords() {
        var message = WorkspaceIssueFactory.CreateSimulatedRuntimeErrorMessage(
            DeveloperRuntimeIssueSimulation.BuildTempFiles);

        Assert.Multiple(() => {
            Assert.That(message, Does.Contain("Access to the path"));
            Assert.That(message, Does.Contain(".tmp"));
        });
    }

    [Test]
    public void CreateSimulatedRuntimeErrorMessage_GenericRuntimeFailure_IsNotNullOrEmpty() {
        var message = WorkspaceIssueFactory.CreateSimulatedRuntimeErrorMessage(
            DeveloperRuntimeIssueSimulation.GenericRuntimeFailure);

        Assert.That(message, Is.Not.Null.And.Not.Empty);
    }

    // ── CreateRuntimeIssue ─────────────────────────────────────────────────

    [Test]
    public void CreateRuntimeIssue_WhenAuthError_ReturnsCopilotAuthTitle() {
        const string errorMessage =
            "Execution failed: Error: Session was not created with authentication info or custom provider";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("GitHub Copilot sign-in"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenAuthError_HasCopilotDocsLink() {
        const string errorMessage =
            "Error: Session was not created with authentication info or custom provider";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.PrimaryLink, Is.Not.Null);
        Assert.That(issue.PrimaryLink!.Url, Does.Contain("docs.github.com/copilot"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenBuildLockError_ReturnsBuildTempFilesTitle() {
        const string errorMessage =
            @"Access to the path 'C:\Source\SquadUI\SquadDash\obj\Debug\net10.0-windows\SquadDash.tmp' is denied.";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("Build couldn't update temp files"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenBuildLockError_ProvidesRunBuildAction() {
        const string errorMessage =
            @"Access to the path 'C:\Source\SquadUI\SquadDash\obj\Debug\net10.0-windows\SquadDash.tmp' is denied.";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Action, Is.Not.Null);
        Assert.That(issue.Action!.Kind, Is.EqualTo(WorkspaceIssueActionKind.LaunchPowerShellCommand));
    }

    [Test]
    public void CreateRuntimeIssue_WhenErrorParsingKeyword_ReturnsBundledSdkRepairTitle() {
        const string errorMessage = "Error parsing: C:\\Source\\SquadUI\\Squad.SDK\\node_modules\\package.json";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("bundled Squad SDK needs repair"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenVscodeJsonrpcKeyword_ReturnsBundledSdkRepairTitle() {
        const string errorMessage = "node:internal/modules vscode-jsonrpc failed to initialize";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("bundled Squad SDK needs repair"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenDependenciesNotFound_ReturnsBundledSdkMissingTitle() {
        const string errorMessage = "Required dependencies were not found under the SDK directory.";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("Bundled Squad SDK files are missing or damaged"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenCompatibilityRepairFailed_ReturnsBundledSdkMissingTitle() {
        const string errorMessage = "compatibility repair failed: missing native binaries";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("Bundled Squad SDK files are missing or damaged"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenGenericError_ReturnsGenericFailureTitle() {
        const string errorMessage = "The Squad SDK process exited before the prompt completed.";

        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, null);

        Assert.That(issue.Title, Does.Contain("Squad couldn't finish that prompt"));
    }

    [Test]
    public void CreateRuntimeIssue_WhenNullErrorMessage_ReturnsGenericFailureTitle() {
        var issue = WorkspaceIssueFactory.CreateRuntimeIssue(null!, null);

        Assert.That(issue.Title, Does.Contain("Squad couldn't finish that prompt"));
    }

    [Test]
    public void CreateRuntimeIssue_AlwaysReturnsNonNullPresentation() {
        var issue = WorkspaceIssueFactory.CreateRuntimeIssue("some error", null);

        Assert.That(issue, Is.Not.Null);
    }

    // ── CreateSimulatedRuntimeIssue ────────────────────────────────────────

    [Test]
    public void CreateSimulatedRuntimeIssue_PrependsPreviewToTitle() {
        var issue = WorkspaceIssueFactory.CreateSimulatedRuntimeIssue(
            DeveloperRuntimeIssueSimulation.GenericRuntimeFailure,
            null);

        Assert.That(issue.Title, Does.StartWith("Preview:"));
    }

    [Test]
    public void CreateSimulatedRuntimeIssue_DisablesInstallAndDoctorButtons() {
        var issue = WorkspaceIssueFactory.CreateSimulatedRuntimeIssue(
            DeveloperRuntimeIssueSimulation.CopilotAuthRequired,
            null);

        Assert.Multiple(() => {
            Assert.That(issue.ShowInstallButton, Is.False);
            Assert.That(issue.ShowDoctorButton, Is.False);
        });
    }

    // ── CreateStartupIssue (simulated) ─────────────────────────────────────

    [Test]
    public void CreateStartupIssue_WithSquadNotInstalledSimulation_ReturnsInstallIssue() {
        var issue = WorkspaceIssueFactory.CreateStartupIssue(
            null,
            DeveloperStartupIssueSimulation.SquadNotInstalled);

        Assert.That(issue, Is.Not.Null);
        Assert.That(issue!.Title, Does.Contain("Squad isn't installed"));
        Assert.That(issue.ShowInstallButton, Is.True);
    }

    [Test]
    public void CreateStartupIssue_WithPartialInstallSimulation_ReturnsFinishInstallIssue() {
        var issue = WorkspaceIssueFactory.CreateStartupIssue(
            null,
            DeveloperStartupIssueSimulation.PartialSquadInstall);

        Assert.That(issue, Is.Not.Null);
        Assert.That(issue!.Title, Does.Contain("Finish installing Squad"));
        Assert.That(issue.ShowInstallButton, Is.True);
    }

    [Test]
    public void CreateStartupIssue_WithMissingNodeToolingSimulation_ReturnsMissingToolingIssue() {
        var issue = WorkspaceIssueFactory.CreateStartupIssue(
            null,
            DeveloperStartupIssueSimulation.MissingNodeTooling);

        Assert.That(issue, Is.Not.Null);
        Assert.That(issue!.Title, Does.Contain("Node.js tooling is missing"));
        Assert.That(issue.PrimaryLink, Is.Not.Null);
        Assert.That(issue.PrimaryLink!.Url, Does.Contain("nodejs.org"));
    }

    [Test]
    public void CreateStartupIssue_WithSimulation_DetailTextContainsSimulationNotice() {
        var issue = WorkspaceIssueFactory.CreateStartupIssue(
            null,
            DeveloperStartupIssueSimulation.SquadNotInstalled);

        Assert.That(issue!.DetailText, Does.Contain("Developer simulation is active."));
    }

    // ── CreateStartupIssue (real, with explicit installationState) ─────────

    [Test]
    public void CreateStartupIssue_WhenFullyInstalled_ReturnsNullWhenToolsPresent() {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.GetPath(".squad"));
        workspace.CreateFile(Path.Combine(".squad", "team.md"));
        workspace.CreateFile(Path.Combine("node_modules", ".bin", "squad.cmd"));

        var service = new SquadInstallationStateService();
        var state = service.GetState(workspace.RootPath);

        // This test only validates the non-simulated path when Squad is fully installed.
        // GetMissingTools() checks PATH for node/npm/npx; skip if those are absent.
        Assume.That(state.IsSquadInstalledForActiveDirectory, Is.True);

        // If node tools are present on PATH, a fully-installed workspace returns null.
        // If node tools are absent the method returns a missing-tools issue instead;
        // that branch is covered by the simulated tests above.
        var issue = WorkspaceIssueFactory.CreateStartupIssue(state);

        Assert.That(issue is null || issue.Title.Contains("Node.js"), Is.True);
    }

    [Test]
    public void CreateStartupIssue_WhenPartialInstall_ReturnsFinishInstallIssue() {
        using var workspace = new TestWorkspace();
        var state = new SquadInstallationState(
            ActiveDirectory: workspace.RootPath,
            SquadFolderPath: workspace.GetPath(".squad"),
            TeamFilePath: workspace.GetPath(".squad", "team.md"),
            PackageJsonPath: workspace.GetPath("package.json"),
            LocalSquadCommandPath: workspace.GetPath("node_modules", ".bin", "squad.cmd"),
            IsWorkspaceInitialized: true,
            HasPackageManifest: true,
            HasLocalCliCommand: false,
            IsSquadInstalledForActiveDirectory: false);

        var issue = WorkspaceIssueFactory.CreateStartupIssue(state);

        // Skip the test if environment is missing node/npm/npx or pwsh.
        Assume.That(issue, Is.Not.Null);
        Assume.That(issue!.Title, Does.Not.Contain("Node.js").And.Not.Contain("PowerShell"));

        Assert.Multiple(() => {
            Assert.That(issue.Title, Does.Contain("Finish installing Squad"));
            Assert.That(issue.ShowInstallButton, Is.True);
        });
    }

    [Test]
    public void CreateStartupIssue_WhenNothingInstalled_ReturnsSquadNotInstalledIssue() {
        using var workspace = new TestWorkspace();
        var state = new SquadInstallationState(
            ActiveDirectory: workspace.RootPath,
            SquadFolderPath: workspace.GetPath(".squad"),
            TeamFilePath: workspace.GetPath(".squad", "team.md"),
            PackageJsonPath: workspace.GetPath("package.json"),
            LocalSquadCommandPath: workspace.GetPath("node_modules", ".bin", "squad.cmd"),
            IsWorkspaceInitialized: false,
            HasPackageManifest: false,
            HasLocalCliCommand: false,
            IsSquadInstalledForActiveDirectory: false);

        var issue = WorkspaceIssueFactory.CreateStartupIssue(state);

        // Skip the test if environment is missing node/npm/npx or pwsh.
        Assume.That(issue, Is.Not.Null);
        Assume.That(issue!.Title, Does.Not.Contain("Node.js").And.Not.Contain("PowerShell"));

        Assert.Multiple(() => {
            Assert.That(issue.Title, Does.Contain("Squad isn't installed"));
            Assert.That(issue.ShowInstallButton, Is.True);
        });
    }

    [Test]
    public void CreateStartupIssue_WhenNullInstallationState_ReturnsNullOrToolingIssue() {
        var issue = WorkspaceIssueFactory.CreateStartupIssue(null);

        Assert.That(issue is null || issue.Title.Contains("Node.js") || issue.Title.Contains("PowerShell"), Is.True);
    }
}
