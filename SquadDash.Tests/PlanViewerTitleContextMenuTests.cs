using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerTitleContextMenuTests
{
    [Test]
    public void VisibleTitle_ExposesTheRenderedLabelBoundToWindowTitle()
    {
        var chrome = ReadSource("ChromedWindow.cs");
        var viewer = ReadSource("PlanViewerWindow.cs");

        Assert.Multiple(() =>
        {
            Assert.That(chrome, Does.Contain("protected TextBlock? TitleLabel { get; set; }")
                .And.Contain("var titleLabel = new TextBlock")
                .And.Contain("titleLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });")
                .And.Contain("TitleLabel = titleLabel;")
                .And.Contain("titleRow.Children.Add(titleLabel);"));
            Assert.That(chrome, Does.Match(@"WindowStyle\s*=\s*WindowStyle\.None;"));
            Assert.That(viewer, Does.Contain("ApplyOuterBorder(titleText: group.GroupTitle)")
                .And.Contain("if (TitleLabel is { } planTitle)")
                .And.Contain("planTitle.ContextMenu = CreatePlanTitleContextMenu(plan, durablePlan);"));
            Assert.That(viewer, Does.Match(
                "Title\\s*=\\s*\\$\"Plan — \\{durablePlan\\?\\.DisplayName \\?\\? plan\\.Group\\.GroupTitle\\}\";"));
        });
    }

    [Test]
    public void TitleChrome_OpensMenuOnlyOverTitleAndPreservesNativeCaptionGestures()
    {
        var source = ReadSource("ChromedWindow.cs");
        var handler = Method(source, "case WM_NCRBUTTONUP:", "case WM_MOUSEMOVE:");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private const int WM_NCRBUTTONUP = 0x00A5;")
                .And.Contain("private const int HTCAPTION = 2;")
                .And.Contain("AddHook(HoverWndProc)"));
            Assert.That(handler, Does.Contain("wParam == HTCAPTION")
                .And.Contain("TitleLabel is { ContextMenu: { } menu } titleLabel")
                .And.Contain("unchecked((short)lParam.ToInt64())")
                .And.Contain("unchecked((short)(lParam.ToInt64() >> 16))")
                .And.Contain("new Rect(titleLabel.RenderSize).Contains(titleLabel.PointFromScreen(screenPoint))")
                .And.Contain("menu.PlacementTarget = titleLabel;")
                .And.Contain("menu.Placement = PlacementMode.MousePoint;")
                .And.Contain("menu.IsOpen = true;")
                .And.Contain("handled = true;"));
            Assert.That(source, Does.Not.Contain("SetIsHitTestVisibleInChrome(titleLabel")
                .And.Not.Contain("case WM_NCLBUTTON")
                .And.Not.Contain("DragMove();"));
            Assert.That(BuildContent(), Does.Not.Contain("titleLabel.MouseLeftButtonDown")
                .And.Not.Contain("SetIsHitTestVisibleInChrome(titleLabel"));
        });
    }

    [Test]
    public void BuildContent_ReplacesTitleMenuOutsideOneTimeChromeCreation()
    {
        var build = BuildContent();

        Assert.Multiple(() =>
        {
            Assert.That(build, Does.Contain(
                "        _contentHolder ??= ApplyOuterBorder(titleText: group.GroupTitle);\n" +
                "        if (TitleLabel is { } planTitle)\n" +
                "            planTitle.ContextMenu = CreatePlanTitleContextMenu(plan, durablePlan);\n" +
                "        _contentHolder.Child = root;"));
            Assert.That(Regex.Matches(build, @"planTitle\.ContextMenu\s*=").Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void TitleMenu_HasOnlyTwoCopyActionsWithCurrentContentAndDisabledMissingPath()
    {
        var menu = Method(ReadSource("PlanViewerWindow.cs"),
            "private ContextMenu CreatePlanTitleContextMenu(", "private static void CopyPlanTitleText(");
        var headers = Regex.Matches(menu, "CreateThemedMenuItem\\(\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(headers, Is.EqualTo(new[] { "Copy plan name", "Copy plan file path" }));
            Assert.That(Regex.Matches(menu, @"menu\.Items\.Add\(").Count, Is.EqualTo(2));
            Assert.That(menu, Does.Contain("menu.Items.Add(copyName);")
                .And.Contain("menu.Items.Add(copyPath);")
                .And.Not.Contain("AppendDeveloperNormalizeMenuItem")
                .And.Not.Contain("CreateThemedMenuSeparator"));
            Assert.That(menu, Does.Match(
                @"PlanViewerTitleClipboardContent\.Create\(\s*durablePlan, plan\.Group\.GroupTitle, _squadFolderPath\);"));
            Assert.That(menu, Does.Contain("copyName.Click += (_, _) => CopyPlanTitleText(content.Name);")
                .And.Contain("CreateThemedMenuItem(\"Copy plan file path\", content.FilePath is not null)")
                .And.Contain("if (content.FilePath is { } filePath)\n" +
                    "            copyPath.Click += (_, _) => CopyPlanTitleText(filePath);"));
        });
    }

    [Test]
    public void TitleMenu_ReusesDynamicThemeStyleHelpers()
    {
        var source = ReadSource("PlanViewerWindow.cs");
        var titleMenu = Method(source, "private ContextMenu CreatePlanTitleContextMenu(",
            "private static void CopyPlanTitleText(");
        var menuStyle = Method(source, "private static ContextMenu CreateThemedMenu(",
            "private static MenuItem CreateThemedMenuItem(");
        var itemStyle = Method(source, "private static MenuItem CreateThemedMenuItem(",
            "private void AppendDeveloperNormalizeMenuItem(");

        Assert.Multiple(() =>
        {
            Assert.That(titleMenu, Does.Contain("var menu = CreateThemedMenu();")
                .And.Not.Contain("new ContextMenu")
                .And.Not.Contain("new MenuItem"));
            Assert.That(menuStyle, Does.Contain(
                "menu.SetResourceReference(ContextMenu.StyleProperty, \"ThemedContextMenuStyle\");"));
            Assert.That(itemStyle, Does.Contain("Header = header, IsEnabled = isEnabled")
                .And.Contain("item.SetResourceReference(MenuItem.StyleProperty, \"ThemedMenuItemStyle\");"));
        });
    }

    [Test]
    public void CopyHandler_CatchesAndLogsClipboardComFailures()
    {
        var copy = Method(ReadSource("PlanViewerWindow.cs"),
            "private static void CopyPlanTitleText(", "private static ContextMenu CreateThemedMenu(");

        Assert.That(copy, Does.Match(
            @"try\s*\{\s*System\.Windows\.Clipboard\.SetText\(text\);\s*\}\s*" +
            @"catch \(System\.Runtime\.InteropServices\.COMException ex\)\s*\{\s*" +
            "SquadDashTrace\\.Write\\(\"PlanViewer\", \\$\"Could not copy plan reference to the clipboard: \\{ex\\.Message\\}\"\\);\\s*\\}"));
    }

    [TestCase("  Friendly plan alias \t", "Friendly plan alias")]
    [TestCase(null, "Original plan title")]
    [TestCase("", "Original plan title")]
    [TestCase(" \t ", "Original plan title")]
    public void Content_UsesTrimmedDisplayAliasOrOriginalTitle(string? alias, string expectedName)
    {
        var plan = BuildPlan() with { DisplayTitle = alias };

        var content = PlanViewerTitleClipboardContent.Create(plan, "Stale group title", null);

        Assert.Multiple(() =>
        {
            Assert.That(content.Name, Is.EqualTo(expectedName));
            Assert.That(plan.Title, Is.EqualTo("Original plan title"));
        });
    }

    [TestCase(null)]
    [TestCase(@"C:\Workspace with spaces\.squad")]
    public void PendingPlan_UsesGroupTitleAndNeverFabricatesPath(string? folder)
    {
        var content = PlanViewerTitleClipboardContent.Create(null, "Pending group title", folder);

        Assert.Multiple(() =>
        {
            Assert.That(content.Name, Is.EqualTo("Pending group title"));
            Assert.That(content.FilePath, Is.Null);
        });
    }

    [Test]
    public void DurablePlan_PathUsesCanonicalIdentityNotDisplayTitle()
    {
        var plan = BuildPlan() with { DisplayTitle = "  Friendly alias  " };
        var content = PlanViewerTitleClipboardContent.Create(
            plan, "Group title", @"C:\Workspace with spaces\.squad");

        Assert.Multiple(() =>
        {
            Assert.That(content.Name, Is.EqualTo("Friendly alias"));
            Assert.That(content.FilePath, Is.EqualTo(
                @"C:\Workspace with spaces\.squad\plans\PLAN-42.json"));
            Assert.That(Path.IsPathFullyQualified(content.FilePath!), Is.True);
        });
    }

    [Test]
    public void RelativeWorkspace_IsNormalizedToAbsolutePlanPath()
    {
        var folder = Path.Combine("Title menu workspace", "discard", "..", ".squad");
        var expected = Path.Combine(Directory.GetCurrentDirectory(),
            "Title menu workspace", ".squad", "plans", "PLAN-42.json");

        var content = PlanViewerTitleClipboardContent.Create(BuildPlan(), "Group title", folder);

        Assert.Multiple(() =>
        {
            Assert.That(content.FilePath, Is.EqualTo(expected));
            Assert.That(Path.IsPathFullyQualified(content.FilePath!), Is.True);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t ")]
    public void MissingWorkspace_DisablesPathWithoutLosingName(string? folder)
    {
        var content = PlanViewerTitleClipboardContent.Create(BuildPlan(), "Group title", folder);

        Assert.Multiple(() =>
        {
            Assert.That(content.Name, Is.EqualTo("Original plan title"));
            Assert.That(content.FilePath, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t ")]
    public void MissingPlanId_DisablesPathWithoutLosingName(string? planId)
    {
        var plan = BuildPlan() with { PlanId = planId! };
        var content = PlanViewerTitleClipboardContent.Create(
            plan, "Group title", @"C:\Workspace with spaces\.squad");

        Assert.Multiple(() =>
        {
            Assert.That(content.Name, Is.EqualTo("Original plan title"));
            Assert.That(content.FilePath, Is.Null);
        });
    }

    private static Plan BuildPlan() =>
        new("PLAN-42", "revision", PlanSource.DecomposeDecision, PlanLifecycleStatus.Staged,
            "Original plan title", "feature/plan", "Summary", [], [],
            new PlanProgress(0, 0, null), new PlanTimestamps(DateTimeOffset.UnixEpoch));

    private static string BuildContent() =>
        Method(ReadSource("PlanViewerWindow.cs"), "private void BuildContent(",
            "private PlanTaskCorrectionActivityState ResolveTaskCorrectionActivity(");

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source[start..end];
    }

    private static string ReadSource(string fileName)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SquadDash", fileName);
            if (File.Exists(path))
                return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        throw new FileNotFoundException($"Could not locate {fileName}.");
    }
}
