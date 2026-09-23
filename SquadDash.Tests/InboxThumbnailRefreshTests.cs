using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxThumbnailRefreshTests
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void NotifyPlanProgressChanged_RefreshesOnlyForMatchingPlanGroup() =>
        WpfTestContext.Run(() =>
        {
            var resolverCalls = 0;
            var window = CreateWindow("PLAN-MATCH", planGroupId =>
            {
                resolverCalls++;
                return BuildGroup(planGroupId);
            });

            window.NotifyPlanProgressChanged("PLAN-OTHER");
            Assert.That(resolverCalls, Is.EqualTo(0));

            window.NotifyPlanProgressChanged("PLAN-MATCH");
            Assert.That(resolverCalls, Is.EqualTo(1));
        });

    [Test]
    public void NotifyAppearanceChanged_RequestsThumbnailRenderWhenPlanResolves() =>
        WpfTestContext.Run(() =>
        {
            var resolverCalls = 0;
            var window = CreateWindow("PLAN-APPEARANCE", planGroupId =>
            {
                resolverCalls++;
                return BuildGroup(planGroupId);
            });
            var planThumbnailImage = GetPrivateField<Image>(window, "_planThumbnailImage");
            var renderer = GetPrivateField<PlanThumbnailRenderer>(window, "_planThumbnailRenderer");
            var initialSource = planThumbnailImage.Source;
            var imageUpdatedCount = 0;
            renderer.ImageUpdated += _ => imageUpdatedCount++;

            window.NotifyAppearanceChanged();
            PumpDispatcher(TimeSpan.FromMilliseconds(450));

            Assert.Multiple(() =>
            {
                Assert.That(resolverCalls, Is.EqualTo(1));
                Assert.That(imageUpdatedCount, Is.GreaterThan(0));
                Assert.That(planThumbnailImage.Source, Is.Not.Null);
                Assert.That(planThumbnailImage.Source, Is.Not.SameAs(initialSource));
            });
        });

    [Test]
    public void Notifications_DoNotResolvePlanThumbnail_AfterWindowIsClosed() =>
        WpfTestContext.Run(() =>
        {
            var resolverCalls = 0;
            var window = CreateWindow("PLAN-CLOSED", planGroupId =>
            {
                resolverCalls++;
                return BuildGroup(planGroupId);
            });

            SetPrivateField(window, "_isClosed", true);

            window.NotifyPlanProgressChanged("PLAN-CLOSED");
            window.NotifyAppearanceChanged();

            Assert.That(resolverCalls, Is.EqualTo(0));
        });

    private static InboxMessageWindow CreateWindow(
        string planGroupId,
        Func<string, DecomposedTaskGroup?> resolvePlanGroupForThumbnail)
    {
        var message = new InboxMessage
        {
            Id = "msg-thumbnail",
            Subject = "Plan update",
            From = "coordinator",
            Body = "plan body",
            Attachments =
            [
                new InboxAttachment
                {
                    Type = DecomposePlanInbox.AttachmentType,
                    Label = "View plan",
                    PlanGroupId = planGroupId,
                    PlanRevision = "rev-1",
                },
            ],
            Actions = [],
        };

        return new InboxMessageWindow(
            message,
            (_, _) => Task.FromResult(true),
            resolvePlanGroupForThumbnail: resolvePlanGroupForThumbnail);
    }

    private static DecomposedTaskGroup BuildGroup(string planGroupId) =>
        new(
            planGroupId,
            "Plan group",
            "feature/plan-group",
            "summary",
            [
                new DecomposedSubTask(
                    "task-1",
                    "Implement task",
                    [],
                    "high"),
            ]);

    private static TField GetPrivateField<TField>(object instance, string fieldName)
        where TField : class
    {
        var field = instance.GetType().GetField(fieldName, InstanceNonPublic);
        Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
        var value = field!.GetValue(instance) as TField;
        Assert.That(value, Is.Not.Null, $"Expected private field '{fieldName}' to have a value.");
        return value!;
    }

    private static void SetPrivateField<TValue>(object instance, string fieldName, TValue value)
    {
        var field = instance.GetType().GetField(fieldName, InstanceNonPublic);
        Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
        field!.SetValue(instance, value);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
