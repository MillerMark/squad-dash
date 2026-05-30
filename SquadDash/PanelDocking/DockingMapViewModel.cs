#nullable enable

namespace SquadDash.PanelDocking;

internal sealed record SlotButtonViewModel(
    string Label,
    bool IsSourcePanel,
    bool IsExpansionButton,
    double X,
    double Y,
    double Width,
    double Height,
    DockZone TargetZone,
    int TargetOrder,
    string SourcePanelId
);

internal sealed record DockingMapViewModel(
    IReadOnlyList<SlotButtonViewModel> Slots,
    double PopupWidth,
    double PopupHeight,
    double SourceSlotCenterX,
    double SourceSlotCenterY
);
