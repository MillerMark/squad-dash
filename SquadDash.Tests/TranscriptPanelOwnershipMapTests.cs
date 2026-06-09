namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="TranscriptPanelOwnershipMap"/>.
///
/// These tests pin the correct ownership-resolution semantics that hover-glow
/// handlers must rely on.  They were written to expose the existing bug where
/// the main-window code re-derived ownership from <c>IsTranscriptTargetSelected</c>
/// at interaction time; that property is <c>true</c> for BOTH the main-panel agent
/// and every secondary-panel agent, so <c>FirstOrDefault</c> could return the wrong
/// card and produce cross-wired glows.
/// </summary>
[TestFixture]
internal sealed class TranscriptPanelOwnershipMapTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static AgentStatusCard MakeCard(string name) =>
        new(name, name[0].ToString(), "Role", "", "", "", "#AABBCC",
            accentStorageKey: name);

    // ── Initial state ─────────────────────────────────────────────────────────

    [Test]
    public void InitialState_MainPanelOwner_IsNull()
    {
        var map = new TranscriptPanelOwnershipMap();
        Assert.That(map.MainPanelOwner, Is.Null);
    }

    [Test]
    public void InitialState_GetOwnerForPanel_ReturnsNull()
    {
        var map = new TranscriptPanelOwnershipMap();
        var token = new object();
        Assert.That(map.GetOwnerForPanel(token), Is.Null);
    }

    [Test]
    public void InitialState_GetSecondaryPanelForAgent_ReturnsNull()
    {
        var map = new TranscriptPanelOwnershipMap();
        var card = MakeCard("Argus");
        Assert.That(map.GetSecondaryPanelForAgent(card), Is.Null);
    }

    [Test]
    public void InitialState_HasSecondaryPanel_ReturnsFalse()
    {
        var map = new TranscriptPanelOwnershipMap();
        var card = MakeCard("Argus");
        Assert.That(map.HasSecondaryPanel(card), Is.False);
    }

    // ── Main panel ownership ──────────────────────────────────────────────────

    [Test]
    public void SetMainPanelOwner_StoresOwner()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");

        map.SetMainPanelOwner(argus);

        Assert.That(map.MainPanelOwner, Is.SameAs(argus));
    }

    [Test]
    public void SetMainPanelOwner_Null_ClearsOwner()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        map.SetMainPanelOwner(argus);

        map.SetMainPanelOwner(null);

        Assert.That(map.MainPanelOwner, Is.Null);
    }

    [Test]
    public void IsMainPanelOwner_True_ForCurrentOwner()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        map.SetMainPanelOwner(argus);

        Assert.That(map.IsMainPanelOwner(argus), Is.True);
    }

    [Test]
    public void IsMainPanelOwner_False_ForDifferentAgent()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        map.SetMainPanelOwner(argus);

        // Lyra is NOT in the main panel — must not claim ownership even if she
        // happens to have IsTranscriptTargetSelected=true (secondary-panel case).
        Assert.That(map.IsMainPanelOwner(lyra), Is.False);
    }

    [Test]
    public void IsMainPanelOwner_False_WhenNoOwnerSet()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");

        Assert.That(map.IsMainPanelOwner(argus), Is.False);
    }

    [Test]
    public void ReplaceMainPanelOwner_OldOwnerNoLongerMain()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        map.SetMainPanelOwner(argus);

        map.SetMainPanelOwner(lyra);

        Assert.That(map.IsMainPanelOwner(argus), Is.False);
        Assert.That(map.IsMainPanelOwner(lyra), Is.True);
    }

    // ── Secondary panel registration ──────────────────────────────────────────

    [Test]
    public void RegisterSecondaryPanel_GetOwnerForPanel_RoundTrip()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var panelToken = new object();

        map.RegisterSecondaryPanel(panelToken, lyra);

        Assert.That(map.GetOwnerForPanel(panelToken), Is.SameAs(lyra));
    }

    [Test]
    public void GetOwnerForPanel_ReturnsNull_ForUnknownToken()
    {
        var map = new TranscriptPanelOwnershipMap();
        var known = new object();
        var unknown = new object();
        map.RegisterSecondaryPanel(known, MakeCard("Lyra"));

        Assert.That(map.GetOwnerForPanel(unknown), Is.Null);
    }

    [Test]
    public void GetSecondaryPanelForAgent_ReturnsToken_WhenRegistered()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var panelToken = new object();
        map.RegisterSecondaryPanel(panelToken, lyra);

        Assert.That(map.GetSecondaryPanelForAgent(lyra), Is.SameAs(panelToken));
    }

    [Test]
    public void GetSecondaryPanelForAgent_ReturnsNull_ForUnregisteredAgent()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var argus = MakeCard("Argus");
        map.RegisterSecondaryPanel(new object(), lyra);

        Assert.That(map.GetSecondaryPanelForAgent(argus), Is.Null);
    }

    [Test]
    public void HasSecondaryPanel_True_AfterRegister()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        map.RegisterSecondaryPanel(new object(), lyra);

        Assert.That(map.HasSecondaryPanel(lyra), Is.True);
    }

    [Test]
    public void HasSecondaryPanel_False_ForMainPanelAgent()
    {
        // Argus owns the main panel only; no secondary panel registered for him.
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        map.SetMainPanelOwner(argus);

        Assert.That(map.HasSecondaryPanel(argus), Is.False);
    }

    [Test]
    public void UnregisterSecondaryPanel_RemovesOwnership()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var panelToken = new object();
        map.RegisterSecondaryPanel(panelToken, lyra);

        map.UnregisterSecondaryPanel(panelToken);

        Assert.That(map.GetOwnerForPanel(panelToken), Is.Null);
    }

    [Test]
    public void HasSecondaryPanel_False_AfterUnregister()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var panelToken = new object();
        map.RegisterSecondaryPanel(panelToken, lyra);

        map.UnregisterSecondaryPanel(panelToken);

        Assert.That(map.HasSecondaryPanel(lyra), Is.False);
    }

    [Test]
    public void GetSecondaryPanelForAgent_ReturnsNull_AfterUnregister()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var panelToken = new object();
        map.RegisterSecondaryPanel(panelToken, lyra);
        map.UnregisterSecondaryPanel(panelToken);

        Assert.That(map.GetSecondaryPanelForAgent(lyra), Is.Null);
    }

    [Test]
    public void RegisterSamePanelTwice_OverwritesOwner()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var argus = MakeCard("Argus");
        var panelToken = new object();
        map.RegisterSecondaryPanel(panelToken, lyra);

        map.RegisterSecondaryPanel(panelToken, argus);

        Assert.That(map.GetOwnerForPanel(panelToken), Is.SameAs(argus));
    }

    // ── Multiple secondary panels ─────────────────────────────────────────────

    [Test]
    public void MultipleSecondaryPanels_EachMappedIndependently()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var argus = MakeCard("Argus");
        var lyraPanel = new object();
        var argusPanel = new object();
        map.RegisterSecondaryPanel(lyraPanel, lyra);
        map.RegisterSecondaryPanel(argusPanel, argus);

        Assert.That(map.GetOwnerForPanel(lyraPanel), Is.SameAs(lyra));
        Assert.That(map.GetOwnerForPanel(argusPanel), Is.SameAs(argus));
        Assert.That(map.GetSecondaryPanelForAgent(lyra), Is.SameAs(lyraPanel));
        Assert.That(map.GetSecondaryPanelForAgent(argus), Is.SameAs(argusPanel));
    }

    [Test]
    public void UnregisterOne_DoesNotAffectOther()
    {
        var map = new TranscriptPanelOwnershipMap();
        var lyra = MakeCard("Lyra");
        var argus = MakeCard("Argus");
        var lyraPanel = new object();
        var argusPanel = new object();
        map.RegisterSecondaryPanel(lyraPanel, lyra);
        map.RegisterSecondaryPanel(argusPanel, argus);

        map.UnregisterSecondaryPanel(argusPanel);

        Assert.That(map.HasSecondaryPanel(lyra), Is.True);
        Assert.That(map.HasSecondaryPanel(argus), Is.False);
    }

    // ── Bug-scenario regression tests ─────────────────────────────────────────
    // These document the exact cross-wire scenarios reported: Argus in main panel,
    // Lyra in supplementary transcript panel.  Under the old lookup-at-hover-time
    // approach, both agents had IsTranscriptTargetSelected=true, and FirstOrDefault
    // could return the wrong one.  The map must never exhibit that confusion.

    [Test]
    public void BugScenario_ArgusMain_LyraSecondary_IsMainPanelOwner_Lyra_IsFalse()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        var lyraPanel = new object();

        map.SetMainPanelOwner(argus);
        map.RegisterSecondaryPanel(lyraPanel, lyra);

        // Hovering Lyra's card must NOT glow the main panel.
        Assert.That(map.IsMainPanelOwner(lyra), Is.False);
    }

    [Test]
    public void BugScenario_ArgusMain_LyraSecondary_IsMainPanelOwner_Argus_IsTrue()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        map.SetMainPanelOwner(argus);
        map.RegisterSecondaryPanel(new object(), lyra);

        // Hovering Argus's card MUST glow the main panel.
        Assert.That(map.IsMainPanelOwner(argus), Is.True);
    }

    [Test]
    public void BugScenario_ArgusMain_LyraSecondary_GetSecondaryPanel_Argus_IsNull()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        map.SetMainPanelOwner(argus);
        map.RegisterSecondaryPanel(new object(), lyra);

        // Argus has no secondary panel; hovering Argus's card must not glow a secondary border.
        Assert.That(map.GetSecondaryPanelForAgent(argus), Is.Null);
        Assert.That(map.HasSecondaryPanel(argus), Is.False);
    }

    [Test]
    public void BugScenario_ArgusMain_LyraSecondary_GetOwnerForLyraPanel_IsLyra()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        var lyraPanel = new object();
        map.SetMainPanelOwner(argus);
        map.RegisterSecondaryPanel(lyraPanel, lyra);

        // Hovering Lyra's secondary panel must resolve to Lyra (glow Lyra's card).
        Assert.That(map.GetOwnerForPanel(lyraPanel), Is.SameAs(lyra));
    }

    [Test]
    public void BugScenario_HoverMainPanel_GetOwner_ReturnsArgus()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        map.SetMainPanelOwner(argus);
        map.RegisterSecondaryPanel(new object(), lyra);

        // MainTranscriptBorder_MouseEnter must illuminate Argus's card, not Lyra's.
        Assert.That(map.MainPanelOwner, Is.SameAs(argus));
        Assert.That(map.IsMainPanelOwner(argus), Is.True);
        Assert.That(map.IsMainPanelOwner(lyra), Is.False);
    }

    [Test]
    public void BugScenario_HoverLyraSecondaryPanel_DoesNotGlowArgusCard()
    {
        var map = new TranscriptPanelOwnershipMap();
        var argus = MakeCard("Argus");
        var lyra = MakeCard("Lyra");
        var lyraPanel = new object();
        map.SetMainPanelOwner(argus);
        map.RegisterSecondaryPanel(lyraPanel, lyra);

        var owner = map.GetOwnerForPanel(lyraPanel);

        // Owner must be Lyra; Argus must not be returned.
        Assert.That(owner, Is.SameAs(lyra));
        Assert.That(owner, Is.Not.SameAs(argus));
    }
}
