namespace SquadDash.Tests;

[TestFixture]
public sealed class AgentNameHumanizerTests {
    [TestCase("R2D2", "R2D2")]
    [TestCase("R2-D2", "R2-D2")]
    [TestCase("API", "API")]
    [TestCase("HanSolo", "Han Solo")]
    [TestCase("MaceWindu", "Mace Windu")]
    [TestCase("LukeSkywalker", "Luke Skywalker")]
    [TestCase("AhsokaTano", "Ahsoka Tano")]
    [TestCase("han-solo", "han solo")]
    [TestCase("han_solo", "han solo")]
    [TestCase("r2d2-intellisense-logic", "r2d2 intellisense logic")]
    [TestCase("McGregor", "McGregor")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase("Squad", "Squad")]
    [TestCase("general-purpose", "general purpose")]
    [TestCase("Lyra Morn", "Lyra Morn")]
    [TestCase("Arjun Sen", "Arjun Sen")]
    [TestCase("Jae Min Kade", "Jae Min Kade")]
    [TestCase("John Doe", "John Doe")]
    public void Humanize_ReturnsExpectedResult(string input, string expected) {
        var result = AgentNameHumanizer.Humanize(input);

        Assert.That(result, Is.EqualTo(expected));
    }
}
