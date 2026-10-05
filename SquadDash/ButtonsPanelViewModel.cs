using System.Collections.Generic;

namespace SquadDash;

internal sealed class ButtonsPanelViewModel {
    public IReadOnlyList<ButtonDefinition> Buttons { get; set; } = [];
    public string FilterText { get; set; } = string.Empty;
    public string? LoadError { get; set; }
}
