using System.Threading.Tasks;

namespace SquadDash;

public interface ISquadCliSelectionStore
{
    CliSelectionMode GetSelectionMode(string workspaceDir);
    string? GetCustomPath(string workspaceDir);
    Task SaveSelectionAsync(string workspaceDir, CliSelectionMode mode, string? customPath);
}
