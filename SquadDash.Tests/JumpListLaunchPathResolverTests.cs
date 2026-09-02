using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class JumpListLaunchPathResolverTests
{
    [Test]
    public void Resolve_SourceSlotLayout_UsesLauncherBesidePayload()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Source\squad-dash\Run\A\SquadDash.exe",
        };

        var result = JumpListLaunchPathResolver.Resolve(
            @"C:\Source\squad-dash",
            @"C:\Source\squad-dash\Run\A",
            @"C:\Source\squad-dash\Run\A\SquadDash.App.exe",
            existing.Contains);

        Assert.That(result, Is.EqualTo(@"C:\Source\squad-dash\Run\A\SquadDash.exe"));
    }

    [Test]
    public void Resolve_InstalledLayout_UsesStableRootLauncher()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\MarkM\AppData\Local\SquadDash\SquadDash.exe",
        };

        var result = JumpListLaunchPathResolver.Resolve(
            @"C:\Users\MarkM\AppData\Local\SquadDash",
            @"C:\Users\MarkM\AppData\Local\SquadDash\app",
            @"C:\Users\MarkM\AppData\Local\SquadDash\app\SquadDash.App.exe",
            existing.Contains);

        Assert.That(result, Is.EqualTo(@"C:\Users\MarkM\AppData\Local\SquadDash\SquadDash.exe"));
    }

    [Test]
    public void Resolve_NoLauncherAvailable_FallsBackToCurrentProcess()
    {
        const string processPath = @"C:\fallback\SquadDash.App.exe";

        var result = JumpListLaunchPathResolver.Resolve(
            @"C:\app-root",
            @"C:\app-root\app",
            processPath,
            _ => false);

        Assert.That(result, Is.EqualTo(processPath));
    }
}
