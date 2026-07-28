using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanIntentDetectorTests
{
    // ── Null / empty input ───────────────────────────────────────────────────

    [Test]
    public void Classify_NullInput_ReturnsNone()
    {
        Assert.That(PlanIntentDetector.Classify(null), Is.EqualTo(PlanCreationIntent.None));
    }

    [Test]
    public void Classify_EmptyInput_ReturnsNone()
    {
        Assert.That(PlanIntentDetector.Classify(string.Empty), Is.EqualTo(PlanCreationIntent.None));
    }

    [Test]
    public void Classify_WhitespaceInput_ReturnsNone()
    {
        Assert.That(PlanIntentDetector.Classify("   "), Is.EqualTo(PlanCreationIntent.None));
    }

    // ── ExplicitCreate ───────────────────────────────────────────────────────

    [TestCase("create a plan for the auth migration")]
    [TestCase("draft a plan")]
    [TestCase("devise a plan for the refactor")]
    [TestCase("prepare a plan for Q3 features")]
    [TestCase("make a plan for the deployment")]
    [TestCase("write me a plan")]
    [TestCase("design a plan for the new API")]
    [TestCase("propose a plan for the restructure")]
    [TestCase("outline a plan")]
    [TestCase("generate a plan for extracting these components")]
    [TestCase("formulate a plan for the sprint")]
    [TestCase("produce a plan")]
    [TestCase("Can you create a plan for splitting up this class?")]
    [TestCase("Please draft me a plan for the authentication refactor.")]
    public void Classify_ExplicitCreationVerb_ReturnsExplicitCreate(string prompt)
    {
        Assert.That(PlanIntentDetector.Classify(prompt), Is.EqualTo(PlanCreationIntent.ExplicitCreate));
    }

    [TestCase("plan out the database migration")]
    [TestCase("Let's plan out our next sprint")]
    [TestCase("can you plan out the steps for me")]
    public void Classify_PlanOut_ReturnsExplicitCreate(string prompt)
    {
        Assert.That(PlanIntentDetector.Classify(prompt), Is.EqualTo(PlanCreationIntent.ExplicitCreate));
    }

    // ── PlanAndImplement ─────────────────────────────────────────────────────

    [TestCase("create a plan and implement it")]
    [TestCase("draft a plan then execute it")]
    [TestCase("design a plan and build it")]
    [TestCase("outline a plan and then deploy")]
    public void Classify_PlanAndImplementVerbs_ReturnsPlanAndImplement(string prompt)
    {
        Assert.That(PlanIntentDetector.Classify(prompt), Is.EqualTo(PlanCreationIntent.PlanAndImplement));
    }

    // ── Discussion ───────────────────────────────────────────────────────────

    [TestCase("I plan to implement the new payment flow next week")]
    [TestCase("my plan is to extract the service class first")]
    [TestCase("my plan was to start with tests")]
    [TestCase("what's the plan for this sprint?")]
    [TestCase("what is the plan?")]
    [TestCase("do you have a plan for this?")]
    [TestCase("the plan is to refactor before shipping")]
    [TestCase("our plan is to go with option A")]
    [TestCase("their plan was to rewrite it")]
    [TestCase("we have plan A and plan B")]
    [TestCase("plan b seems better")]
    public void Classify_DiscussionOnlyPatterns_ReturnsDiscussion(string prompt)
    {
        Assert.That(PlanIntentDetector.Classify(prompt), Is.EqualTo(PlanCreationIntent.Discussion));
    }

    // ── None ─────────────────────────────────────────────────────────────────

    [TestCase("can you implement the auth service?")]
    [TestCase("fix the null reference in UserService")]
    [TestCase("add unit tests for the parser")]
    [TestCase("how does the routing work?")]
    public void Classify_UnrelatedPrompts_ReturnsNone(string prompt)
    {
        Assert.That(PlanIntentDetector.Classify(prompt), Is.EqualTo(PlanCreationIntent.None));
    }

    // ── IsExplicitPlanRequest helper ─────────────────────────────────────────

    [TestCase("create a plan for the migration", true)]
    [TestCase("plan out the refactor", true)]
    [TestCase("create a plan and implement it", true)]
    [TestCase("I plan to do X", false)]
    [TestCase("what's the plan?", false)]
    [TestCase("fix the bug", false)]
    [TestCase(null, false)]
    public void IsExplicitPlanRequest_CorrectForAllIntents(string? prompt, bool expected)
    {
        Assert.That(PlanIntentDetector.IsExplicitPlanRequest(prompt), Is.EqualTo(expected));
    }

    // ── Case insensitivity ───────────────────────────────────────────────────

    [TestCase("CREATE A PLAN FOR REFACTORING")]
    [TestCase("Draft A Plan")]
    [TestCase("PLAN OUT THE DEPLOYMENT")]
    public void Classify_UpperCase_StillReturnsExplicitCreate(string prompt)
    {
        Assert.That(PlanIntentDetector.Classify(prompt), Is.EqualTo(PlanCreationIntent.ExplicitCreate));
    }
}
