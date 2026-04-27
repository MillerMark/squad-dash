namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadCliCommandsTests {
    [Test]
    public void InstallLocalCli_HasCmdExeFileName() {
        Assert.That(SquadCliCommands.InstallLocalCli.FileName, Is.EqualTo("cmd.exe"));
    }

    [Test]
    public void InstallLocalCli_ArgumentsContainNpmInstall() {
        Assert.That(SquadCliCommands.InstallLocalCli.Arguments, Does.Contain("npm install"));
    }

    [Test]
    public void InstallLocalCli_HasNonEmptyDisplayName() {
        Assert.That(SquadCliCommands.InstallLocalCli.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Init_HasCmdExeFileName() {
        Assert.That(SquadCliCommands.Init.FileName, Is.EqualTo("cmd.exe"));
    }

    [Test]
    public void Init_ArgumentsContainNpxAndInit() {
        Assert.That(SquadCliCommands.Init.Arguments, Does.Contain("npx").And.Contain("init"));
    }

    [Test]
    public void Init_HasNonEmptyDisplayName() {
        Assert.That(SquadCliCommands.Init.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Doctor_HasCmdExeFileName() {
        Assert.That(SquadCliCommands.Doctor.FileName, Is.EqualTo("cmd.exe"));
    }

    [Test]
    public void Doctor_ArgumentsContainNpxAndDoctor() {
        Assert.That(SquadCliCommands.Doctor.Arguments, Does.Contain("npx").And.Contain("doctor"));
    }

    [Test]
    public void Doctor_HasNonEmptyDisplayName() {
        Assert.That(SquadCliCommands.Doctor.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void AllCommands_HaveDistinctArguments() {
        var arguments = new[] {
            SquadCliCommands.InstallLocalCli.Arguments,
            SquadCliCommands.Init.Arguments,
            SquadCliCommands.Doctor.Arguments
        };

        Assert.That(arguments, Is.Unique);
    }
}
