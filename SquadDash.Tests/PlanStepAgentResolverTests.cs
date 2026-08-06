using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStepAgentResolverTests
{
    // ── Sample routing.md content ────────────────────────────────────────────────

    private const string SampleRoutingMd =
        "# Work Routing\n\n" +
        "## Routing Table\n\n" +
        "| Work Type | Route To | Examples |\n" +
        "|-----------|----------|----------|\n" +
        "| WPF/XAML UI & user experience | Lyra Morn | MainWindow, dialog controls, data binding, animations |\n" +
        "| C# backend services & persistence | Arjun Sen | Store.cs, persistence, thread safety |\n" +
        "| Testing & quality | Vesper Knox | NUnit tests, coverage, test quality |\n" +
        "| TypeScript/SDK bridge | Talia Rune | Squad.SDK, NDJSON, npm |\n";

    // ── Sample team.md content ───────────────────────────────────────────────────

    private const string SampleTeamMd =
        "# Squad Team\n\n" +
        "## Members\n\n" +
        "| Name | Role | Charter | Status |\n" +
        "|------|------|---------|--------|\n" +
        "| Lyra Morn | WPF & UI Specialist | agents/lyra-morn/charter.md | active |\n" +
        "| Arjun Sen | C# Backend Services Specialist | agents/arjun-sen/charter.md | active |\n" +
        "| Vesper Knox | Testing & Quality Specialist | agents/vesper-knox/charter.md | active |\n" +
        "| Talia Rune | TypeScript & SDK Bridge Specialist | agents/talia-rune/charter.md | active |\n" +
        "| Argus Weld | Continuous Improvement | agents/argus-weld/charter.md | 🌙 Background |\n";

    // ── Parsing tests ────────────────────────────────────────────────────────────

    [Test]
    public void ParseRoutingMd_ValidTable_ExtractsRules()
    {
        var rules = PlanStepAgentResolver.ParseRoutingMd(SampleRoutingMd);

        Assert.That(rules, Has.Count.EqualTo(4));

        var lyra = rules[0];
        Assert.That(lyra.WorkType,  Is.EqualTo("WPF/XAML UI & user experience"));
        Assert.That(lyra.AgentName, Is.EqualTo("Lyra Morn"));
        Assert.That(lyra.Keywords,  Does.Contain("MainWindow"));

        var vesper = rules[2];
        Assert.That(vesper.AgentName, Is.EqualTo("Vesper Knox"));
        Assert.That(vesper.Keywords,  Does.Contain("NUnit tests"));
    }

    [Test]
    public void ParseRoutingMd_EmptyContent_ReturnsEmpty()
    {
        var rules = PlanStepAgentResolver.ParseRoutingMd(string.Empty);
        Assert.That(rules, Is.Empty);
    }

    [Test]
    public void ParseRoutingMd_MalformedTable_ReturnsEmpty()
    {
        var rules = PlanStepAgentResolver.ParseRoutingMd("not a table at all\njust plain text\n");
        Assert.That(rules, Is.Empty);
    }

    [Test]
    public void ParseTeamMd_ValidTable_ExtractsActiveAgents()
    {
        var agents = PlanStepAgentResolver.ParseTeamMd(SampleTeamMd);

        // Argus Weld has Background status → excluded
        Assert.That(agents.Count(a => a.IsActive), Is.EqualTo(4));

        var lyra = agents[0];
        Assert.That(lyra.Name,   Is.EqualTo("Lyra Morn"));
        Assert.That(lyra.Handle, Is.EqualTo("lyra-morn"));
        Assert.That(lyra.IsActive, Is.True);
    }

    [Test]
    public void ParseTeamMd_ProseCharterColumn_UsesNameHandleAndCanonicalCharterFallback()
    {
        const string team = """
            ## Members
            | Name | Role | Charter | Status |
            |---|---|---|---|
            | Arjun Sen | Backend Design | Domain modeling, API design, backend implementation | Active |
            """;

        var agent = PlanStepAgentResolver.ParseTeamMd(team).Single();

        Assert.Multiple(() =>
        {
            Assert.That(agent.Handle, Is.EqualTo("arjun-sen"));
            Assert.That(agent.CharterPath, Is.Null);
            Assert.That(agent.IsActive, Is.True);
        });
    }

    [Test]
    public void ParseTeamMd_InactiveAgent_Excluded()
    {
        var agents = PlanStepAgentResolver.ParseTeamMd(SampleTeamMd);

        var argus = agents.FirstOrDefault(a => a.Name == "Argus Weld");
        Assert.That(argus, Is.Not.Null);
        Assert.That(argus!.IsActive, Is.False);
    }

    // ── Resolution tests ─────────────────────────────────────────────────────────

    private static (IReadOnlyList<RoutingRule> rules, IReadOnlyList<RosterAgent> agents) BuildTestData()
    {
        var rules  = PlanStepAgentResolver.ParseRoutingMd(SampleRoutingMd);
        var agents = PlanStepAgentResolver.ParseTeamMd(SampleTeamMd)
                         .Where(a => a.IsActive)
                         .ToList();
        return (rules, agents);
    }

    [Test]
    public void Resolve_WpfKeywords_RoutesToLyraMorn()
    {
        var (rules, agents) = BuildTestData();
        var resolver = new PlanStepAgentResolver(rules, agents);

        var result = resolver.Resolve(
            "Fix WPF binding",
            "Update data binding in MainWindow to support dark mode");

        Assert.That(result.IsGenericFallback, Is.False);
        Assert.That(result.AgentName,   Is.EqualTo("Lyra Morn"));
        Assert.That(result.AgentHandle, Is.EqualTo("lyra-morn"));
    }

    [Test]
    public void Resolve_TestingKeywords_RoutesToVesperKnox()
    {
        var (rules, agents) = BuildTestData();
        var resolver = new PlanStepAgentResolver(rules, agents);

        var result = resolver.Resolve(
            "Add NUnit coverage",
            "Add NUnit test coverage for the authentication service");

        Assert.That(result.IsGenericFallback, Is.False);
        Assert.That(result.AgentName,   Is.EqualTo("Vesper Knox"));
        Assert.That(result.AgentHandle, Is.EqualTo("vesper-knox"));
    }

    [Test]
    public void Resolve_BackendKeywords_RoutesToArjunSen()
    {
        var (rules, agents) = BuildTestData();
        var resolver = new PlanStepAgentResolver(rules, agents);

        var result = resolver.Resolve(
            "Fix thread safety bug",
            "Fix thread safety issue in persistence layer of Store.cs");

        Assert.That(result.IsGenericFallback, Is.False);
        Assert.That(result.AgentName,   Is.EqualTo("Arjun Sen"));
        Assert.That(result.AgentHandle, Is.EqualTo("arjun-sen"));
    }

    [Test]
    public void Resolve_NoMatch_ReturnsFallback()
    {
        var (rules, agents) = BuildTestData();
        var resolver = new PlanStepAgentResolver(rules, agents);

        var result = resolver.Resolve(
            "Update README",
            "Correct a typo in the project readme file documentation");

        Assert.That(result.IsGenericFallback, Is.True);
        Assert.That(result.AgentName, Is.Null);
        Assert.That(result.FallbackReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Resolve_MatchedAgentInactive_ReturnsFallback()
    {
        var rules = PlanStepAgentResolver.ParseRoutingMd(SampleRoutingMd);
        // Roster has no active agents
        var inactiveOnly = PlanStepAgentResolver.ParseTeamMd(SampleTeamMd)
            .Select(a => a with { IsActive = false })
            .ToList();

        var resolver = new PlanStepAgentResolver(rules, inactiveOnly);

        var result = resolver.Resolve(
            "Fix WPF binding in MainWindow",
            "data binding issue");

        Assert.That(result.IsGenericFallback, Is.True);
    }

    [Test]
    public void Resolve_EmptyRules_ReturnsFallback()
    {
        var agents   = PlanStepAgentResolver.ParseTeamMd(SampleTeamMd).Where(a => a.IsActive).ToList();
        var resolver = new PlanStepAgentResolver([], agents);

        var result = resolver.Resolve("Fix MainWindow dialog", "data binding in WPF");

        Assert.That(result.IsGenericFallback, Is.True);
    }

    [Test]
    public void Resolve_EmptyRoster_ReturnsFallback()
    {
        var rules    = PlanStepAgentResolver.ParseRoutingMd(SampleRoutingMd);
        var resolver = new PlanStepAgentResolver(rules, []);

        var result = resolver.Resolve("Fix MainWindow dialog", "data binding in WPF");

        Assert.That(result.IsGenericFallback, Is.True);
    }
}
