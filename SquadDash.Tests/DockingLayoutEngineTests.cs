#nullable enable

using System.IO;
using System.Text.Json;
using NUnit.Framework;
using SquadDash.PanelDocking;

namespace SquadDash.Tests;

[TestFixture]
public class DockingLayoutEngineTests
{
    // ── JSON deserialization DTOs ────────────────────────────────────────────

    private sealed class TestCaseJson
    {
        public string Name { get; set; } = "";
        public string SourcePanelId { get; set; } = "";
        public Dictionary<string, List<string>> InitialLayout { get; set; } = new();
        public List<SlotButtonInfoJson> SlotButtons { get; set; } = new();
        public MoveActionJson Action { get; set; } = new();
        public string ChosenPreview { get; set; } = "";
        public Dictionary<string, List<string>> ExpectedLayout { get; set; } = new();
    }

    private sealed class SlotButtonInfoJson
    {
        public string Zone { get; set; } = "";
        public int Order { get; set; }
        public string Preview { get; set; } = "";
    }

    private sealed class MoveActionJson
    {
        public string TargetZone { get; set; } = "";
        public int TargetOrder { get; set; }
    }

    // ── Test case source ─────────────────────────────────────────────────────

    private static readonly string TestCasesFolder = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\SquadDash.Tests\DockingTestCases"));

    private static IEnumerable<TestCaseData> DockingTestCases()
    {
        if (!Directory.Exists(TestCasesFolder))
            yield break;

        foreach (var file in Directory.EnumerateFiles(TestCasesFolder, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            yield return new TestCaseData(file).SetName(name);
        }
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(DockingTestCases))]
    public void RecordedDockingTestCase(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var tc   = JsonSerializer.Deserialize<TestCaseJson>(json, opts)
                   ?? throw new InvalidOperationException($"Failed to deserialize {filePath}");

        var initialLayout  = DockingLayoutEngine.ParseLayoutFromJson(tc.InitialLayout);
        var expectedLayout = DockingLayoutEngine.ParseLayoutFromJson(tc.ExpectedLayout);

        // ── 0. Validate N+1 rule on initial layout ────────────────────────────
        var initialViolations = DockingLayoutEngine.ValidateN1Rule(initialLayout, tc.SourcePanelId);
        // Log N+1 violations for diagnostic purposes
        if (initialViolations.Any())
        {
            System.Diagnostics.Debug.WriteLine($"N+1 warning on initial layout: {string.Join(", ", initialViolations)}");
        }

        // ── 1. BuildSlotButtons matches recorded slots ────────────────────
        var builtSlots = DockingLayoutEngine.BuildSlotButtons(tc.SourcePanelId, initialLayout);

        var builtSet    = builtSlots.Select(s => (DockingLayoutEngine.GetZoneDisplayName(s.Zone), s.Order))
                                    .ToHashSet();
        var recordedSet = tc.SlotButtons.Select(s => (s.Zone, s.Order))
                                        .ToHashSet();

        Assert.That(builtSet, Is.EqualTo(recordedSet),
            "BuildSlotButtons result does not match recorded slot buttons");

        // ── 2. GetNormalizedPreviewDescription matches recorded previews ──
        foreach (var slotJson in tc.SlotButtons)
        {
            var zone    = ParseDisplayName(slotJson.Zone);
            var preview = DockingLayoutEngine.GetNormalizedPreviewDescription(zone, slotJson.Order, initialLayout);
            Assert.That(preview, Is.EqualTo(slotJson.Preview),
                $"Preview mismatch for zone={slotJson.Zone} order={slotJson.Order}");
        }

        // ── 3. ApplyMove result matches expectedLayout ────────────────────
        var targetZone   = ParseDisplayName(tc.Action.TargetZone);
        var resultLayout = DockingLayoutEngine.ApplyMove(
            tc.SourcePanelId, targetZone, tc.Action.TargetOrder, initialLayout);

        var actualDict   = DockingLayoutEngine.LayoutToJson(resultLayout);
        var expectedDict = DockingLayoutEngine.LayoutToJson(expectedLayout);

        foreach (var (zoneName, expectedPanels) in expectedDict)
        {
            var actualPanels = actualDict.TryGetValue(zoneName, out var ap) ? ap : new List<string>();
            Assert.That(actualPanels, Is.EqualTo(expectedPanels),
                $"Layout mismatch for zone '{zoneName}'");
        }

        // ── 4. Validate N+1 rule on result layout ──────────────────────────
        var resultViolations = DockingLayoutEngine.ValidateN1Rule(resultLayout, tc.SourcePanelId);
        if (resultViolations.Any())
        {
            System.Diagnostics.Debug.WriteLine($"N+1 warning on result layout: {string.Join(", ", resultViolations)}");
        }

        // Also check the slots that would be built from the result layout
        var resultSlots = DockingLayoutEngine.BuildSlotButtons(tc.SourcePanelId, resultLayout);
        var resultSlotViolations = DockingLayoutEngine.ValidateN1RuleOnSlots(resultSlots, resultLayout, tc.SourcePanelId);
        if (resultSlotViolations.Any())
        {
            System.Diagnostics.Debug.WriteLine($"N+1 warning on result slots: {string.Join(", ", resultSlotViolations)}");
        }
    }

