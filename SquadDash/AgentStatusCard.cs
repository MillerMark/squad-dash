using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SquadDash;

internal sealed class AgentStatusCard : INotifyPropertyChanged, IHaveUniqueName {
    private static bool _isDarkTheme = false;
    private static bool _imagesVisible = true;
    private static readonly List<WeakReference<AgentStatusCard>> _liveInstances = [];

    public static bool ImagesVisible {
        get => _imagesVisible;
        set {
            if (_imagesVisible == value) return;
            _imagesVisible = value;
            lock (_liveInstances) {
                foreach (var weakRef in _liveInstances) {
                    if (weakRef.TryGetTarget(out var card)) {
                        card.OnPropertyChanged(nameof(AvatarImageVisibility));
                        card.OnPropertyChanged(nameof(InitialVisibility));
                    }
                }
            }
        }
    }

    private string _displayName;
    private string _roleText;
    private string _statusText;
    private string _bubbleText;
    private string _detailText;
    private Brush _accentBrush;
    private Brush _darkAccentBrush;
    private string _accentColorHex;
    private Visibility _cardVisibility;
    private Visibility _threadChipsVisibility;
    private Visibility _overflowChipVisibility;
    private string _overflowChipText;
    private bool _isInActivePanel;
    private bool _isTranscriptTargetSelected;
    private ImageSource? _agentImageSource;
    private double _avatarDiameter = 58;
    private double _initialFontSize = 20;
    private bool _hideImage;

