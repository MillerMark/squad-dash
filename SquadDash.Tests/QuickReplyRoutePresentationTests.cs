namespace SquadDash.Tests;

[TestFixture]
internal sealed class QuickReplyRoutePresentationTests {
    [Test]
    public void BuildCaption_ReturnsCoordinatorCaption_WhenAllOptionsStayWithCoordinator() {
        var caption = QuickReplyRoutePresentation.BuildCaption([
            new QuickReplyRoutePresentation.RouteInfo("start_coordinator", null, null),
            new QuickReplyRoutePresentation.RouteInfo("done", null, null)
        ]);

        Assert.That(caption, Is.EqualTo("Next step will stay with the Coordinator."));
    }

    [Test]
    public void BuildCaption_ReturnsAgentCaption_WhenAllOptionsContinueWithSameAgent() {
        var caption = QuickReplyRoutePresentation.BuildCaption([
            new QuickReplyRoutePresentation.RouteInfo("continue_current_agent", "Lyra Morn", null),
            new QuickReplyRoutePresentation.RouteInfo("continue_current_agent", "Lyra Morn", null)
        ]);

        Assert.That(caption, Is.EqualTo("Next step will continue with Lyra Morn."));
    }

    [Test]
    public void BuildCaption_ReturnsNamedAgentCaption_WhenAllOptionsStartWithSameAgent() {
        var caption = QuickReplyRoutePresentation.BuildCaption([
            new QuickReplyRoutePresentation.RouteInfo("start_named_agent", "Orion Vale", null),
            new QuickReplyRoutePresentation.RouteInfo("start_named_agent", "Orion Vale", null)
        ]);

        Assert.That(caption, Is.EqualTo("Next step will go to Orion Vale."));
    }

    [Test]
    public void BuildCaption_ReturnsNull_ForMixedRoutes() {
        var caption = QuickReplyRoutePresentation.BuildCaption([
            new QuickReplyRoutePresentation.RouteInfo("continue_current_agent", "Lyra Morn", null),
            new QuickReplyRoutePresentation.RouteInfo("start_coordinator", null, null)
        ]);

        Assert.That(caption, Is.Null);
    }

    [Test]
    public void BuildCaption_ReturnsNull_ForMixedAgentRouteModes_InsteadOfThrowing() {
        var caption = QuickReplyRoutePresentation.BuildCaption([
            new QuickReplyRoutePresentation.RouteInfo("continue_current_agent", "Mira Quill", null),
            new QuickReplyRoutePresentation.RouteInfo("start_named_agent", "Mira Quill", null)
        ]);

        Assert.That(caption, Is.Null);
    }

    [Test]
    public void BuildButtonToolTip_ReturnsCoordinatorToolTip_WhenNoAgentLabelExists() {
        var toolTip = QuickReplyRoutePresentation.BuildButtonToolTip(
            new QuickReplyRoutePresentation.RouteInfo("start_coordinator", null, null));

        Assert.That(toolTip, Is.EqualTo("Handled by Coordinator"));
    }

    [Test]
    public void BuildButtonToolTip_ReturnsAgentToolTip_WhenAgentLabelExists() {
        var toolTip = QuickReplyRoutePresentation.BuildButtonToolTip(
            new QuickReplyRoutePresentation.RouteInfo("continue_current_agent", "Lyra Morn", null));

        Assert.That(toolTip, Is.EqualTo("Continue with Lyra Morn"));
    }

    [Test]
    public void BuildButtonToolTip_IgnoresReason_WhenProvided() {
        var toolTip = QuickReplyRoutePresentation.BuildButtonToolTip(
            new QuickReplyRoutePresentation.RouteInfo(
                "start_named_agent",
                "Orion Vale",
                "Architectural backlog ownership belongs to Orion Vale."));

        Assert.That(toolTip, Is.EqualTo("Start with Orion Vale"));
    }
}
