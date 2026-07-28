using NUnit.Framework;
using SquadDash;

namespace SquadDash.Tests;

/// <summary>
/// Tests for ReadAgentSatelliteCoalescer.
///
/// NOTE: FindActiveEntry is not covered here because ToolTranscriptEntry requires
/// WPF UI controls (Expander, TextBlock, Button, etc.) that cannot be instantiated
/// in a headless test environment. Only TryExtractAgentId — which has no WPF
/// dependency — is tested below.
/// </summary>
[TestFixture]
internal sealed class AgentPollCoalescingTests
{
    // ── TryExtractAgentId ─────────────────────────────────────────────────────

    [Test]
    public void TryExtractAgentId_NullJson_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(null);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_EmptyJson_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(string.Empty);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_WhitespaceJson_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId("   ");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_MalformedJson_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId("not-valid-json{{{");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_MissingField_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(@"{""wait"":true,""timeout"":30}");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_ValidJson_ReturnsAgentId()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(@"{""agent_id"":""my-agent-abc"",""wait"":true}");
        Assert.That(result, Is.EqualTo("my-agent-abc"));
    }

    [Test]
    public void TryExtractAgentId_AgentIdIsNull_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(@"{""agent_id"":null}");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_AgentIdIsNumber_ReturnsNull()
    {
        // agent_id must be a string; a number should not match
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(@"{""agent_id"":42}");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_NonObjectJson_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(@"""just-a-string""");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryExtractAgentId_ArrayJson_ReturnsNull()
    {
        var result = ReadAgentSatelliteCoalescer.TryExtractAgentId(@"[""agent_id"",""foo""]");
        Assert.That(result, Is.Null);
    }
}
