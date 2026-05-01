using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class HostCommandRegistryTests {

    private string? _tempWorkspaceDir;

    [TearDown]
    public void TearDown() {
        if (_tempWorkspaceDir != null && Directory.Exists(_tempWorkspaceDir)) {
            Directory.Delete(_tempWorkspaceDir, recursive: true);
            _tempWorkspaceDir = null;
        }
    }

    private string CreateTempWorkspace() {
        var dir = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "test-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempWorkspaceDir = dir;
        return dir;
    }

    // ── Built-in commands ─────────────────────────────────────────────────────

    [Test]
    public void GetCommands_NoWorkspace_ReturnsSixBuiltInCommands() {
        var registry = new HostCommandRegistry();

        var commands = registry.GetCommands(workspaceFolder: null);

        Assert.That(commands, Has.Count.EqualTo(6));
    }

    [Test]
    public void GetCommands_AllSixBuiltInCommandNamesPresent() {
        var registry = new HostCommandRegistry();

        var names = registry.GetCommands(workspaceFolder: null)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.That(names, Is.EquivalentTo(new[] {
            "start_loop", "stop_loop", "get_queue_status",
            "open_panel", "inject_text", "clear_approved"
        }));
    }

    [TestCase("start_loop",        "Silent")]
    [TestCase("stop_loop",         "Silent")]
    [TestCase("get_queue_status",  "InjectResultAsContext")]
    [TestCase("open_panel",        "Silent")]
    [TestCase("inject_text",       "InjectResultAsContext")]
    [TestCase("clear_approved",    "Silent")]
    public void GetCommands_BuiltInCommand_HasCorrectResultBehavior(
        string commandName, string expectedBehaviorName) {
        var expected = Enum.Parse<HostCommandResultBehavior>(expectedBehaviorName);
        var registry = new HostCommandRegistry();

        var descriptor = registry.GetCommands(null)
            .Single(c => c.Name == commandName);

        Assert.That(descriptor.ResultBehavior, Is.EqualTo(expected));
    }

    [Test]
    public void GetCommands_OpenPanel_HasRequiredNameParameter() {
        var registry = new HostCommandRegistry();

        var descriptor = registry.GetCommands(null).Single(c => c.Name == "open_panel");
        var nameParam = descriptor.Parameters.SingleOrDefault(p => p.Name == "name");

        Assert.That(nameParam, Is.Not.Null);
        Assert.That(nameParam!.Required, Is.True);
    }

    [Test]
    public void GetCommands_InjectText_HasRequiredTextParameter() {
        var registry = new HostCommandRegistry();

        var descriptor = registry.GetCommands(null).Single(c => c.Name == "inject_text");
        var textParam = descriptor.Parameters.SingleOrDefault(p => p.Name == "text");

        Assert.That(textParam, Is.Not.Null);
        Assert.That(textParam!.Required, Is.True);
    }

    [Test]
    public void GetCommands_StartLoop_HasNoParameters() {
        var registry = new HostCommandRegistry();

        var descriptor = registry.GetCommands(null).Single(c => c.Name == "start_loop");

        Assert.That(descriptor.Parameters, Is.Empty);
    }

    // ── BuildCatalogInstruction ───────────────────────────────────────────────

    [Test]
    public void BuildCatalogInstruction_IncludesAllSixCommandNames() {
        var registry = new HostCommandRegistry();

        var catalog = registry.BuildCatalogInstruction(workspaceFolder: null);

        Assert.Multiple(() => {
            Assert.That(catalog, Does.Contain("start_loop"));
            Assert.That(catalog, Does.Contain("stop_loop"));
            Assert.That(catalog, Does.Contain("get_queue_status"));
            Assert.That(catalog, Does.Contain("open_panel"));
            Assert.That(catalog, Does.Contain("inject_text"));
            Assert.That(catalog, Does.Contain("clear_approved"));
        });
    }

    [Test]
    public void BuildCatalogInstruction_MentionsHostCommandJsonFormat() {
        var registry = new HostCommandRegistry();

        var catalog = registry.BuildCatalogInstruction(workspaceFolder: null);

        Assert.That(catalog, Does.Contain("HOST_COMMAND_JSON"));
    }

    [Test]
    public void GetCommands_NullWorkspaceFolder_SameAsNoWorkspace() {
        var registry = new HostCommandRegistry();

        var withNull = registry.GetCommands(workspaceFolder: null);
        var withEmpty = registry.GetCommands(workspaceFolder: null);

        Assert.That(
            withNull.Select(c => c.Name),
            Is.EquivalentTo(withEmpty.Select(c => c.Name)));
    }

    // ── Workspace extension loading ───────────────────────────────────────────

    [Test]
    public void GetCommands_ValidCommandsJson_MergesCustomWithBuiltIns() {
        var workspace = CreateTempWorkspace();
        var squadDir = Path.Combine(workspace, ".squad");
        Directory.CreateDirectory(squadDir);
        File.WriteAllText(Path.Combine(squadDir, "commands.json"), """
            [
              {
                "name": "custom_deploy",
                "description": "Deploy the application",
                "parameters": [],
                "resultBehavior": "Silent"
              }
            ]
            """);

        var registry = new HostCommandRegistry();
        var commands = registry.GetCommands(workspace);

        Assert.That(commands.Any(c => c.Name == "custom_deploy"), Is.True);
        Assert.That(commands.Any(c => c.Name == "start_loop"), Is.True);
        Assert.That(commands.Count, Is.GreaterThan(6));
    }

    [Test]
    public void GetCommands_CommandsJsonMissing_ReturnsOnlyBuiltIns() {
        var workspace = CreateTempWorkspace();

        var registry = new HostCommandRegistry();
        var commands = registry.GetCommands(workspace);

        Assert.That(commands, Has.Count.EqualTo(6));
    }

    [Test]
    public void GetCommands_CommandsJsonInvalidJson_GracefullyReturnsOnlyBuiltIns() {
        var workspace = CreateTempWorkspace();
        var squadDir = Path.Combine(workspace, ".squad");
        Directory.CreateDirectory(squadDir);
        File.WriteAllText(Path.Combine(squadDir, "commands.json"), "this is not valid JSON %%%");

        var registry = new HostCommandRegistry();
        var commands = registry.GetCommands(workspace);

        Assert.That(commands, Has.Count.EqualTo(6));
    }

    [Test]
    public void GetCommands_CommandsJsonDuplicatesBuiltInName_BuiltInWins() {
        var workspace = CreateTempWorkspace();
        var squadDir = Path.Combine(workspace, ".squad");
        Directory.CreateDirectory(squadDir);
        File.WriteAllText(Path.Combine(squadDir, "commands.json"), """
            [
              {
                "name": "start_loop",
                "description": "Custom override attempt",
                "parameters": [],
                "resultBehavior": "Silent"
              }
            ]
            """);

        var registry = new HostCommandRegistry();
        var commands = registry.GetCommands(workspace);

        var startLoopEntries = commands.Where(c => c.Name == "start_loop").ToList();
        Assert.That(startLoopEntries, Has.Count.EqualTo(1));
        Assert.That(startLoopEntries[0].Description, Is.Not.EqualTo("Custom override attempt"));
    }
}
