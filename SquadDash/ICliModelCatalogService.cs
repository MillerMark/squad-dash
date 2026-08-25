using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal interface ICliModelCatalogService {
    Task<CliModelMetadata?> GetModelMetadataAsync(
        string modelId,
        AiProviderType providerType,
        CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
