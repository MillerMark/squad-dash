const fs = require('fs');
const filePath = 'C:/Source/SquadUI/SquadDash/MainWindow.xaml.cs';
let content = fs.readFileSync(filePath, 'utf8');

// Step 1: Replace field declarations (remove 4, add registry)
const oldFields = 
`    private readonly Dictionary<string, TranscriptThreadState> _agentThreadsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TranscriptThreadState> _agentThreadsByToolCallId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BackgroundAgentLaunchInfo> _agentLaunchesByToolCallId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _backgroundReportPromotionGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TranscriptThreadState> _agentThreadOrder = [];`;

const newFields =
`    private readonly Dictionary<string, int> _backgroundReportPromotionGenerations = new(StringComparer.OrdinalIgnoreCase);
    private AgentThreadRegistry _agentThreadRegistry = null!;`;

if (!content.includes(oldFields)) {
    console.error('ERROR: Could not find field declarations to replace!');
    process.exit(1);
}
content = content.replace(oldFields, newFields);
console.log('Step 1: Field declarations replaced');

// Step 2: Replace field references
content = content.replace(/_agentThreadsByKey\b/g, '_agentThreadRegistry.ThreadsByKey');
content = content.replace(/_agentThreadsByToolCallId\b/g, '_agentThreadRegistry.ThreadsByToolCallId');
content = content.replace(/_agentLaunchesByToolCallId\b/g, '_agentThreadRegistry.LaunchesByToolCallId');
content = content.replace(/_agentThreadOrder\b/g, '_agentThreadRegistry.ThreadOrder');
console.log('Step 2: Field references replaced');

// Step 3: Replace the clear block
const oldClear =
`        _agentThreadRegistry.ThreadsByKey.Clear();
        _agentThreadRegistry.ThreadsByToolCallId.Clear();
        _agentThreadRegistry.LaunchesByToolCallId.Clear();
        _backgroundReportPromotionGenerations.Clear();
        _agentThreadRegistry.ThreadOrder.Clear();`;
const newClear =
`        _agentThreadRegistry.ClearAll();
        _backgroundReportPromotionGenerations.Clear();`;
if (content.includes(oldClear)) {
    content = content.replace(oldClear, newClear);
    console.log('Step 3: Clear block replaced');
} else {
    console.warn('WARNING: Clear block not found - may already be different');
}

// Step 4: Add constructor instantiation after InitializeComponent();
const ctorInsertPoint = '        InitializeComponent();';
const ctorCode = `        InitializeComponent();
        _agentThreadRegistry = new AgentThreadRegistry(
            beginTranscriptTurn:                   (thread, prompt) => BeginTranscriptTurn(thread, prompt),
            finalizeCurrentTurnResponse:            thread => FinalizeCurrentTurnResponse(thread),
            collapseCurrentTurnThinking:            thread => CollapseCurrentTurnThinking(thread),
            renderToolEntry:                        entry => RenderToolEntry(entry),
            updateToolSpinnerState:                 () => UpdateToolSpinnerState(),
            syncActiveToolName:                    () => SyncActiveToolName(),
            syncThreadChip:                         thread => SyncThreadChip(thread),
            syncTaskToolTranscriptLink:             thread => SyncTaskToolTranscriptLink(thread),
            appendText:                             (thread, text) => AppendText(thread, text),
            syncAgentCards:                         () => RefreshAgentCards(),
            syncAgentCardsWithThreads:              () => SyncAgentCardsWithThreads(),
            getKnownTeamAgentDescriptors:           () => GetKnownTeamAgentDescriptors(),
            updateTranscriptThreadBadge:            () => UpdateTranscriptThreadBadge(),
            isThreadActiveForDisplay:               thread => IsThreadActiveForDisplay(thread),
            observeBackgroundAgentActivity:         (thread, reason) => ObserveBackgroundAgentActivity(thread, reason),
            renderConversationHistory:              (thread, turns) => RenderConversationHistoryAsync(thread, turns),
            resolveBackgroundAgentDisplayLabel:     agent => ResolveBackgroundAgentDisplayLabel(agent),
            buildAgentLabel:                        thread => BuildBackgroundAgentLabel(thread));`;

if (!content.includes(ctorInsertPoint)) {
    console.error('ERROR: Could not find InitializeComponent() for constructor insertion!');
    process.exit(1);
}
content = content.replace(ctorInsertPoint, ctorCode);
console.log('Step 4: Constructor instantiation added');

fs.writeFileSync(filePath, content, 'utf8');
const lineCount = content.split('\n').length;
console.log('Done. Lines:', lineCount);

// Verification
const oldRefCount = (content.match(/_agentThreadsByKey|_agentThreadsByToolCallId(?!\.)|_agentLaunchesByToolCallId/g) || []).length;
console.log('Old field refs still in file:', oldRefCount);
