using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CliModelCatalogServiceTests {
    [Test]
    public async Task GetModelMetadataAsync_TtlExpiry_TriggersRefetch() {
        var now = new DateTimeOffset(2026, 08, 20, 12, 0, 0, TimeSpan.Zero);
        var fetchCount = 0;

        var service = new CliModelCatalogService(
            _ => {
                fetchCount++;
                return Task.FromResult<IReadOnlyList<CliModelMetadata>>([
                    new CliModelMetadata("gpt-5", "chat", 1.23m, 4.56m, "ga", ["low", "medium", "high"])
                ]);
            },
            cacheTtl: TimeSpan.FromSeconds(5),
            utcNow: () => now);

        _ = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);
        now = now.AddSeconds(4);
        _ = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);
        now = now.AddSeconds(2);
        _ = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);

        Assert.That(fetchCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetModelMetadataAsync_OnRefreshFailure_ServesStaleCache() {
        var now = new DateTimeOffset(2026, 08, 20, 12, 0, 0, TimeSpan.Zero);
        var fetchCount = 0;

        var service = new CliModelCatalogService(
            _ => {
                fetchCount++;
                if (fetchCount == 1) {
                    return Task.FromResult<IReadOnlyList<CliModelMetadata>>([
                        new CliModelMetadata("gpt-5", "chat", 0.9m, 1.1m, "preview", ["low", "high"])
                    ]);
                }

                throw new TimeoutException("offline");
            },
            cacheTtl: TimeSpan.FromSeconds(5),
            utcNow: () => now);

        var first = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);
        now = now.AddSeconds(6);
        var second = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);

        Assert.Multiple(() => {
            Assert.That(fetchCount, Is.EqualTo(2));
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.ModelId, Is.EqualTo("gpt-5"));
            Assert.That(second.CliCategory, Is.EqualTo("chat"));
            Assert.That(second.ReleaseStatus, Is.EqualTo("preview"));
            Assert.That(second.SupportedReasoningEfforts, Is.EqualTo(new[] { "low", "high" }));
            Assert.That(service.IsCacheStale, Is.True);
        });
    }

    [Test]
    public async Task GetModelMetadataAsync_NonCopilotProvider_ReturnsNullEvenWithMatchingCacheEntry() {
        var fetchCount = 0;
        var service = new CliModelCatalogService(
            _ => {
                fetchCount++;
                return Task.FromResult<IReadOnlyList<CliModelMetadata>>([
                    new CliModelMetadata("gpt-5", "chat", 1.0m, 2.0m, "ga", ["low"])
                ]);
            });

        var copilotResult = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);
        var customResult = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Custom);
        var byokResult = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Byok);

        Assert.Multiple(() => {
            Assert.That(fetchCount, Is.EqualTo(1));
            Assert.That(copilotResult, Is.Not.Null);
            Assert.That(customResult, Is.Null);
            Assert.That(byokResult, Is.Null);
        });
    }

    [Test]
    public async Task GetModelMetadataAsync_ColdStartFetchFailure_ReturnsNull() {
        var fetchCount = 0;
        var service = new CliModelCatalogService(
            _ => {
                fetchCount++;
                throw new TimeoutException("offline");
            });

        var result = await service.GetModelMetadataAsync("gpt-5", AiProviderType.Copilot);

        Assert.Multiple(() => {
            Assert.That(fetchCount, Is.EqualTo(1));
            Assert.That(result, Is.Null);
        });
    }
}