    public AgentStatusCard(
        string name,
        string initial,
        string roleText,
        string statusText,
        string bubbleText,
        string detailText,
        string accentColorHex,
        string accentStorageKey,
        string? charterPath = null,
        string? historyPath = null,
        string? folderPath = null,
        bool isCompact = false,
        bool isLeadAgent = false,
        bool isDynamicAgent = false,
        bool? isUtilityAgent = null) {
        Name = name;
        Initial = initial;
        AccentStorageKey = accentStorageKey;
        CharterPath = charterPath;
        HistoryPath = historyPath;
        FolderPath = folderPath;
        IsCompact = isCompact;
        IsUtilityAgent = isUtilityAgent ?? isCompact;
        IsLeadAgent = isLeadAgent;
        IsDynamicAgent = isDynamicAgent;
        Threads = new ObservableCollection<TranscriptThreadState>();
        Threads.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ThreadsVisibility));
        _displayName = name;
        _roleText = roleText;
        _statusText = statusText;
        _bubbleText = bubbleText;
        _detailText = detailText;
        _accentColorHex = accentColorHex;
        _accentBrush = CreateAccentBrush(accentColorHex);
        _darkAccentBrush = CreateDarkAccentBrush(accentColorHex);
        _cardVisibility = Visibility.Visible;
        _threadChipsVisibility = Visibility.Collapsed;
        _overflowChipVisibility = Visibility.Collapsed;
        _overflowChipText = string.Empty;
        lock (_liveInstances)
            _liveInstances.Add(new WeakReference<AgentStatusCard>(this));
    }

    ~AgentStatusCard() {
        lock (_liveInstances)
            _liveInstances.RemoveAll(w => !w.TryGetTarget(out _));
    }

    public string Name { get; }
    public string UniqueName => Name;
    public string DisplayName {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }
    public string Initial { get; }
    public string AccentStorageKey { get; }
    public string? CharterPath { get; }
    public string? HistoryPath { get; }
    public string? FolderPath { get; }
    public bool IsCompact { get; }
    public bool IsUtilityAgent { get; }
    public bool IsLeadAgent { get; }
    public bool IsDynamicAgent { get; }
    public ObservableCollection<TranscriptThreadState> Threads { get; }
    public string RoleText {
        get => _roleText;
        set {
            if (!SetField(ref _roleText, value))
                return;

            OnPropertyChanged(nameof(RoleVisibility));
        }
    }
    public Visibility BubbleVisibility => string.IsNullOrWhiteSpace(BubbleText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CharterVisibility => string.IsNullOrWhiteSpace(CharterPath) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(DetailText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DocumentsVisibility => string.IsNullOrWhiteSpace(CharterPath) && string.IsNullOrWhiteSpace(HistoryPath)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public bool NameIsClickable => IsLeadAgent || DocumentsVisibility == Visibility.Visible;
    public Visibility ThreadsVisibility => Threads.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ThreadChipsVisibility {
        get => _threadChipsVisibility;
        set {
            if (_threadChipsVisibility == value)
                return;

            _threadChipsVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility OverflowChipVisibility {
        get => _overflowChipVisibility;
        set {
            if (_overflowChipVisibility == value)
                return;

            _overflowChipVisibility = value;
            OnPropertyChanged();
        }
    }
    public string OverflowChipText {
        get => _overflowChipText;
        set {
            if (!SetField(ref _overflowChipText, value))
                return;
        }
    }
    public Visibility HistoryVisibility => string.IsNullOrWhiteSpace(HistoryPath) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RoleVisibility => string.IsNullOrWhiteSpace(RoleText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StatusVisibility => string.IsNullOrWhiteSpace(StatusText) || IsStatusNoise(StatusText)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string DocumentsToolTip => $"Open {Name}'s history & charter";
    public Visibility CardVisibility {
        get => _cardVisibility;
        set {
            if (_cardVisibility == value)
                return;

            _cardVisibility = value;
            OnPropertyChanged();
        }
    }
    public bool IsTranscriptTargetSelected {
        get => _isTranscriptTargetSelected;
        set => SetField(ref _isTranscriptTargetSelected, value);
    }
    public bool IsInActivePanel {
        get => _isInActivePanel;
        set => SetField(ref _isInActivePanel, value);
    }

    public Brush AccentBrush {
        get => _accentBrush;
        private set => SetField(ref _accentBrush, value);
    }

    // In dark theme, dynamic (completed/temporary) agent cards use a muted dark-gray
    // so the bright default accent (#D0D5DB) doesn't jar against the dark background.
    private static readonly SolidColorBrush DarkDynamicAccentBrush =
        new(Color.FromRgb(0x55, 0x50, 0x4C));

    public Brush EffectiveAccentBrush =>
        _isDarkTheme && IsDynamicAgent ? DarkDynamicAccentBrush
        : _isDarkTheme ? _darkAccentBrush
        : _accentBrush;

    public string AccentColorHex {
        get => _accentColorHex;
        set {
            if (string.Equals(_accentColorHex, value, StringComparison.OrdinalIgnoreCase))
                return;

            _accentColorHex = value;
            AccentBrush = CreateAccentBrush(value);
            _darkAccentBrush = CreateDarkAccentBrush(value);
            OnPropertyChanged(nameof(EffectiveAccentBrush));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccentColorHex)));
        }
    }

    public ImageSource? AgentImageSource {
        get => _agentImageSource;
        set {
            if (ReferenceEquals(_agentImageSource, value))
                return;
            _agentImageSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvatarImageVisibility));
            OnPropertyChanged(nameof(InitialVisibility));
        }
    }

    public Visibility AvatarImageVisibility =>
        _imagesVisible && !_hideImage && _agentImageSource is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialVisibility =>
        !_imagesVisible || _hideImage || _agentImageSource is null ? Visibility.Visible : Visibility.Collapsed;

    public bool HideImage {
        get => _hideImage;
        set {
            if (_hideImage == value) return;
            _hideImage = value;
            OnPropertyChanged(nameof(AvatarImageVisibility));
        }
    }

    public double AvatarDiameter {
        get => _avatarDiameter;
        set {
            if (_avatarDiameter == value) return;
            _avatarDiameter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvatarCornerRadius));
        }
    }

    public CornerRadius AvatarCornerRadius => new(_avatarDiameter / 2);

    public double InitialFontSize {
        get => _initialFontSize;
        set {
            if (_initialFontSize == value) return;
            _initialFontSize = value;
            OnPropertyChanged();
        }
    }

    public string StatusText {
        get => _statusText;
        set {
            if (!SetField(ref _statusText, value))
                return;

            OnPropertyChanged(nameof(StatusVisibility));
        }
    }

    public string BubbleText {
        get => _bubbleText;
        set {
            if (!SetField(ref _bubbleText, value))
                return;

            OnPropertyChanged(nameof(BubbleVisibility));
        }
    }

    public string DetailText {
        get => _detailText;
        set {
            if (!SetField(ref _detailText, value))
                return;

            OnPropertyChanged(nameof(DetailVisibility));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref Brush field, Brush value, [CallerMemberName] string? propertyName = null) {
        if (ReferenceEquals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null) {
        if (field == value)
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null) {
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static SolidColorBrush CreateAccentBrush(string hex) =>
        ColorUtilities.CreateAccentBrush(hex);

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(EffectiveAccentBrush));

    public static void SetTheme(bool isDark) { _isDarkTheme = isDark; }

    public static bool IsDarkTheme => _isDarkTheme;

    private static SolidColorBrush CreateDarkAccentBrush(string hex) =>
        ColorUtilities.CreateDarkAccentBrush(hex);

    private static bool IsStatusNoise(string statusText) {
        if (string.IsNullOrWhiteSpace(statusText))
            return true;

        var normalized = new string(statusText
            .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            .ToArray());
        normalized = string.Join(" ", normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return string.Equals(normalized, "Ready", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Active", StringComparison.OrdinalIgnoreCase);
    }
}

internal enum SidebarEntryKind {
    File,
    Folder,
    Message
}

internal sealed record SidebarEntry(
    string Title,
    string Subtitle,
    string Path,
    bool Exists,
    SidebarEntryKind Kind) {
    public Visibility SubtitleVisibility => string.IsNullOrWhiteSpace(Subtitle)
        ? Visibility.Collapsed
        : Visibility.Visible;
}

internal sealed record AgentAccentPaletteOption(string Hex);

internal sealed record AgentAccentSelection(AgentStatusCard AgentCard, string AccentHex);
