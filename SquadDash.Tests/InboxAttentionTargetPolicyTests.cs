namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxAttentionTargetPolicyTests
{
    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void ShouldUseInboxPanel_HonorsLiveAndPersistedVisibility(
        bool panelVisible,
        bool persistedPanelVisible,
        bool expected)
    {
        Assert.That(
            InboxAttentionTargetPolicy.ShouldUseInboxPanel(panelVisible, persistedPanelVisible),
            Is.EqualTo(expected));
    }

    [TestCase(false, false, InboxAttentionTargetKind.Menu)]
    [TestCase(false, true, InboxAttentionTargetKind.Menu)]
    [TestCase(true, false, InboxAttentionTargetKind.FilteredPanel)]
    [TestCase(true, true, InboxAttentionTargetKind.MessageRow)]
    public void Select_UsesMenuOnlyWhenPanelIsHiddenAndPreservesFilterTruth(
        bool panelVisible,
        bool messageRowVisible,
        InboxAttentionTargetKind expected)
    {
        Assert.That(
            InboxAttentionTargetPolicy.Select(panelVisible, messageRowVisible),
            Is.EqualTo(expected));
    }
}
