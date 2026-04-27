namespace SquadDash.Tests;

[TestFixture]
internal sealed class StartupFolderParserTests {
    [TestCase(new[] { @"C:\Users\Mark\source\repos\WpfCalc" }, @"C:\Users\Mark\source\repos\WpfCalc")]
    [TestCase(new[] { "\"C:\\Users\\Mark\\source\\repos\\WpfCalc\"" }, @"C:\Users\Mark\source\repos\WpfCalc")]
    [TestCase(new[] { "C:\\Users\\Mark\\source\\repos\\WpfCalc\"" }, @"C:\Users\Mark\source\repos\WpfCalc")]
    [TestCase(new[] { "--folder", "\"C:\\Users\\Mark\\source\\repos\\WpfCalc\"" }, @"C:\Users\Mark\source\repos\WpfCalc")]
    [TestCase(new[] { "-f", "C:\\Users\\Mark\\source\\repos\\WpfCalc\"" }, @"C:\Users\Mark\source\repos\WpfCalc")]
    [TestCase(new[] { "--workspace", "\"C:\\Users\\Mark\\source\\repos\\WpfCalc\"" }, @"C:\Users\Mark\source\repos\WpfCalc")]
    public void Parse_NormalizesQuotedFolderArguments(string[] args, string expected) {
        var result = StartupFolderParser.Parse(args);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("\"\"")]
    [TestCase("   ")]
    public void Normalize_ReturnsNullForBlankValues(string value) {
        var result = StartupFolderParser.Normalize(value);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseArguments_ParsesApplicationRootAndWorkspace() {
        var result = StartupFolderParser.ParseArguments([
            "--app-root",
            "\"D:\\Drive\\Source\\SquadUI\"",
            "--workspace",
            "\"C:\\Users\\Mark\\source\\repos\\WpfCalc\""
        ]);

        Assert.Multiple(() => {
            Assert.That(result.ApplicationRoot, Is.EqualTo(@"D:\Drive\Source\SquadUI"));
            Assert.That(result.StartupFolder, Is.EqualTo(@"C:\Users\Mark\source\repos\WpfCalc"));
        });
    }
}
