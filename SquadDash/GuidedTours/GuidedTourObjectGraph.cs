using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.GuidedTours;

internal static class GuidedTourObjectGraph
{
    internal static GuidedTour? Rebind(GuidedTour? previousTour, IReadOnlyList<GuidedTour> freshlyLoadedTours)
    {
        if (previousTour is null)
            return null;

        return freshlyLoadedTours.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(previousTour.Id) &&
             string.Equals(candidate.Id, previousTour.Id, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(candidate.Name, previousTour.Name, StringComparison.OrdinalIgnoreCase));
    }
}
