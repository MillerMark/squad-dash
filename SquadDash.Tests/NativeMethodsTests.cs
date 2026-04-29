namespace SquadDash.Tests;

[TestFixture]
internal sealed class NativeMethodsTests
{
    [Test]
    public void ComputeMaximizedWorkAreaBounds_PrimaryMonitor_UsesWorkAreaSize()
    {
        var bounds = NativeMethods.ComputeMaximizedWorkAreaBounds(
            monitorLeft: 0,
            monitorTop: 0,
            workLeft: 0,
            workTop: 0,
            workRight: 1920,
            workBottom: 1040);

        Assert.That(bounds, Is.EqualTo(new NativeMethods.MaximizedWorkAreaBounds(0, 0, 1920, 1040)));
    }

    [Test]
    public void ComputeMaximizedWorkAreaBounds_SecondaryMonitorRightOfPrimary_UsesMonitorRelativePosition()
    {
        var bounds = NativeMethods.ComputeMaximizedWorkAreaBounds(
            monitorLeft: 1920,
            monitorTop: 0,
            workLeft: 1920,
            workTop: 40,
            workRight: 4480,
            workBottom: 1440);

        Assert.That(bounds, Is.EqualTo(new NativeMethods.MaximizedWorkAreaBounds(0, 40, 2560, 1400)));
    }

    [Test]
    public void ComputeMaximizedWorkAreaBounds_SecondaryMonitorLeftOfPrimary_UsesMonitorRelativePosition()
    {
        var bounds = NativeMethods.ComputeMaximizedWorkAreaBounds(
            monitorLeft: -2560,
            monitorTop: 0,
            workLeft: -2560,
            workTop: 0,
            workRight: -40,
            workBottom: 1440);

        Assert.That(bounds, Is.EqualTo(new NativeMethods.MaximizedWorkAreaBounds(0, 0, 2520, 1440)));
    }

    [Test]
    public void ComputeMaximizedWorkAreaBounds_TaskbarOnLeft_OffsetsFromMonitorOrigin()
    {
        var bounds = NativeMethods.ComputeMaximizedWorkAreaBounds(
            monitorLeft: 1920,
            monitorTop: 0,
            workLeft: 1960,
            workTop: 0,
            workRight: 4480,
            workBottom: 1440);

        Assert.That(bounds, Is.EqualTo(new NativeMethods.MaximizedWorkAreaBounds(40, 0, 2520, 1440)));
    }
}
