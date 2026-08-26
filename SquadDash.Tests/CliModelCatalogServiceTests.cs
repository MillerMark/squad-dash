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

    [Test]
    public async Task GetAllModelIds_AfterRefresh_ReturnsCachedModelIds() {
        var service = new CliModelCatalogService(
            _ => Task.FromResult<IReadOnlyList<CliModelMetadata>>([
                new CliModelMetadata("gpt-5", "chat", null, null, "ga", ["low"]),
                new CliModelMetadata("claude-sonnet-4.6", "chat", null, null, "ga", ["low", "medium", "high"])
            ]));

        Assert.That(service.GetAllModelIds(), Is.Empty);

        await service.RefreshAsync();

        Assert.That(service.GetAllModelIds(), Is.EquivalentTo(new[] { "gpt-5", "claude-sonnet-4.6" }));
    }

    [Test]
    public void ParseCatalog_EnrichedFieldsPresent_HydratesMetadata() {
        var parsed = CliModelCatalogService.ParseCatalog("""
            [
              {
                "id": "gpt-5",
                "name": "GPT-5",
                "category": "chat",
                "releaseStatus": "ga",
                "inputPricePerMillionTokens": 1.25,
                "outputPricePerMillionTokens": "10.00",
                "supportedReasoningEfforts": ["low", "high"]
              }
            ]
            """);

        Assert.That(parsed, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(parsed[0].ModelId, Is.EqualTo("gpt-5"));
            Assert.That(parsed[0].CliCategory, Is.EqualTo("chat"));
            Assert.That(parsed[0].ReleaseStatus, Is.EqualTo("ga"));
            Assert.That(parsed[0].InputPricePerMillionTokens, Is.EqualTo(1.25m));
            Assert.That(parsed[0].OutputPricePerMillionTokens, Is.EqualTo(10.00m));
            Assert.That(parsed[0].SupportedReasoningEfforts, Is.EqualTo(new[] { "low", "high" }));
        });
    }

    [Test]
    public void ParseCatalog_EnrichedFieldsAbsent_UsesNullAndEmptyDefaults() {
        var parsed = CliModelCatalogService.ParseCatalog("""
            [
              {
                "id": "gpt-4.1-mini",
                "name": "GPT-4.1 mini"
              }
            ]
            """);

        Assert.That(parsed, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(parsed[0].CliCategory, Is.Null);
            Assert.That(parsed[0].ReleaseStatus, Is.Null);
            Assert.That(parsed[0].InputPricePerMillionTokens, Is.Null);
            Assert.That(parsed[0].OutputPricePerMillionTokens, Is.Null);
            Assert.That(parsed[0].SupportedReasoningEfforts, Is.Empty);
        });
    }

    [Test]
    public void ParseCatalog_SingularReasoningEffortField_IsAccepted() {
        var parsed = CliModelCatalogService.ParseCatalog("""
            [
              {
                "id": "gpt-5",
                "supportedReasoningEffort": ["medium", "high"]
              }
            ]
            """);

        Assert.That(parsed, Has.Count.EqualTo(1));
        Assert.That(parsed[0].SupportedReasoningEfforts, Is.EqualTo(new[] { "medium", "high" }));
    }
}
