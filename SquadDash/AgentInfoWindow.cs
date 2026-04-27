using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;

internal sealed class AgentInfoWindow : Window {
    private AgentInfoWindow(AgentStatusCard card, string? workspaceFolderPath, string agentImageAssetsDirectory) {
        Title = card.Name;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MaxWidth = 520;
        Width = 520;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        var root = new StackPanel();
        Content = root;

        // Portrait image
        var imageSource = ResolveImage(card, workspaceFolderPath, agentImageAssetsDirectory);
        if (imageSource is not null) {
            var imageBorder = new Border {
                Height = 364,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var image = new Image {
                Source = imageSource,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            imageBorder.Child = image;
            root.Children.Add(imageBorder);
        }

        // Agent name
        var nameBlock = new TextBlock {
            Text = card.Name,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24, 16, 24, 8)
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        root.Children.Add(nameBlock);

        // Role pill
        if (!string.IsNullOrWhiteSpace(card.RoleText)) {
            var rolePill = new Border {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 4, 12, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            rolePill.SetResourceReference(Border.BackgroundProperty, "RoleBadgeSurface");
            rolePill.SetResourceReference(Border.BorderBrushProperty, "BadgeBorder");
            var roleText = new TextBlock {
                Text = card.RoleText,
                FontSize = 13
            };
            roleText.SetResourceReference(TextBlock.ForegroundProperty, "AgentRoleText");
            rolePill.Child = roleText;
            root.Children.Add(rolePill);
        }

        // Bio
        var bioText = LoadBio(card, workspaceFolderPath);
        if (!string.IsNullOrWhiteSpace(bioText)) {
            var scrollViewer = new ScrollViewer {
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(24, 0, 24, 16)
            };
            var bioBlock = new TextBlock {
                Text = bioText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 20
            };
            bioBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            scrollViewer.Content = bioBlock;
            root.Children.Add(scrollViewer);
        }

        // Close button
        var closeButton = new Button {
            Content = "Close",
            Width = 88,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 20)
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        closeButton.Click += (_, _) => Close();
        root.Children.Add(closeButton);
    }

    public static void Show(Window owner, AgentStatusCard card, string? workspaceFolderPath, string agentImageAssetsDirectory) {
        var window = new AgentInfoWindow(card, workspaceFolderPath, agentImageAssetsDirectory) { Owner = owner };
        window.Show();
    }

    private static ImageSource? ResolveImage(AgentStatusCard card, string? workspaceFolderPath, string agentImageAssetsDirectory) {
        if (card.AgentImageSource is not null)
            return card.AgentImageSource;

        var bundledPath = AgentImagePathResolver.ResolveBundledPath(card, agentImageAssetsDirectory);
        if (!string.IsNullOrWhiteSpace(bundledPath))
            return LoadBitmap(bundledPath);

        return null;
    }

    private static BitmapImage? LoadBitmap(string path) {
        try {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch {
            return null;
        }
    }

    private static string? LoadBio(AgentStatusCard card, string? workspaceFolderPath) {
        var mdContent = TryLoadProfilesMd(workspaceFolderPath)
            ?? TryLoadSquadDashMd(workspaceFolderPath)
            ?? SquadInstallerService.LoadEmbeddedSquadDashProfilesMdPublic()
            ?? SquadInstallerService.LoadEmbeddedSquadDashMdPublic();

        if (mdContent is not null) {
            var bio = ExtractBioFromMd(mdContent, card.Name);
            if (!string.IsNullOrWhiteSpace(bio))
                return bio;
        }

        // Fall back to charter responsibilities
        if (card.CharterPath is not null && File.Exists(card.CharterPath))
            return ExtractResponsibilitiesFromCharter(card.CharterPath);

        return null;
    }

    private static string? TryLoadProfilesMd(string? workspaceFolderPath) {
        if (workspaceFolderPath is null)
            return null;

        var path = Path.Combine(workspaceFolderPath, ".squad", "universes", "squaddash-profiles.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string? TryLoadSquadDashMd(string? workspaceFolderPath) {
        if (workspaceFolderPath is null)
            return null;
        var path = Path.Combine(workspaceFolderPath, ".squad", "universes", "squaddash.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string? ExtractBioFromMd(string content, string agentName) {
        var lines = content.Split('\n');
        var headerLine = $"### {agentName}";
        int start = -1;
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].Trim().Equals(headerLine, StringComparison.OrdinalIgnoreCase)) {
                start = i + 1;
                break;
            }
        }
        if (start < 0)
            return null;

        // Collect lines until next ---
        var section = new List<string>();
        for (var i = start; i < lines.Length; i++) {
            if (lines[i].Trim() == "---")
                break;
            section.Add(lines[i]);
        }

        var bioLines = new List<string>();

        // Extract Bio paragraph
        for (var i = 0; i < section.Count; i++) {
            var trimmed = section[i].Trim();
            if (trimmed.StartsWith("**Bio:**", StringComparison.OrdinalIgnoreCase)) {
                var bioText = trimmed["**Bio:**".Length..].Trim();
                if (!string.IsNullOrEmpty(bioText))
                    bioLines.Add(bioText);
                // Continue collecting wrapped bio lines
                for (var j = i + 1; j < section.Count; j++) {
                    var next = section[j].Trim();
                    if (next.StartsWith("**") || string.IsNullOrEmpty(next))
                        break;
                    bioLines.Add(next);
                }
                break;
            }
        }

        // Extract Specialties list
        var specialties = new List<string>();
        for (var i = 0; i < section.Count; i++) {
            if (section[i].Trim().StartsWith("**Specialties:**", StringComparison.OrdinalIgnoreCase)) {
                for (var j = i + 1; j < section.Count; j++) {
                    var next = section[j].Trim();
                    if (next.StartsWith("-"))
                        specialties.Add("• " + next[1..].Trim());
                    else if (next.StartsWith("**") || (!string.IsNullOrEmpty(next) && !next.StartsWith("-")))
                        break;
                }
                break;
            }
        }

        if (bioLines.Count == 0 && specialties.Count == 0)
            return null;

        var result = new System.Text.StringBuilder();
        if (bioLines.Count > 0)
            result.AppendLine(string.Join(" ", bioLines));
        if (specialties.Count > 0) {
            if (result.Length > 0)
                result.AppendLine();
            result.AppendLine("Specialties:");
            foreach (var s in specialties)
                result.AppendLine(s);
        }

        return result.ToString().Trim();
    }

    private static string? ExtractResponsibilitiesFromCharter(string charterPath) {
        try {
            var lines = File.ReadAllLines(charterPath);
            var inSection = false;
            var collected = new List<string>();
            foreach (var line in lines) {
                var heading = line.TrimStart('#').Trim();
                if (heading.Equals("Responsibilities", StringComparison.OrdinalIgnoreCase) ||
                    heading.Equals("What I Own", StringComparison.OrdinalIgnoreCase)) {
                    inSection = true;
                    continue;
                }
                if (inSection) {
                    if (line.StartsWith('#'))
                        break;
                    var clean = line.Trim().TrimStart('-').Trim()
                        .Replace("**", "")
                        .Replace("__", "");
                    if (!string.IsNullOrEmpty(clean))
                        collected.Add("• " + clean);
                }
            }
            return collected.Count > 0 ? string.Join("\n", collected) : null;
        }
        catch {
            return null;
        }
    }
}
