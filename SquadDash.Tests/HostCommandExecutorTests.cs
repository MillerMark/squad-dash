using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class HostCommandExecutorTests {

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class RecordingCommandHandler : IHostCommandHandler {
        private readonly string _name;
        private readonly Func<IReadOnlyDictionary<string, string>, HostCommandResult> _execute;

        public List<IReadOnlyDictionary<string, string>> CallLog { get; } = new();

        public RecordingCommandHandler(
            string name,
            Func<IReadOnlyDictionary<string, string>, HostCommandResult>? execute = null) {
            _name = name;
            _execute = execute ?? (_ => new HostCommandResult(true));
        }

        public string CommandName => _name;

        public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
            CallLog.Add(parameters);
            return _execute(parameters);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HostCommandRegistry BuildRegistry() => new HostCommandRegistry();

    // ── Sequential execution order ────────────────────────────────────────────

    [Test]
    public void Execute_CommandsRunInArrayOrder() {
        var executionOrder = new List<string>();

        var handlerA = new RecordingCommandHandler("cmd_a",
            _ => { executionOrder.Add("cmd_a"); return new HostCommandResult(true); });
        var handlerB = new RecordingCommandHandler("cmd_b",
            _ => { executionOrder.Add("cmd_b"); return new HostCommandResult(true); });
        var handlerC = new RecordingCommandHandler("cmd_c",
            _ => { executionOrder.Add("cmd_c"); return new HostCommandResult(true); });

        var executor = new HostCommandExecutor();
        executor.Register(handlerA);
        executor.Register(handlerB);
        executor.Register(handlerC);

        var registry = BuildRegistry();
        var invocations = new[] {
            new HostCommandInvocation("cmd_a"),
            new HostCommandInvocation("cmd_b"),
            new HostCommandInvocation("cmd_c")
        };

        executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(executionOrder, Is.EqualTo(new[] { "cmd_a", "cmd_b", "cmd_c" }));
    }

    // ── Resilience ────────────────────────────────────────────────────────────

    [Test]
    public void Execute_OneCommandFails_RemainingCommandsStillRun() {
        var handlerA = new RecordingCommandHandler("cmd_a",
            _ => new HostCommandResult(false, ErrorMessage: "failure"));
        var handlerB = new RecordingCommandHandler("cmd_b");

        var executor = new HostCommandExecutor();
        executor.Register(handlerA);
        executor.Register(handlerB);

        var registry = BuildRegistry();
        var invocations = new[] {
            new HostCommandInvocation("cmd_a"),
            new HostCommandInvocation("cmd_b")
        };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(handlerB.CallLog, Has.Count.EqualTo(1));
    }

    [Test]
    public void Execute_UnknownCommand_SkippedGracefully_OtherCommandsRun() {
        var handlerB = new RecordingCommandHandler("known_cmd");

        var executor = new HostCommandExecutor();
        executor.Register(handlerB);

        var registry = BuildRegistry();
        var invocations = new[] {
            new HostCommandInvocation("unknown_cmd_xyz"),
            new HostCommandInvocation("known_cmd")
        };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(handlerB.CallLog, Has.Count.EqualTo(1));
        Assert.That(results.Any(r => !r.Result.Success || r.Invocation.Command == "known_cmd"), Is.True);
    }

    [Test]
    public void Execute_HandlerThrowsException_ResultHasSuccessFalse_NextCommandStillRuns() {
        var handlerA = new RecordingCommandHandler("cmd_a",
            _ => throw new InvalidOperationException("handler exploded"));
        var handlerB = new RecordingCommandHandler("cmd_b");

        var executor = new HostCommandExecutor();
        executor.Register(handlerA);
        executor.Register(handlerB);

        var registry = BuildRegistry();
        var invocations = new[] {
            new HostCommandInvocation("cmd_a"),
            new HostCommandInvocation("cmd_b")
        };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        var resultA = results.First(r => r.Invocation.Command == "cmd_a");
        Assert.That(resultA.Result.Success, Is.False);
        Assert.That(handlerB.CallLog, Has.Count.EqualTo(1));
    }

    [Test]
    public void Execute_EmptyInvocationsList_ReturnsEmptyResultList() {
        var executor = new HostCommandExecutor();
        var registry = BuildRegistry();

        var results = executor.Execute(Array.Empty<HostCommandInvocation>(), registry, workspaceFolder: null);

        Assert.That(results, Is.Empty);
    }

    // ── TryParseAndExecute ────────────────────────────────────────────────────

    [Test]
    public void TryParseAndExecute_ValidBlock_ParsesAndExecutes() {
        var handler = new RecordingCommandHandler("start_loop");

        var executor = new HostCommandExecutor();
        executor.Register(handler);

        const string response = """
            Starting the loop now.

            HOST_COMMAND_JSON:
            [
              { "command": "start_loop" }
            ]
            """;

        var results = executor.TryParseAndExecute(
            response, BuildRegistry(), workspaceFolder: null, out _);

        Assert.That(results, Is.Not.Null);
        Assert.That(results!, Has.Count.EqualTo(1));
        Assert.That(handler.CallLog, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryParseAndExecute_NoBlock_ReturnsNull() {
        var executor = new HostCommandExecutor();

        var results = executor.TryParseAndExecute(
            "Just a normal response.", BuildRegistry(), workspaceFolder: null, out _);

        Assert.That(results, Is.Null);
    }

    [Test]
    public void TryParseAndExecute_StripsBlockFromBodyWithoutCommandBlock() {
        var executor = new HostCommandExecutor();

        const string response = """
            Here is my answer.

            HOST_COMMAND_JSON:
            [
              { "command": "stop_loop" }
            ]
            """;

        executor.TryParseAndExecute(
            response, BuildRegistry(), workspaceFolder: null, out var body);

        Assert.That(body, Does.Not.Contain("HOST_COMMAND_JSON:"));
        Assert.That(body.Trim(), Does.Contain("Here is my answer."));
    }

    // ── Parameter validation ──────────────────────────────────────────────────

    [Test]
    public void Execute_MissingRequiredParameter_ResultHasSuccessFalse() {
        var executor = new HostCommandExecutor();
        var registry = BuildRegistry();

        // open_panel requires "name" — invoke without it
        var invocations = new[] {
            new HostCommandInvocation("open_panel", Parameters: null)
        };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Result.Success, Is.False);
    }

    [Test]
    public void Execute_AllRequiredParametersPresent_ExecutesSuccessfully() {
        var handler = new RecordingCommandHandler("open_panel");

        var executor = new HostCommandExecutor();
        executor.Register(handler);

        var registry = BuildRegistry();
        var invocations = new[] {
            new HostCommandInvocation("open_panel",
                Parameters: new Dictionary<string, string> { ["name"] = "Approvals" })
        };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(results[0].Result.Success, Is.True);
        Assert.That(handler.CallLog, Has.Count.EqualTo(1));
    }

    // ── HasOutput / ResultBehavior ────────────────────────────────────────────

    [Test]
    public void Execute_SilentCommand_ResultDoesNotHaveOutput() {
        var handler = new RecordingCommandHandler("start_loop",
            _ => new HostCommandResult(true, Output: null));

        var executor = new HostCommandExecutor();
        executor.Register(handler);

        var registry = BuildRegistry();
        var invocations = new[] { new HostCommandInvocation("start_loop") };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(results[0].Result.HasOutput, Is.False);
    }

    [Test]
    public void Execute_InjectResultAsContextCommand_WithOutput_HasOutputIsTrue() {
        var handler = new RecordingCommandHandler("get_queue_status",
            _ => new HostCommandResult(true, Output: "Queue: 3 items pending"));

        var executor = new HostCommandExecutor();
        executor.Register(handler);

        var registry = BuildRegistry();
        var invocations = new[] { new HostCommandInvocation("get_queue_status") };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(results[0].Result.HasOutput, Is.True);
        Assert.That(results[0].Result.Output, Is.EqualTo("Queue: 3 items pending"));
    }

    [Test]
    public void Execute_MultipleInjectResultAsContextCommands_AllResultsReturnedWithOutputs() {
        var handlerQS = new RecordingCommandHandler("get_queue_status",
            _ => new HostCommandResult(true, Output: "Queue: 2 items"));
        var handlerIT = new RecordingCommandHandler("inject_text",
            _ => new HostCommandResult(true, Output: "Text injected"));

        var executor = new HostCommandExecutor();
        executor.Register(handlerQS);
        executor.Register(handlerIT);

        var registry = BuildRegistry();
        var invocations = new[] {
            new HostCommandInvocation("get_queue_status"),
            new HostCommandInvocation("inject_text",
                Parameters: new Dictionary<string, string> { ["text"] = "hello" })
        };

        var results = executor.Execute(invocations, registry, workspaceFolder: null);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(r => r.Result.HasOutput), Is.True);
    }
}
