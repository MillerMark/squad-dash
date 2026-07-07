namespace SquadDash.Tests;

[TestFixture]
internal sealed class UniverseSelectionPromptPolicyTests {
    [Test]
    public void ShouldPrompt_UtilityOnlyRosterAndNoTurns_ReturnsTrue() {
        var members = new[] { UtilityMember("Scribe"), UtilityMember("Ralph") };

        var result = UniverseSelectionPromptPolicy.ShouldPrompt(
            members,
            WorkspaceConversationState.Empty);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldPrompt_UtilityOnlyRosterAndOnlySessionBoundary_ReturnsTrue() {
        var members = new[] { UtilityMember("Scribe"), UtilityMember("Ralph") };
        var state = WorkspaceConversationState.Empty with {
            Turns = [BoundaryTurn()]
        };

        var result = UniverseSelectionPromptPolicy.ShouldPrompt(members, state);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldPrompt_ExistingUniverseSelectorTurn_ReturnsFalse() {
        var members = new[] { UtilityMember("Scribe"), UtilityMember("Ralph") };
        var state = WorkspaceConversationState.Empty with {
            Turns = [UniverseSelectorTurn()]
        };

        var result = UniverseSelectionPromptPolicy.ShouldPrompt(members, state);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldPrompt_ExistingInitFollowUpTurn_ReturnsFalse() {
        var members = new[] { UtilityMember("Scribe"), UtilityMember("Ralph") };
        var state = WorkspaceConversationState.Empty with {
            Turns = [
                UniverseSelectorTurn(),
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow,
                    "Create a suitable team from the Star Wars universe.",
                    string.Empty,
                    "What are you building?",
                    true,
                    Array.Empty<TranscriptToolRecord>())
            ]
        };

        var result = UniverseSelectionPromptPolicy.ShouldPrompt(members, state);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldPrompt_NonUtilityRoster_ReturnsFalse() {
        var members = new[] { UtilityMember("Scribe"), NonUtilityMember("Leia") };

        var result = UniverseSelectionPromptPolicy.ShouldPrompt(
            members,
            WorkspaceConversationState.Empty);

        Assert.That(result, Is.False);
    }

    private static SquadTeamMember UtilityMember(string name) =>
        new(name, "Utility", "Ready", null, null, null, true, name.ToLowerInvariant());

    private static SquadTeamMember NonUtilityMember(string name) =>
        new(name, "Lead", "Ready", null, null, null, false, name.ToLowerInvariant());

    private static TranscriptTurnRecord BoundaryTurn() =>
        new(
            DateTimeOffset.UtcNow,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            Array.Empty<TranscriptToolRecord>()) {
            IsSessionBoundary = true
        };

    private static TranscriptTurnRecord UniverseSelectorTurn() =>
        new(
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            string.Empty,
            string.Empty,
            "Ready to create a team? Select a universe:\n\n[SquadDash Universe] [Star Wars]",
            true,
            Array.Empty<TranscriptToolRecord>());
}
