#nullable enable

namespace SquadDash.PanelDocking;

public enum DockResizeOrientation
{
    Horizontal,
    Vertical,
}

internal enum DockResizeMode
{
    Normal,
    Proportional,
    Chain,
}

public interface IDockResizeSizeHint
{
    double GetMinimumDockSize(DockResizeOrientation orientation);
    double? GetMaximumUsefulDockSize(DockResizeOrientation orientation);
}

internal readonly record struct DockResizeParticipant(
    double CurrentSize,
    double MinimumSize,
    double? MaximumUsefulSize = null,
    bool CanShrink = true,
    bool CanGrow = true);

internal static class DockResizeEngine
{
    public static double[] Resize(
        IReadOnlyList<DockResizeParticipant> participants,
        int splitterLeftParticipantIndex,
        DockResizeMode mode,
        double delta)
    {
        var sizes = participants.Select(p => Math.Max(0, p.CurrentSize)).ToArray();
        if (participants.Count < 2 ||
            splitterLeftParticipantIndex < 0 ||
            splitterLeftParticipantIndex >= participants.Count - 1 ||
            Math.Abs(delta) < 0.01)
        {
            return sizes;
        }

        return mode switch
        {
            DockResizeMode.Chain => ResizeChain(participants, sizes, splitterLeftParticipantIndex, delta),
            DockResizeMode.Proportional => ResizeProportional(participants, sizes, splitterLeftParticipantIndex, delta),
            _ => ResizeNormal(participants, sizes, splitterLeftParticipantIndex, delta),
        };
    }

    private static double[] ResizeNormal(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        int leftIndex,
        double delta)
    {
        int rightIndex = leftIndex + 1;
        if (delta > 0)
        {
            var amount = Math.Min(delta, Math.Min(ConsequenceGrowCapacity(participants[leftIndex], sizes[leftIndex]), ShrinkCapacity(participants[rightIndex], sizes[rightIndex])));
            sizes[leftIndex] += amount;
            sizes[rightIndex] -= amount;
        }
        else
        {
            var requested = -delta;
            var amount = Math.Min(requested, Math.Min(ShrinkCapacity(participants[leftIndex], sizes[leftIndex]), ConsequenceGrowCapacity(participants[rightIndex], sizes[rightIndex])));
            sizes[leftIndex] -= amount;
            sizes[rightIndex] += amount;
        }

        return sizes;
    }

    private static double[] ResizeProportional(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        int leftIndex,
        double delta)
    {
        var rightIndices = Enumerable.Range(leftIndex + 1, participants.Count - leftIndex - 1).ToArray();
        if (rightIndices.Length < 2)
            return ResizeNormal(participants, sizes, leftIndex, delta);

        if (delta > 0)
        {
            var amount = Math.Min(delta, Math.Min(GrowCapacity(participants[leftIndex], sizes[leftIndex]), TotalShrinkCapacity(participants, sizes, rightIndices)));
            sizes[leftIndex] += amount;
            ApplyProportionalShrink(participants, sizes, rightIndices, amount);
        }
        else
        {
            var requested = -delta;
            var amount = Math.Min(requested, Math.Min(ShrinkCapacity(participants[leftIndex], sizes[leftIndex]), TotalGrowCapacity(participants, sizes, rightIndices)));
            sizes[leftIndex] -= amount;
            ApplyProportionalGrow(participants, sizes, rightIndices, amount);
        }

        return sizes;
    }

    private static double[] ResizeChain(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        int leftIndex,
        double delta)
    {
        var leftNearestFirst = Enumerable.Range(0, leftIndex + 1).Reverse().ToArray();
        var rightNearestFirst = Enumerable.Range(leftIndex + 1, participants.Count - leftIndex - 1).ToArray();

        if (delta > 0)
        {
            var amount = Math.Min(delta, Math.Min(TotalGrowCapacity(participants, sizes, leftNearestFirst), TotalShrinkCapacity(participants, sizes, rightNearestFirst)));
            ApplySequentialGrow(participants, sizes, leftNearestFirst, amount);
            ApplySequentialShrink(participants, sizes, rightNearestFirst, amount);
        }
        else
        {
            var requested = -delta;
            var amount = Math.Min(requested, Math.Min(TotalShrinkCapacity(participants, sizes, leftNearestFirst), TotalConsequenceGrowCapacity(participants, sizes, rightNearestFirst)));
            ApplySequentialShrink(participants, sizes, leftNearestFirst, amount);
            ApplySequentialConsequenceGrow(participants, sizes, rightNearestFirst, amount);
        }

        return sizes;
    }

    private static double ShrinkCapacity(DockResizeParticipant participant, double size)
    {
        if (!participant.CanShrink) return 0;
        return Math.Max(0, size - Math.Max(0, participant.MinimumSize));
    }

    private static double GrowCapacity(DockResizeParticipant participant, double size)
    {
        if (!participant.CanGrow) return 0;
        if (participant.MaximumUsefulSize is not { } max) return double.PositiveInfinity;
        return Math.Max(0, Math.Max(participant.MinimumSize, max) - size);
    }

