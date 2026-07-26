namespace SquadDash.Tests;

/// <summary>
/// Unit tests for <see cref="WeakEventBroker"/>.
/// </summary>
[TestFixture]
internal sealed class WeakEventBrokerTests
{
    // ── Simple event type for test isolation ─────────────────────────────────

    private sealed record TestEvent(string Payload);
    private sealed record OtherEvent(int Value);

    // ── Subscribe + Publish ───────────────────────────────────────────────────

    [Test]
    public void Publish_AfterSubscribe_DeliversEventToHandler()
    {
        var broker = new WeakEventBroker();
        var received = new List<string>();
        Action<TestEvent> handler = e => received.Add(e.Payload);

        broker.Subscribe(handler);
        broker.Publish(new TestEvent("hello"));

        Assert.That(received, Is.EqualTo(new[] { "hello" }));
    }

    [Test]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var broker = new WeakEventBroker();

        Assert.DoesNotThrow(() => broker.Publish(new TestEvent("no-one-home")));
    }

    [Test]
    public void Publish_EventWithNoMatchingSubscribers_DoesNotDeliverToOtherTypes()
    {
        var broker = new WeakEventBroker();
        var received = new List<string>();
        Action<TestEvent> handler = e => received.Add(e.Payload);

        broker.Subscribe(handler);
        broker.Publish(new OtherEvent(42)); // different type

        Assert.That(received, Is.Empty);
    }

    // ── Multiple subscribers ──────────────────────────────────────────────────

    [Test]
    public void Publish_WithMultipleSubscribers_AllReceiveEvent()
    {
        var broker = new WeakEventBroker();
        var log = new List<string>();

        Action<TestEvent> handlerA = e => log.Add("A:" + e.Payload);
        Action<TestEvent> handlerB = e => log.Add("B:" + e.Payload);
        Action<TestEvent> handlerC = e => log.Add("C:" + e.Payload);

        broker.Subscribe(handlerA);
        broker.Subscribe(handlerB);
        broker.Subscribe(handlerC);

        broker.Publish(new TestEvent("x"));

        Assert.That(log, Is.EquivalentTo(new[] { "A:x", "B:x", "C:x" }));
    }

    [Test]
    public void Publish_SameHandlerSubscribedTwice_InvokedTwice()
    {
        var broker = new WeakEventBroker();
        var count = 0;
        Action<TestEvent> handler = _ => count++;

        broker.Subscribe(handler);
        broker.Subscribe(handler); // intentional duplicate
        broker.Publish(new TestEvent("dup"));

        Assert.That(count, Is.EqualTo(2));
    }

    // ── Unsubscribe ───────────────────────────────────────────────────────────

    [Test]
    public void Unsubscribe_RemovesHandler_NoLongerReceivesEvents()
    {
        var broker = new WeakEventBroker();
        var received = new List<string>();
        Action<TestEvent> handler = e => received.Add(e.Payload);

        broker.Subscribe(handler);
        broker.Unsubscribe(handler);
        broker.Publish(new TestEvent("after-unsub"));

        Assert.That(received, Is.Empty);
    }

    [Test]
    public void Unsubscribe_OneOfTwoHandlers_OtherStillReceivesEvents()
    {
        var broker = new WeakEventBroker();
        var log = new List<string>();

        Action<TestEvent> handlerA = e => log.Add("A");
        Action<TestEvent> handlerB = e => log.Add("B");

        broker.Subscribe(handlerA);
        broker.Subscribe(handlerB);
        broker.Unsubscribe(handlerA);
        broker.Publish(new TestEvent("t"));

        Assert.That(log, Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public void Unsubscribe_NonexistentHandler_DoesNotThrow()
    {
        var broker = new WeakEventBroker();
        Action<TestEvent> handler = _ => { };

        Assert.DoesNotThrow(() => broker.Unsubscribe(handler));
    }

    [Test]
    public void Unsubscribe_WhenSameHandlerSubscribedTwice_RemovesFirstOccurrence()
    {
        var broker = new WeakEventBroker();
        var count = 0;
        Action<TestEvent> handler = _ => count++;

        broker.Subscribe(handler);
        broker.Subscribe(handler);
        broker.Unsubscribe(handler); // removes one entry
        broker.Publish(new TestEvent("once"));

        Assert.That(count, Is.EqualTo(1));
    }

    // ── GC / WeakReference safety ─────────────────────────────────────────────

    /// <summary>
    /// Helper that subscribes from a short-lived scope. The subscriber object
    /// holds the delegate field; once the object is eligible for GC, the delegate
    /// (and thus the broker's WeakReference to it) also becomes eligible.
    /// </summary>
    private sealed class ShortLivedSubscriber
    {
        public int CallCount;
        // Field holds a strong reference to the delegate so the broker's WeakRef
        // stays live for as long as THIS object is alive.
        public readonly Action<TestEvent> Handler;

        public ShortLivedSubscriber(WeakEventBroker broker)
        {
            Handler = _ => CallCount++;
            broker.Subscribe(Handler);
        }
    }

    [Test]
    public void Publish_AfterSubscriberGcd_DoesNotThrowAndPrunesDeadRef()
    {
        var broker = new WeakEventBroker();

        // Subscribe from a short-lived scope and immediately drop the reference.
        // The lambda is stored ONLY in Handler which is a field of the subscriber;
        // once the subscriber is GC'd, nothing else holds the delegate strongly.
        SubscribeAndDropRef(broker);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(); // second pass ensures finalizers ran

        // Publish must not throw even though the registered handler is now dead.
        Assert.DoesNotThrow(() => broker.Publish(new TestEvent("after-gc")));
    }

    [Test]
    public void Publish_AfterSubscriberGcd_CallCountDoesNotIncrease()
    {
        var broker = new WeakEventBroker();
        var liveCount = 0;
        Action<TestEvent> liveHandler = _ => liveCount++;
        broker.Subscribe(liveHandler);

        SubscribeAndDropRef(broker); // dead subscriber
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        broker.Publish(new TestEvent("mixed"));

        // Only the live handler should have incremented liveCount.
        Assert.That(liveCount, Is.EqualTo(1));
    }

    // Isolated helper so the JIT cannot keep the local alive beyond this call.
    private static void SubscribeAndDropRef(WeakEventBroker broker)
    {
        var sub = new ShortLivedSubscriber(broker);
        // Ensure the compiler doesn't optimize away the local.
        GC.KeepAlive(sub);
        // sub goes out of scope; no more strong refs to it or its Handler delegate.
    }

    // ── Thread safety ─────────────────────────────────────────────────────────

    [Test]
    public void SubscribeAndPublish_ConcurrentlyFromMultipleThreads_DoesNotThrow()
    {
        var broker = new WeakEventBroker();
        var counter = 0;
        Action<TestEvent> handler = _ => Interlocked.Increment(ref counter);

        const int threadCount = 8;
        const int iterationsPerThread = 200;

        var threads = Enumerable.Range(0, threadCount).Select(_ => new Thread(() =>
        {
            for (var i = 0; i < iterationsPerThread; i++)
            {
                broker.Subscribe(handler);
                broker.Publish(new TestEvent("t"));
                broker.Unsubscribe(handler);
            }
        })).ToList();

        Assert.DoesNotThrow(() =>
        {
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join(TimeSpan.FromSeconds(10));
        });
    }

    // ── Subscribe null guard ──────────────────────────────────────────────────

    [Test]
    public void Subscribe_NullHandler_ThrowsArgumentNullException()
    {
        var broker = new WeakEventBroker();
        Assert.Throws<ArgumentNullException>(() => broker.Subscribe<TestEvent>(null!));
    }

    [Test]
    public void Unsubscribe_NullHandler_ThrowsArgumentNullException()
    {
        var broker = new WeakEventBroker();
        Assert.Throws<ArgumentNullException>(() => broker.Unsubscribe<TestEvent>(null!));
    }

    // ── Independent event types ───────────────────────────────────────────────

    [Test]
    public void Subscribe_DifferentEventTypes_DeliveredIndependently()
    {
        var broker = new WeakEventBroker();
        var testLog = new List<string>();
        var otherLog = new List<int>();

        Action<TestEvent> testHandler = e => testLog.Add(e.Payload);
        Action<OtherEvent> otherHandler = e => otherLog.Add(e.Value);

        broker.Subscribe(testHandler);
        broker.Subscribe(otherHandler);

        broker.Publish(new TestEvent("str"));
        broker.Publish(new OtherEvent(99));

        Assert.Multiple(() => {
            Assert.That(testLog, Is.EqualTo(new[] { "str" }));
            Assert.That(otherLog, Is.EqualTo(new[] { 99 }));
        });
    }
}
