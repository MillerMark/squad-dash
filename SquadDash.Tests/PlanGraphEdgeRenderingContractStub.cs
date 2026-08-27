using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

// Stub: full implementation lives in PlanGraphEdgeRenderingContract.cs, which is excluded from the test project.
internal sealed class PlanGraphConnectorGroup;

// Stub used by linked PlanThumbnailRenderer.cs in test builds.
internal sealed class PlanGraphEdgeRenderingContract
{
    public PlanGraphEdgeRenderingContract(double lineThicknessMultiplier = 1.0) { }

    public PlanGraphConnectorGroup AddConnector(
        Canvas canvas,
        Point from,
        Point to,
        bool arrowHead,
        int skipCount = 0,
        bool dashed = false,
        string? toolTip = null,
        double splitAtX = double.NaN,
        PlanGraphConnectorGroup? existingGroup = null)
    {
        return existingGroup ?? new PlanGraphConnectorGroup();
    }
}
