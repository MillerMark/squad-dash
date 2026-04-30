using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadSdkProcessSerializationTests {
    [Test]
    public void PromptRequest_UsesLowercaseJsonPropertyNames() {
        var json = JsonSerializer.Serialize(new SquadSdkPromptRequest(
            "status?",
            @"D:\Drive\Source\SquadUI",
            "session-123",
            @"D:\Users\Mark\AppData\Local\SquadDash\workspaces\repo\sdk-config"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"prompt\":\"status?\""));
            Assert.That(json, Does.Contain("\"cwd\":\"D:\\\\Drive\\\\Source\\\\SquadUI\""));
            Assert.That(json, Does.Contain("\"sessionId\":\"session-123\""));
            Assert.That(json, Does.Contain("\"configDir\":\"D:\\\\Users\\\\Mark\\\\AppData\\\\Local\\\\SquadDash\\\\workspaces\\\\repo\\\\sdk-config\""));
            Assert.That(json, Does.Not.Contain("\"Prompt\""));
            Assert.That(json, Does.Not.Contain("\"Cwd\""));
            Assert.That(json, Does.Not.Contain("\"SessionId\""));
            Assert.That(json, Does.Not.Contain("\"ConfigDirectory\""));
        });
    }

    [Test]
    public void DelegateRequest_UsesLowercaseJsonPropertyNames() {
        var json = JsonSerializer.Serialize(new SquadSdkDelegateRequest(
            "Hand off to Lyra",
            "lyra-morn",
            @"D:\Drive\Source\SquadUI",
            "session-456",
            @"D:\Users\Mark\AppData\Local\SquadDash\workspaces\repo\sdk-config"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"selectedOption\":\"Hand off to Lyra\""));
            Assert.That(json, Does.Contain("\"targetAgent\":\"lyra-morn\""));
            Assert.That(json, Does.Contain("\"cwd\":\"D:\\\\Drive\\\\Source\\\\SquadUI\""));
            Assert.That(json, Does.Contain("\"sessionId\":\"session-456\""));
            Assert.That(json, Does.Contain("\"configDir\":\"D:\\\\Users\\\\Mark\\\\AppData\\\\Local\\\\SquadDash\\\\workspaces\\\\repo\\\\sdk-config\""));
            Assert.That(json, Does.Contain("\"type\":\"delegate\""));
            Assert.That(json, Does.Not.Contain("\"SelectedOption\""));
            Assert.That(json, Does.Not.Contain("\"TargetAgent\""));
            Assert.That(json, Does.Not.Contain("\"ConfigDirectory\""));
        });
    }

    [Test]
    public void CancelBackgroundTaskRequest_UsesLowercaseJsonPropertyNames() {
        var json = JsonSerializer.Serialize(new SquadSdkCancelBackgroundTaskRequest(
            "task-123",
            "session-789"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"taskId\":\"task-123\""));
            Assert.That(json, Does.Contain("\"sessionId\":\"session-789\""));
            Assert.That(json, Does.Contain("\"type\":\"cancel_background_task\""));
            Assert.That(json, Does.Not.Contain("\"TaskId\""));
            Assert.That(json, Does.Not.Contain("\"SessionId\""));
        });
    }

    [Test]
    public void RunLoopRequest_UsesCorrectJsonPropertyNames() {
        var json = JsonSerializer.Serialize(new SquadSdkRunLoopRequest("C:\\workspace\\loop.md", "C:\\workspace", null, null));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"type\":\"run_loop\""));
            Assert.That(json, Does.Contain("\"loopMdPath\":\"C:\\\\workspace\\\\loop.md\""));
            Assert.That(json, Does.Contain("\"cwd\":\"C:\\\\workspace\""));
            Assert.That(json, Does.Not.Contain("\"Type\""));
            Assert.That(json, Does.Not.Contain("\"LoopMdPath\""));
        });
    }

    [Test]
    public void RcStartRequest_UsesCorrectJsonPropertyNames() {
        var json = JsonSerializer.Serialize(new SquadSdkRcStartRequest(3000, "my-repo", "main", "my-machine", "C:\\workspace\\.squad", "C:\\workspace", "req-1", null));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"port\":3000"));
            Assert.That(json, Does.Contain("\"repo\":\"my-repo\""));
            Assert.That(json, Does.Contain("\"branch\":\"main\""));
            Assert.That(json, Does.Contain("\"machine\":\"my-machine\""));
            Assert.That(json, Does.Contain("\"type\":\"rc_start\""));
            Assert.That(json, Does.Not.Contain("\"Port\""));
            Assert.That(json, Does.Not.Contain("\"Repo\""));
            Assert.That(json, Does.Not.Contain("\"Branch\""));
            Assert.That(json, Does.Not.Contain("\"sessionId\""));
            Assert.That(json, Does.Not.Contain("\"tunnelMode\""));
            Assert.That(json, Does.Not.Contain("\"tunnelToken\""));
        });
    }

    [Test]
    public void RcStartRequest_WithTunnelMode_NgrokIncludesFields() {
        var json = JsonSerializer.Serialize(new SquadSdkRcStartRequest(
            3000, "my-repo", "main", "my-machine",
            "C:\\workspace\\.squad", "C:\\workspace", "req-1",
            null, "ngrok", "my-authtoken"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"tunnelMode\":\"ngrok\""));
            Assert.That(json, Does.Contain("\"tunnelToken\":\"my-authtoken\""));
            Assert.That(json, Does.Not.Contain("\"TunnelMode\""));
            Assert.That(json, Does.Not.Contain("\"TunnelToken\""));
        });
    }

    [Test]
    public void RcStartRequest_WithTunnelMode_CloudflareIncludesField() {
        var json = JsonSerializer.Serialize(new SquadSdkRcStartRequest(
            3000, "my-repo", "main", "my-machine",
            "C:\\workspace\\.squad", "C:\\workspace", "req-1",
            null, "cloudflare", null));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"tunnelMode\":\"cloudflare\""));
            Assert.That(json, Does.Not.Contain("\"tunnelToken\""));
        });
    }

    [Test]
    public void RcStartRequest_WithNullTunnelMode_OmitsTunnelFields() {
        var json = JsonSerializer.Serialize(new SquadSdkRcStartRequest(
            3000, "my-repo", "main", "my-machine",
            "C:\\workspace\\.squad", "C:\\workspace", "req-1",
            null, null, null));

        Assert.Multiple(() => {
            Assert.That(json, Does.Not.Contain("\"tunnelMode\""));
            Assert.That(json, Does.Not.Contain("\"tunnelToken\""));
        });
    }

    [Test]
    public void RcStopRequest_UsesCorrectJsonPropertyNames() {
        var json = JsonSerializer.Serialize(new SquadSdkRcStopRequest("req-2"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"requestId\":\"req-2\""));
            Assert.That(json, Does.Contain("\"type\":\"rc_stop\""));
            Assert.That(json, Does.Not.Contain("\"RequestId\""));
            Assert.That(json, Does.Not.Contain("\"Type\""));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesLoopIterationPayload() {
        const string json = """
            {
              "type": "loop_iteration",
              "loopIteration": 3,
              "loopMdPath": "C:\\workspace\\loop.md",
              "outputLine": "Running iteration 3"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("loop_iteration"));
            Assert.That(evt.LoopIteration, Is.EqualTo(3));
            Assert.That(evt.LoopMdPath, Is.EqualTo("C:\\workspace\\loop.md"));
            Assert.That(evt.OutputLine, Is.EqualTo("Running iteration 3"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesWatchFleetPayload() {
        const string json = """
            {
              "type": "watch_fleet_dispatched",
              "watchFleetSize": 5,
              "watchCycleId": "cycle-abc"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("watch_fleet_dispatched"));
            Assert.That(evt.WatchFleetSize, Is.EqualTo(5));
            Assert.That(evt.WatchCycleId, Is.EqualTo("cycle-abc"));
        });
    }

    /// <summary>
    /// Verifies deserialization of the rc_started event using the exact camelCase field names
    /// emitted by the TypeScript runPrompt.ts bridge (rcPort, rcToken, rcUrl, rcLanUrl).
    /// </summary>
    [Test]
    public void SquadSdkEvent_DeserializesRcStartedPayload() {
        const string json = """
            {
              "type": "rc_started",
              "rcPort": 3000,
              "rcToken": "tok-xyz",
              "rcUrl": "http://localhost:3000",
              "rcLanUrl": "http://192.168.1.100:3000"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_started"));
            Assert.That(evt.RcPort, Is.EqualTo(3000));
            Assert.That(evt.RcToken, Is.EqualTo("tok-xyz"));
            Assert.That(evt.RcUrl, Is.EqualTo("http://localhost:3000"));
            Assert.That(evt.RcLanUrl, Is.EqualTo("http://192.168.1.100:3000"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesToolLifecyclePayload() {
        const string json = """
            {
              "type": "tool_complete",
              "toolCallId": "tool-123",
              "toolName": "powershell",
              "startedAt": "2026-04-06T14:24:00.0000000-04:00",
              "finishedAt": "2026-04-06T14:24:03.0000000-04:00",
              "description": "Check repo",
              "command": "git status",
              "success": false,
              "outputText": "fatal: not a git repository",
              "args": {
                "command": "git status"
              }
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("tool_complete"));
            Assert.That(evt.ToolCallId, Is.EqualTo("tool-123"));
            Assert.That(evt.ToolName, Is.EqualTo("powershell"));
            Assert.That(evt.Command, Is.EqualTo("git status"));
            Assert.That(evt.Success, Is.False);
            Assert.That(evt.OutputText, Is.EqualTo("fatal: not a git repository"));
            Assert.That(evt.Args.GetProperty("command").GetString(), Is.EqualTo("git status"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSessionReadyPayload() {
        const string json = """
            {
              "type": "session_ready",
              "sessionId": "session-456",
              "sessionResumed": true,
              "sessionReuseKind": "provider_resume",
              "sessionAcquireDurationMs": 912,
              "sessionResumeDurationMs": 875,
              "sessionAgeMs": 42000,
              "sessionPromptCountBeforeCurrent": 7,
              "sessionPromptCountIncludingCurrent": 8,
              "backgroundAgentCount": 0,
              "backgroundShellCount": 1,
              "knownSubagentCount": 2,
              "activeToolCount": 0,
              "cachedAssistantChars": 1440,
              "restoredContextSummary": "messageCount=42, summaryCount=3"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("session_ready"));
            Assert.That(evt.SessionId, Is.EqualTo("session-456"));
            Assert.That(evt.SessionResumed, Is.True);
            Assert.That(evt.SessionReuseKind, Is.EqualTo("provider_resume"));
            Assert.That(evt.SessionAcquireDurationMs, Is.EqualTo(912));
            Assert.That(evt.SessionResumeDurationMs, Is.EqualTo(875));
            Assert.That(evt.SessionAgeMs, Is.EqualTo(42000));
            Assert.That(evt.SessionPromptCountBeforeCurrent, Is.EqualTo(7));
            Assert.That(evt.SessionPromptCountIncludingCurrent, Is.EqualTo(8));
            Assert.That(evt.BackgroundAgentCount, Is.EqualTo(0));
            Assert.That(evt.BackgroundShellCount, Is.EqualTo(1));
            Assert.That(evt.KnownSubagentCount, Is.EqualTo(2));
            Assert.That(evt.ActiveToolCount, Is.EqualTo(0));
            Assert.That(evt.CachedAssistantChars, Is.EqualTo(1440));
            Assert.That(evt.RestoredContextSummary, Is.EqualTo("messageCount=42, summaryCount=3"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSdkDiagnosticsPayload() {
        const string json = """
            {
              "type": "sdk_diagnostics",
              "requestId": "request-123",
              "diagnosticPhase": "send_completed",
              "diagnosticEventType": "message_delta",
              "sendMethod": "session.sendAndWait",
              "diagnosticAt": "2026-04-21T20:07:34.190Z",
              "sendStartedAt": "2026-04-21T20:07:34.180Z",
              "firstSdkEventAt": "2026-04-21T20:07:44.180Z",
              "firstSdkEventType": "reasoning_delta",
              "firstThinkingAt": "2026-04-21T20:07:44.180Z",
              "firstResponseAt": "2026-04-21T20:08:14.180Z",
              "sendCompletedAt": "2026-04-21T20:08:20.180Z",
              "millisecondsSinceSendStart": 46000,
              "timeToFirstSdkEventMs": 10000,
              "timeToFirstThinkingMs": 10000,
              "timeToFirstResponseMs": 40000
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("sdk_diagnostics"));
            Assert.That(evt.RequestId, Is.EqualTo("request-123"));
            Assert.That(evt.DiagnosticPhase, Is.EqualTo("send_completed"));
            Assert.That(evt.DiagnosticEventType, Is.EqualTo("message_delta"));
            Assert.That(evt.SendMethod, Is.EqualTo("session.sendAndWait"));
            Assert.That(evt.FirstSdkEventType, Is.EqualTo("reasoning_delta"));
            Assert.That(evt.MillisecondsSinceSendStart, Is.EqualTo(46000));
            Assert.That(evt.TimeToFirstSdkEventMs, Is.EqualTo(10000));
            Assert.That(evt.TimeToFirstThinkingMs, Is.EqualTo(10000));
            Assert.That(evt.TimeToFirstResponseMs, Is.EqualTo(40000));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesThinkingSpeakerPayload() {
        const string json = """
            {
              "type": "thinking_delta",
              "speaker": "Squad",
              "text": "Let me inspect the repo."
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("thinking_delta"));
            Assert.That(evt.Speaker, Is.EqualTo("Squad"));
            Assert.That(evt.Text, Is.EqualTo("Let me inspect the repo."));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesUsagePayload() {
        const string json = """
            {
              "type": "usage",
              "requestId": "request-789",
              "model": "claude-sonnet-4.6",
              "totalInputTokens": 321,
              "totalOutputTokens": 123,
              "totalTokens": 444
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("usage"));
            Assert.That(evt.RequestId, Is.EqualTo("request-789"));
            Assert.That(evt.Model, Is.EqualTo("claude-sonnet-4.6"));
            Assert.That(evt.TotalInputTokens, Is.EqualTo(321));
            Assert.That(evt.TotalOutputTokens, Is.EqualTo(123));
            Assert.That(evt.TotalTokens, Is.EqualTo(444));
        });
    }

    [Test]
    public void RcStatusBroadcastRequest_SerializesCorrectly() {
        var busyJson = JsonSerializer.Serialize(new SquadSdkRcStatusBroadcastRequest("busy"));
        var idleJson = JsonSerializer.Serialize(new SquadSdkRcStatusBroadcastRequest("idle"));

        Assert.Multiple(() => {
            Assert.That(busyJson, Does.Contain("\"type\":\"rc_status_broadcast\""));
            Assert.That(busyJson, Does.Contain("\"status\":\"busy\""));
            Assert.That(busyJson, Does.Not.Contain("\"Status\""));
            Assert.That(busyJson, Does.Not.Contain("\"Type\""));

            Assert.That(idleJson, Does.Contain("\"status\":\"idle\""));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesRcAudioStartPayload() {
        const string json = """
            {
              "type": "rc_audio_start",
              "connectionId": "conn-abc-123"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_audio_start"));
            Assert.That(evt.ConnectionId, Is.EqualTo("conn-abc-123"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesRcAudioChunkPayload() {
        // Build a small fake PCM chunk and encode it
        var fakePcm = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var b64 = Convert.ToBase64String(fakePcm);

        var json = $$"""
            {
              "type": "rc_audio_chunk",
              "connectionId": "conn-abc-123",
              "audioData": "{{b64}}"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_audio_chunk"));
            Assert.That(evt.ConnectionId, Is.EqualTo("conn-abc-123"));
            Assert.That(evt.AudioData, Is.EqualTo(b64));
        });

        // Verify round-trip: base64 → bytes should match original
        var decoded = Convert.FromBase64String(evt!.AudioData!);
        Assert.That(decoded, Is.EqualTo(fakePcm));
    }

    [Test]
    public void SquadSdkEvent_DeserializesRcAudioEndPayload() {
        const string json = """
            {
              "type": "rc_audio_end",
              "connectionId": "conn-abc-123"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_audio_end"));
            Assert.That(evt.ConnectionId, Is.EqualTo("conn-abc-123"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesRcTunnelStartedPayload() {
        const string json = """
            {
              "type": "rc_tunnel_started",
              "rcTunnelUrl": "https://abc123.ngrok-free.app"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_tunnel_started"));
            Assert.That(evt.RcTunnelUrl, Is.EqualTo("https://abc123.ngrok-free.app"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesRcTunnelErrorPayload() {
        const string json = """
            {
              "type": "rc_tunnel_error",
              "message": "ngrok tunnel did not surface a public URL within 12 s."
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_tunnel_error"));
            Assert.That(evt.Message, Does.Contain("ngrok tunnel"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesRcTunnelStartedPayload_Cloudflare() {
        const string json = """
            {
              "type": "rc_tunnel_started",
              "rcTunnelUrl": "https://sleek-river-xyz.trycloudflare.com"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("rc_tunnel_started"));
            Assert.That(evt.RcTunnelUrl, Does.Contain("trycloudflare.com"));
        });
    }

    [Test]
    public void SubSquadsListRequest_SerializesCorrectly() {
        var json = JsonSerializer.Serialize(new SquadSdkSubSquadsListRequest(
            @"D:\Drive\Source\MyRepo",
            "req-abc-001"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"type\":\"subsquads_list\""));
            Assert.That(json, Does.Contain("\"cwd\":\"D:\\\\Drive\\\\Source\\\\MyRepo\""));
            Assert.That(json, Does.Contain("\"requestId\":\"req-abc-001\""));
            Assert.That(json, Does.Not.Contain("\"Type\""));
            Assert.That(json, Does.Not.Contain("\"Cwd\""));
            Assert.That(json, Does.Not.Contain("\"RequestId\""));
        });
    }

    [Test]
    public void SubSquadsActivateRequest_SerializesCorrectly() {
        var json = JsonSerializer.Serialize(new SquadSdkSubSquadsActivateRequest(
            "ui-team",
            @"D:\Drive\Source\MyRepo",
            "req-abc-002"));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"type\":\"subsquads_activate\""));
            Assert.That(json, Does.Contain("\"subSquadName\":\"ui-team\""));
            Assert.That(json, Does.Contain("\"cwd\":\"D:\\\\Drive\\\\Source\\\\MyRepo\""));
            Assert.That(json, Does.Contain("\"requestId\":\"req-abc-002\""));
            Assert.That(json, Does.Not.Contain("\"SubSquadName\""));
            Assert.That(json, Does.Not.Contain("\"Type\""));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSubSquadsListedPayload_Configured() {
        const string json = """
            {
              "type": "subsquads_listed",
              "requestId": "req-abc-001",
              "subsquadsConfigured": true,
              "subsquadsCount": 2,
              "workstreamsJson": "[{\"name\":\"ui\",\"labelFilter\":\"team:ui\",\"workflow\":\"branch-per-issue\"},{\"name\":\"api\",\"labelFilter\":\"team:api\",\"workflow\":\"direct\"}]",
              "activeSubsquadName": "ui",
              "activeSubsquadSource": "file"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("subsquads_listed"));
            Assert.That(evt.RequestId, Is.EqualTo("req-abc-001"));
            Assert.That(evt.SubSquadsConfigured, Is.True);
            Assert.That(evt.SubSquadsCount, Is.EqualTo(2));
            Assert.That(evt.WorkstreamsJson, Does.Contain("\"name\":\"ui\""));
            Assert.That(evt.ActiveSubsquadName, Is.EqualTo("ui"));
            Assert.That(evt.ActiveSubsquadSource, Is.EqualTo("file"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSubSquadsListedPayload_NotConfigured() {
        const string json = """
            {
              "type": "subsquads_listed",
              "requestId": "req-abc-002",
              "subsquadsConfigured": false,
              "subsquadsCount": 0,
              "workstreamsJson": null,
              "activeSubsquadName": null,
              "activeSubsquadSource": null
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("subsquads_listed"));
            Assert.That(evt.SubSquadsConfigured, Is.False);
            Assert.That(evt.SubSquadsCount, Is.EqualTo(0));
            Assert.That(evt.WorkstreamsJson, Is.Null);
            Assert.That(evt.ActiveSubsquadName, Is.Null);
            Assert.That(evt.ActiveSubsquadSource, Is.Null);
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSubSquadsActivatedPayload() {
        const string json = """
            {
              "type": "subsquads_activated",
              "requestId": "req-abc-003",
              "subSquadName": "api-team"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("subsquads_activated"));
            Assert.That(evt.RequestId, Is.EqualTo("req-abc-003"));
            Assert.That(evt.SubSquadName, Is.EqualTo("api-team"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSubSquadsErrorPayload() {
        const string json = """
            {
              "type": "subsquads_error",
              "requestId": "req-abc-004",
              "message": "Cannot read .squad/workstreams.json: permission denied"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("subsquads_error"));
            Assert.That(evt.RequestId, Is.EqualTo("req-abc-004"));
            Assert.That(evt.Message, Does.Contain("permission denied"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesSubSquadsListedPayload_EnvSourceActive() {
        const string json = """
            {
              "type": "subsquads_listed",
              "subsquadsConfigured": true,
              "subsquadsCount": 1,
              "workstreamsJson": "[{\"name\":\"backend\",\"labelFilter\":\"team:backend\",\"workflow\":\"branch-per-issue\"}]",
              "activeSubsquadName": "backend",
              "activeSubsquadSource": "env"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.SubSquadsConfigured, Is.True);
            Assert.That(evt.SubSquadsCount, Is.EqualTo(1));
            Assert.That(evt.ActiveSubsquadName, Is.EqualTo("backend"));
            Assert.That(evt.ActiveSubsquadSource, Is.EqualTo("env"));
        });
    }

    [Test]
    public void PersonalListRequest_SerializesCorrectly() {
        var req = new SquadSdkPersonalListRequest("req-personal-001");
        var json = JsonSerializer.Serialize(req);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"type\":\"personal_list\""));
            Assert.That(json, Does.Contain("\"requestId\":\"req-personal-001\""));
            Assert.That(json, Does.Not.Contain("\"Type\""));
            Assert.That(json, Does.Not.Contain("\"RequestId\""));
        });
    }

    [Test]
    public void PersonalInitRequest_SerializesCorrectly() {
        var req = new SquadSdkPersonalInitRequest("req-personal-002");
        var json = JsonSerializer.Serialize(req);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"type\":\"personal_init\""));
            Assert.That(json, Does.Contain("\"requestId\":\"req-personal-002\""));
            Assert.That(json, Does.Not.Contain("\"Type\""));
            Assert.That(json, Does.Not.Contain("\"RequestId\""));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesPersonalAgentsListedPayload_WithAgents() {
        const string json = """
            {
              "type": "personal_agents_listed",
              "requestId": "req-personal-003",
              "personalInitialized": true,
              "personalAgentsCount": 2,
              "personalAgentsJson": "[{\"name\":\"aria\",\"role\":\"Assistant\"},{\"name\":\"rex\",\"role\":\"Reviewer\"}]",
              "personalDir": "C:\\Users\\Mark\\AppData\\Roaming\\squad\\personal-squad"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("personal_agents_listed"));
            Assert.That(evt.RequestId, Is.EqualTo("req-personal-003"));
            Assert.That(evt.PersonalInitialized, Is.True);
            Assert.That(evt.PersonalAgentsCount, Is.EqualTo(2));
            Assert.That(evt.PersonalAgentsJson, Does.Contain("\"name\":\"aria\""));
            Assert.That(evt.PersonalDir, Does.Contain("personal-squad"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesPersonalAgentsListedPayload_NotInitialized() {
        const string json = """
            {
              "type": "personal_agents_listed",
              "requestId": "req-personal-004",
              "personalInitialized": false,
              "personalAgentsCount": 0,
              "personalAgentsJson": null,
              "personalDir": null
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("personal_agents_listed"));
            Assert.That(evt.PersonalInitialized, Is.False);
            Assert.That(evt.PersonalAgentsCount, Is.EqualTo(0));
            Assert.That(evt.PersonalAgentsJson, Is.Null);
            Assert.That(evt.PersonalDir, Is.Null);
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesPersonalInitDonePayload() {
        const string json = """
            {
              "type": "personal_init_done",
              "requestId": "req-personal-005",
              "personalDir": "C:\\Users\\Mark\\AppData\\Roaming\\squad\\personal-squad"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("personal_init_done"));
            Assert.That(evt.RequestId, Is.EqualTo("req-personal-005"));
            Assert.That(evt.PersonalDir, Does.Contain("personal-squad"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesPersonalErrorPayload() {
        const string json = """
            {
              "type": "personal_error",
              "requestId": "req-personal-006",
              "message": "SQUAD_NO_PERSONAL is set — personal squad is disabled"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("personal_error"));
            Assert.That(evt.RequestId, Is.EqualTo("req-personal-006"));
            Assert.That(evt.Message, Does.Contain("SQUAD_NO_PERSONAL"));
        });
    }

    [Test]
    public void SquadSdkEvent_DeserializesPersonalAgentsListedPayload_EmptyList() {
        const string json = """
            {
              "type": "personal_agents_listed",
              "requestId": "req-personal-007",
              "personalInitialized": true,
              "personalAgentsCount": 0,
              "personalAgentsJson": "[]",
              "personalDir": "C:\\Users\\Mark\\AppData\\Roaming\\squad\\personal-squad"
            }
            """;

        var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(evt!.Type, Is.EqualTo("personal_agents_listed"));
            Assert.That(evt.PersonalInitialized, Is.True);
            Assert.That(evt.PersonalAgentsCount, Is.EqualTo(0));
            Assert.That(evt.PersonalAgentsJson, Is.EqualTo("[]"));
            Assert.That(evt.PersonalDir, Is.Not.Null);
        });
    }
}
