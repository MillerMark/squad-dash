using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CodeHealthRunPolicyTests {

    [Test]
    public void CanStart_AllowsManualRun_WhenIdleSchedulingIsDisabledAndConfiguredIsFalse() {
        var config = new CodeHealthMdConfig(
            Configured:    false,
            EnabledOnIdle: false);

        Assert.That(CodeHealthRunPolicy.CanStart(config, isManual: true), Is.True);
    }

    [Test]
    public void CanStart_BlocksIdleRun_WhenIdleSchedulingIsDisabledEvenIfConfiguredIsTrue() {
        var config = new CodeHealthMdConfig(
            Configured:    true,
            EnabledOnIdle: false);

        Assert.That(CodeHealthRunPolicy.CanStart(config, isManual: false), Is.False);
    }

    [Test]
    public void CanStart_AllowsIdleRun_WhenIdleSchedulingIsEnabled() {
        var config = new CodeHealthMdConfig(
            Configured:    false,
            EnabledOnIdle: true);

        Assert.That(CodeHealthRunPolicy.CanStart(config, isManual: false), Is.True);
    }
}