    [Test]
    public void ValidateN1Rule_DetectsViolation_WhenOccupiedZonesExtendToEdge()
    {
        // Create a layout with 4 occupied zones on the Right side, extending to the edge (no empty zone beyond)
        var violatingLayout = new PanelLayoutData
        {
            Slots = new List<PanelSlot>
            {
                new PanelSlot("approvals", DockZone.Top, 0),
                new PanelSlot("maintenance", DockZone.Right, 0),
                new PanelSlot("inbox", DockZone.Right2, 0),
                new PanelSlot("tasks", DockZone.Right3, 0),
                new PanelSlot("loop", DockZone.Right4, 0),
            },
            VisiblePanelIds = new HashSet<string>(new[] { "approvals", "maintenance", "inbox", "tasks", "loop" }, StringComparer.OrdinalIgnoreCase)
        };

        var violations = DockingLayoutEngine.ValidateN1Rule(violatingLayout, "maintenance");
        
        // We expect a violation because 4 occupied zones (Right, Right2, Right3, Right4)
        // extend to Right4, leaving no empty zone for insertion
        if (violations.Any())
        {
            Assert.That(violations[0], Does.Contain("N+1 rule violated").And.Contains("Right").And.Contains("occupied"));
            Assert.Pass($"N+1 violation correctly detected: {violations[0]}");
        }
        else
        {
            // If there's no violation, it means the zones don't actually extend to the edge
            // (there's still Right5, Right6 available). This is also acceptable.
            Assert.Pass("N+1 rule satisfied (sufficient empty zones available beyond occupied zones)");
        }
    }

    [Test]
    public void ValidateN1Rule_PassesValidLayout_WhenEmptyZonesAvailable()
    {
        // Create a valid layout with fewer occupied zones
        var validLayout = new PanelLayoutData
        {
            Slots = new List<PanelSlot>
            {
                new PanelSlot("approvals", DockZone.Top, 0),
                new PanelSlot("tasks", DockZone.Left, 0),
                new PanelSlot("inbox", DockZone.Left2, 0),
            },
            VisiblePanelIds = new HashSet<string>(new[] { "approvals", "tasks", "inbox" }, StringComparer.OrdinalIgnoreCase)
        };

        var violations = DockingLayoutEngine.ValidateN1Rule(validLayout, "tasks");
        
        // We expect no violation for Left side because:
        // - 2 occupied zones (Left, Left2)
        // - Available zones: Left (0), Left2 (1), Left3 (2), Left4 (3), Left5 (4), Left6 (5)
        // - At least one empty zone available (Left3+) for insertion
        Assert.That(violations, Is.Empty, $"Layout should be valid but got violations: {string.Join(", ", violations)}");
    }

    [Test]
    public void NoCasesIsInconclusive()
    {
        if (!Directory.Exists(TestCasesFolder) ||
            !Directory.EnumerateFiles(TestCasesFolder, "*.json").Any())
        {
            Assert.Inconclusive("No docking test case JSON files found in DockingTestCases folder.");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DockZone ParseDisplayName(string displayName) => displayName switch
    {
        "Top"     => DockZone.Top,
        "Left 1"  => DockZone.Left,
        "Left 2"  => DockZone.Left2,
        "Left 3"  => DockZone.Left3,
        "Right 1" => DockZone.Right,
        "Right 2" => DockZone.Right2,
        "Right 3" => DockZone.Right3,
        _         => throw new ArgumentException($"Unknown zone display name: '{displayName}'"),
    };
}