    // Used when a panel receives space because another participant is being compressed.
    // MaximumUsefulSize remains a cap while approaching it from below, but it must not freeze
    // the splitter when the receiving participant already starts above that soft cap.
    private static double ConsequenceGrowCapacity(DockResizeParticipant participant, double size)
    {
        if (!participant.CanGrow) return 0;
        if (participant.MaximumUsefulSize is not { } max) return double.PositiveInfinity;
        if (size > max) return double.PositiveInfinity;
        return Math.Max(0, Math.Max(participant.MinimumSize, max) - size);
    }

    private static double TotalShrinkCapacity(IReadOnlyList<DockResizeParticipant> participants, double[] sizes, IReadOnlyList<int> indices) =>
        indices.Sum(i => ShrinkCapacity(participants[i], sizes[i]));

    private static double TotalGrowCapacity(IReadOnlyList<DockResizeParticipant> participants, double[] sizes, IReadOnlyList<int> indices)
    {
        double total = 0;
        foreach (var i in indices)
        {
            var capacity = GrowCapacity(participants[i], sizes[i]);
            if (double.IsPositiveInfinity(capacity))
                return double.PositiveInfinity;
            total += capacity;
        }

        return total;
    }

    private static double TotalConsequenceGrowCapacity(IReadOnlyList<DockResizeParticipant> participants, double[] sizes, IReadOnlyList<int> indices)
    {
        double total = 0;
        foreach (var i in indices)
        {
            var capacity = ConsequenceGrowCapacity(participants[i], sizes[i]);
            if (double.IsPositiveInfinity(capacity))
                return double.PositiveInfinity;
            total += capacity;
        }

        return total;
    }

    private static void ApplySequentialShrink(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        IReadOnlyList<int> indices,
        double amount)
    {
        var remaining = amount;
        foreach (var i in indices)
        {
            if (remaining <= 0) break;
            var applied = Math.Min(remaining, ShrinkCapacity(participants[i], sizes[i]));
            sizes[i] -= applied;
            remaining -= applied;
        }
    }

    private static void ApplySequentialGrow(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        IReadOnlyList<int> indices,
        double amount)
    {
        var remaining = amount;
        foreach (var i in indices)
        {
            if (remaining <= 0) break;
            var capacity = GrowCapacity(participants[i], sizes[i]);
            var applied = double.IsPositiveInfinity(capacity) ? remaining : Math.Min(remaining, capacity);
            sizes[i] += applied;
            remaining -= applied;
        }
    }

    private static void ApplySequentialConsequenceGrow(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        IReadOnlyList<int> indices,
        double amount)
    {
        var remaining = amount;
        foreach (var i in indices)
        {
            if (remaining <= 0) break;
            var capacity = ConsequenceGrowCapacity(participants[i], sizes[i]);
            var applied = double.IsPositiveInfinity(capacity) ? remaining : Math.Min(remaining, capacity);
            sizes[i] += applied;
            remaining -= applied;
        }
    }

    private static void ApplyProportionalShrink(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        IReadOnlyList<int> indices,
        double amount)
    {
        var active = indices.ToList();
        var remaining = amount;

        while (remaining > 0.01 && active.Count > 0)
        {
            var basis = active.Sum(i => Math.Max(0.01, sizes[i]));
            var clamped = false;

            foreach (var i in active.ToArray())
            {
                var share = remaining * (Math.Max(0.01, sizes[i]) / basis);
                var capacity = ShrinkCapacity(participants[i], sizes[i]);
                if (share >= capacity - 0.01)
                {
                    sizes[i] -= capacity;
                    remaining -= capacity;
                    active.Remove(i);
                    clamped = true;
                }
            }

            if (clamped) continue;

            foreach (var i in active)
            {
                var share = remaining * (Math.Max(0.01, sizes[i]) / basis);
                sizes[i] -= share;
            }

            break;
        }
    }

    private static void ApplyProportionalGrow(
        IReadOnlyList<DockResizeParticipant> participants,
        double[] sizes,
        IReadOnlyList<int> indices,
        double amount)
    {
        var active = indices.ToList();
        var remaining = amount;

        while (remaining > 0.01 && active.Count > 0)
        {
            var basis = active.Sum(i => Math.Max(0.01, sizes[i]));
            var clamped = false;

            foreach (var i in active.ToArray())
            {
                var share = remaining * (Math.Max(0.01, sizes[i]) / basis);
                var capacity = GrowCapacity(participants[i], sizes[i]);
                if (!double.IsPositiveInfinity(capacity) && share >= capacity - 0.01)
                {
                    sizes[i] += capacity;
                    remaining -= capacity;
                    active.Remove(i);
                    clamped = true;
                }
            }

            if (clamped) continue;

            foreach (var i in active)
            {
                var share = remaining * (Math.Max(0.01, sizes[i]) / basis);
                sizes[i] += share;
            }

            break;
        }
    }
}
