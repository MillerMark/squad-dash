using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ButtonPlaceholdersTests {
    [Test]
    public void Resolve_KnownTokens_AreLiteralCaseSensitiveAndSinglePass() {
        const string text = "{workspaceRoot}|{workspaceName}|{branch}|{workspaceRoot}";
        var result = ButtonPlaceholders.Resolve(text, @"C:\{branch}\$1", "{workspaceRoot}", "feature/{workspaceName}");
        Assert.That(result, Is.EqualTo(
            @"C:\{branch}\$1|{workspaceRoot}|feature/{workspaceName}|C:\{branch}\$1"));
    }

    [Test]
    public void Resolve_PreservesScriptBlocksUnknownTokensAndDifferentCasing() {
        const string text = "if ($true) { Write-Output '{branch}' }\n" +
            "{\"json\": {\"value\": 1}} {unknown} {Branch} {WorkspaceRoot} {WORKSPACENAME}";
        Assert.That(ButtonPlaceholders.Resolve(text, @"C:\repo", "repo", "main"),
            Is.EqualTo("if ($true) { Write-Output 'main' }\n" +
                "{\"json\": {\"value\": 1}} {unknown} {Branch} {WorkspaceRoot} {WORKSPACENAME}"));
    }

    [Test]
    public void Resolve_DoesNotAddShellQuotingOrInterpretReplacementMetacharacters() {
        Assert.That(ButtonPlaceholders.Resolve("{workspaceRoot}|{workspaceName}|{branch}",
            @"C:\O'Brien $& %TEMP%", "name \"quoted\"", "$1$$"),
            Is.EqualTo("C:\\O'Brien $& %TEMP%|name \"quoted\"|$1$$"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Resolve_MissingBranch_IsRejected(string? branch) {
        Assert.That(() => ButtonPlaceholders.Resolve("echo no tokens", @"C:\repo", "repo", branch!),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase("{", "{")]
    [TestCase("}", "}")]
    [TestCase("{{", "{{")]
    [TestCase("}}", "}}")]
    [TestCase("Write-Output '{' '}'", "Write-Output '{' '}'")]
    [TestCase("{ if ($true) { echo '{branch}' }}", "{ if ($true) { echo 'main' }}")]
    public void Resolve_LiteralBracesNeedNoEscapeAndDoNotChangeScriptSyntax(string text, string expected) {
        Assert.That(ButtonPlaceholders.Resolve(text, @"C:\repo", "repo", "main"), Is.EqualTo(expected));
    }
}
