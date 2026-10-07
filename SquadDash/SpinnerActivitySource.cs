using System;
using System.ComponentModel;
using System.Windows.Media;

namespace SquadDash;

internal interface ISpinnerActivitySource : INotifyPropertyChanged
{
    Brush EffectiveSpinnerAccentBrush { get; }
    event EventHandler<SpinnerActivityKind>? ActivityPulsed;
    event EventHandler? TurnEnded;
    event EventHandler? ActivityPaused;
}

internal interface IStatefulSpinnerActivitySource : ISpinnerActivitySource
{
    bool IsActivityActive { get; }
    SpinnerActivityKind CurrentActivityKind { get; }
}

/// <summary>
/// Per-transcript activity channel. Multiple runs by the same roster agent share an
/// <see cref="AgentStatusCard"/>, but their transcript-title spinners must not share state.
/// </summary>
internal sealed class TranscriptSpinnerActivitySource : IStatefulSpinnerActivitySource
{
    private AgentStatusCard? _accentSource;

    internal AgentStatusCard? AccentSource
    {
        get => _accentSource;
        set
        {
            if (ReferenceEquals(_accentSource, value))
                return;

            if (_accentSource is not null)
                _accentSource.PropertyChanged -= OnAccentSourcePropertyChanged;
            _accentSource = value;
            if (_accentSource is not null)
                _accentSource.PropertyChanged += OnAccentSourcePropertyChanged;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveSpinnerAccentBrush)));
        }
    }

    public Brush EffectiveSpinnerAccentBrush =>
        _accentSource?.EffectiveSpinnerAccentBrush ?? Brushes.Gray;
    public bool IsActivityActive { get; private set; }
    public SpinnerActivityKind CurrentActivityKind { get; private set; } = SpinnerActivityKind.Thinking;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<SpinnerActivityKind>? ActivityPulsed;
    public event EventHandler? TurnEnded;
    public event EventHandler? ActivityPaused;

    internal void FireActivityPulse(SpinnerActivityKind kind)
    {
        IsActivityActive = true;
        CurrentActivityKind = kind;
        ActivityPulsed?.Invoke(this, kind);
    }

    internal void NotifyTurnEnded()
    {
        IsActivityActive = false;
        TurnEnded?.Invoke(this, EventArgs.Empty);
    }

    internal void NotifyActivityPaused()
    {
        IsActivityActive = false;
        ActivityPaused?.Invoke(this, EventArgs.Empty);
    }

    private void OnAccentSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentStatusCard.AccentColorHex)
                           or nameof(AgentStatusCard.EffectiveAccentBrush)
                           or nameof(AgentStatusCard.EffectiveSpinnerAccentBrush))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveSpinnerAccentBrush)));
        }
    }
}
