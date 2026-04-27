namespace SquadDash.Tests;

[TestFixture]
internal sealed class MutexLeaseTests {
    [Test]
    public void TryAcquire_WithValidName_ReturnsTrue() {
        var name = UniqueMutexName();

        var acquired = MutexLease.TryAcquire(name, out var lease);
        using (lease) {
            Assert.That(acquired, Is.True);
            Assert.That(lease, Is.Not.Null);
        }
    }

    [Test]
    public void TryAcquire_WhenAlreadyHeld_ReturnsFalse() {
        var name = UniqueMutexName();

        var acquired1 = MutexLease.TryAcquire(name, out var lease1);
        using (lease1) {
            Assert.That(acquired1, Is.True);

            var acquired2 = MutexLease.TryAcquire(name, out var lease2);

            Assert.That(acquired2, Is.False);
            Assert.That(lease2, Is.Null);
        }
    }

    [Test]
    public void TryAcquire_AfterDispose_CanBeAcquiredAgain() {
        var name = UniqueMutexName();

        MutexLease.TryAcquire(name, out var lease1);
        lease1!.Dispose();

        var acquired = MutexLease.TryAcquire(name, out var lease2);
        using (lease2) {
            Assert.That(acquired, Is.True);
        }
    }

    [Test]
    public void TryAcquire_WithEmptyName_ThrowsArgumentException() {
        Assert.Throws<ArgumentException>(() => MutexLease.TryAcquire("", out _));
    }

    [Test]
    public void TryAcquire_WithWhitespaceName_ThrowsArgumentException() {
        Assert.Throws<ArgumentException>(() => MutexLease.TryAcquire("   ", out _));
    }

    [Test]
    public void TryAcquire_WithNegativeTimeout_ThrowsArgumentOutOfRangeException() {
        var name = UniqueMutexName();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MutexLease.TryAcquire(name, TimeSpan.FromMilliseconds(-2), out _));
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimesWithoutError() {
        var name = UniqueMutexName();
        MutexLease.TryAcquire(name, out var lease);

        Assert.DoesNotThrow(() => {
            lease!.Dispose();
            lease.Dispose();
        });
    }

    [Test]
    public void Acquire_WithValidName_ReturnsNonNullLease() {
        var name = UniqueMutexName();
        using var lease = MutexLease.Acquire(name);

        Assert.That(lease, Is.Not.Null);
    }

    [Test]
    public void TryAcquire_WithZeroTimeoutWhenAlreadyHeld_ReturnsFalseWithoutThrowing() {
        var name = UniqueMutexName();

        using var firstLease = MutexLease.Acquire(name);

        var acquired = MutexLease.TryAcquire(name, TimeSpan.Zero, out var lease2);
        Assert.That(acquired, Is.False);
        Assert.That(lease2, Is.Null);
    }

    private static string UniqueMutexName() =>
        $@"Local\SquadDash.Test.{Guid.NewGuid():N}";
}
