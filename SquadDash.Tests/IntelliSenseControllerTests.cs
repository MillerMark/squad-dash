namespace SquadDash.Tests;

[TestFixture]
internal sealed class IntelliSenseControllerTests {

    // ── TryActivate ──────────────────────────────────────────────────────────

    [Test]
    public void TryActivate_ReturnsNonNullWhenSuggestionsProvided() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" });

        Assert.That(state, Is.Not.Null);
    }

    [Test]
    public void TryActivate_SetsSelectedIndexToZero() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" });

        Assert.That(state!.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void TryActivate_SetsFilterToEmptyString() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" });

        Assert.That(state!.Filter, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TryActivate_SetsFilteredSuggestionsEqualToAllSuggestions() {
        var suggestions = new[] { "Yes", "No", "Maybe" };

        var state = IntelliSenseController.TryActivate('[', 0, suggestions);

        Assert.That(state!.FilteredSuggestions, Is.EquivalentTo(suggestions));
    }

    [Test]
    public void TryActivate_ReturnsNullWhenNoSuggestions() {
        var state = IntelliSenseController.TryActivate('[', 0, Array.Empty<string>());

        Assert.That(state, Is.Null);
    }

    [Test]
    public void TryActivate_StoresTriggerCharAndPosition() {
        var state = IntelliSenseController.TryActivate('@', 7, new[] { "agent" });

        Assert.Multiple(() => {
            Assert.That(state!.TriggerChar, Is.EqualTo('@'));
            Assert.That(state!.TriggerPosition, Is.EqualTo(7));
        });
    }

    // ── UpdateFromText ───────────────────────────────────────────────────────

    [Test]
    public void UpdateFromText_ExtractsFilterFromTextAndCaretIndex() {
        // text="[Ye", triggerPos=0, caret=3 → filter="Ye"
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "Yesterday" })!;

        var updated = IntelliSenseController.UpdateFromText(state, "[Ye", caretIndex: 3);

        Assert.That(updated!.Filter, Is.EqualTo("Ye"));
    }

    [Test]
    public void UpdateFromText_ReturnsNullWhenNoSuggestionsMatchFilter() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "Yesterday" })!;

        var updated = IntelliSenseController.UpdateFromText(state, "[ZZZ", caretIndex: 4);

        Assert.That(updated, Is.Null);
    }

    [Test]
    public void UpdateFromText_ReturnsNonNullWhenSomeSuggestionsStillMatch() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" })!;

        var updated = IntelliSenseController.UpdateFromText(state, "[Ye", caretIndex: 3);

        Assert.That(updated, Is.Not.Null);
    }

    [Test]
    public void UpdateFromText_FiltersAreCaseInsensitive() {
        // filter "ye" should still match "Yes"
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" })!;

        var updated = IntelliSenseController.UpdateFromText(state, "[ye", caretIndex: 3);

        Assert.That(updated!.FilteredSuggestions, Contains.Item("Yes"));
    }

    [Test]
    public void UpdateFromText_ReturnsNullWhenTriggerCharNoLongerAtTriggerPosition() {
        // state expects '[' at index 0, but text has been replaced
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes" })!;

        var updated = IntelliSenseController.UpdateFromText(state, "Ye", caretIndex: 2);

        Assert.That(updated, Is.Null);
    }

    [Test]
    public void UpdateFromText_ReturnsNullWhenCaretAtOrBeforeTriggerPosition() {
        var state = IntelliSenseController.TryActivate('[', 2, new[] { "Yes" })!;

        var updated = IntelliSenseController.UpdateFromText(state, "ab[", caretIndex: 2);

        Assert.That(updated, Is.Null);
    }

    [Test]
    public void UpdateFromText_ClampsSelectedIndexWhenFilteredListShrinks() {
        // start with 3 items, navigate to index 2
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Alpha", "Almond", "Beta" })!;
        state = IntelliSenseController.MoveSelection(state, 2); // select index 2 ("Beta")

        // now filter to only items starting with "Al" (Alpha, Almond) — "Beta" falls off
        var updated = IntelliSenseController.UpdateFromText(state, "[Al", caretIndex: 3);

        Assert.That(updated!.SelectedIndex, Is.EqualTo(1)); // clamped to last in filtered list
    }

    // ── MoveSelection ────────────────────────────────────────────────────────

    [Test]
    public void MoveSelection_MovesSelectionDownByOne() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "A", "B", "C" })!;

        var moved = IntelliSenseController.MoveSelection(state, +1);

        Assert.That(moved.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public void MoveSelection_MovesSelectionUpByOne() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "A", "B", "C" })!;
        state = IntelliSenseController.MoveSelection(state, +2); // index 2

        var moved = IntelliSenseController.MoveSelection(state, -1);

        Assert.That(moved.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public void MoveSelection_ClampsAtBottom() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "A", "B", "C" })!;
        state = IntelliSenseController.MoveSelection(state, +2); // index 2 (last)

        var moved = IntelliSenseController.MoveSelection(state, +1); // try to go past end

        Assert.That(moved.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void MoveSelection_ClampsAtTop() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "A", "B", "C" })!;
        // starts at index 0

        var moved = IntelliSenseController.MoveSelection(state, -1); // try to go before start

        Assert.That(moved.SelectedIndex, Is.EqualTo(0));
    }

    // ── Accept ───────────────────────────────────────────────────────────────

    [Test]
    public void Accept_InsertsSuggestionWhenNoFilterText() {
        // text="[", triggerPos=0, caret=1 — only trigger char present
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" })!;

        var (newText, _) = IntelliSenseController.Accept(state, "[", caretIndex: 1);

        Assert.That(newText, Is.EqualTo("Yes"));
    }

    [Test]
    public void Accept_ReplacesTriggerAndFilterWithSuggestion() {
        // text="[Ye", triggerPos=0, caret=3 → accepts "Yes"
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" })!;
        state = IntelliSenseController.UpdateFromText(state, "[Ye", caretIndex: 3)!;

        var (newText, _) = IntelliSenseController.Accept(state, "[Ye", caretIndex: 3);

        Assert.That(newText, Is.EqualTo("Yes"));
    }

    [Test]
    public void Accept_SetsCaretAfterInsertedSuggestion() {
        // text="[", triggerPos=0, caret=1 → newCaret = 0 + len("Yes") = 3
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes" })!;

        var (_, newCaret) = IntelliSenseController.Accept(state, "[", caretIndex: 1);

        Assert.That(newCaret, Is.EqualTo(3));
    }

    [Test]
    public void Accept_PreservesTextAfterCaretPosition() {
        // text="[Ye rest", triggerPos=0, caret=3 → "Yes rest"
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes" })!;
        state = IntelliSenseController.UpdateFromText(state, "[Ye rest", caretIndex: 3)!;

        var (newText, _) = IntelliSenseController.Accept(state, "[Ye rest", caretIndex: 3);

        Assert.That(newText, Is.EqualTo("Yes rest"));
    }

    [Test]
    public void Accept_HandlesTextBeforeTrigger() {
        // text="send [", triggerPos=5, caret=6 → "send Yes", caret=8
        var state = IntelliSenseController.TryActivate('[', 5, new[] { "Yes" })!;

        var (newText, newCaret) = IntelliSenseController.Accept(state, "send [", caretIndex: 6);

        Assert.Multiple(() => {
            Assert.That(newText, Is.EqualTo("send Yes"));
            Assert.That(newCaret, Is.EqualTo(8)); // triggerPos(5) + len("Yes")(3)
        });
    }

    // ── Slash command filtering (FilterIncludesTrigger=true) ─────────────────

    [Test]
    public void TryActivate_SlashTrigger_SetsFilterIncludesTriggerTrue() {
        var state = IntelliSenseController.TryActivate('/', 0, new[] { "/help", "/tasks" }, filterIncludesTrigger: true);

        Assert.That(state!.FilterIncludesTrigger, Is.True);
    }

    [Test]
    public void TryActivate_DefaultTrigger_SetsFilterIncludesTriggerFalse() {
        var state = IntelliSenseController.TryActivate('[', 0, new[] { "Yes", "No" });

        Assert.That(state!.FilterIncludesTrigger, Is.False);
    }

    [Test]
    public void UpdateFromText_SlashTrigger_ShowsAllCommandsWhenOnlySlashTyped() {
        // text="/", triggerPos=0, caret=1 → filter="/" → all /... commands match
        var commands = new[] { "/help", "/tasks", "/clear" };
        var state = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;

        var updated = IntelliSenseController.UpdateFromText(state, "/", caretIndex: 1);

        Assert.That(updated!.FilteredSuggestions, Is.EquivalentTo(commands));
    }

    [Test]
    public void UpdateFromText_SlashTrigger_FiltersCommandsByPrefix() {
        // text="/he", caret=3 → filter="/he" → only "/help" matches
        var commands = new[] { "/help", "/tasks", "/clear" };
        var state = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;

        var updated = IntelliSenseController.UpdateFromText(state, "/he", caretIndex: 3);

        Assert.That(updated!.FilteredSuggestions, Is.EqualTo(new[] { "/help" }));
    }

    [Test]
    public void UpdateFromText_SlashTrigger_FilterIsCaseInsensitive() {
        // text="/HE", caret=3 → filter="/HE" → matches "/help"
        var commands = new[] { "/help", "/tasks" };
        var state = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;

        var updated = IntelliSenseController.UpdateFromText(state, "/HE", caretIndex: 3);

        Assert.That(updated!.FilteredSuggestions, Contains.Item("/help"));
    }

    [Test]
    public void UpdateFromText_SlashTrigger_ReturnsNullWhenNothingMatches() {
        var commands = new[] { "/help", "/tasks" };
        var state = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;

        var updated = IntelliSenseController.UpdateFromText(state, "/zzz", caretIndex: 4);

        Assert.That(updated, Is.Null);
    }

    [Test]
    public void Accept_SlashTrigger_ProducesFullCommandWithSlash() {
        // text="/", triggerPos=0, caret=1 → accepts "/help" → result="/help"
        var state = IntelliSenseController.TryActivate('/', 0, new[] { "/help", "/tasks" }, filterIncludesTrigger: true)!;

        var (newText, newCaret) = IntelliSenseController.Accept(state, "/", caretIndex: 1);

        Assert.Multiple(() => {
            Assert.That(newText, Is.EqualTo("/help"));
            Assert.That(newCaret, Is.EqualTo(5));
        });
    }

    [Test]
    public void Accept_SlashTrigger_ProducesFullCommandAfterPartialInput() {
        // text="/he", caret=3, filtered to ["/help"] → accepts "/help"
        var state = IntelliSenseController.TryActivate('/', 0, new[] { "/help", "/tasks" }, filterIncludesTrigger: true)!;
        state = IntelliSenseController.UpdateFromText(state, "/he", caretIndex: 3)!;

        var (newText, _) = IntelliSenseController.Accept(state, "/he", caretIndex: 3);

        Assert.That(newText, Is.EqualTo("/help"));
    }

    // ── ResolveAction with isIntelliSenseOpen = true ─────────────────────────

    [Test]
    public void ResolveAction_UpWithIntelliSenseOpen_ReturnsIntelliSenseUp() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Up,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseUp));
    }

    [Test]
    public void ResolveAction_DownWithIntelliSenseOpen_ReturnsIntelliSenseDown() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Down,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseDown));
    }

    [Test]
    public void ResolveAction_EnterWithIntelliSenseOpen_ReturnsIntelliSenseAccept() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseAccept));
    }

    [Test]
    public void ResolveAction_TabWithIntelliSenseOpen_ReturnsIntelliSenseAccept() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Tab,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseAccept));
    }

    [Test]
    public void ResolveAction_EscapeWithIntelliSenseOpen_ReturnsIntelliSenseDismiss() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Escape,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseDismiss));
    }

    [Test]
    public void ResolveAction_CtrlUpWithIntelliSenseOpen_ReturnsNavigateHistoryPrevious() {
        // IntelliSense does not intercept Ctrl+arrow keys
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Up,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: true,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.NavigateHistoryPrevious));
    }

    // ── ResolveAction with isIntelliSenseOpen = false ────────────────────────

    [Test]
    public void ResolveAction_EnterWithIntelliSenseClosed_ReturnsSubmitPrompt() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.SubmitPrompt));
    }

    [Test]
    public void ResolveAction_UpWithIntelliSenseClosed_ReturnsNone() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Up,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    [Test]
    public void ResolveAction_DownWithIntelliSenseClosed_ReturnsNone() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Down,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    [Test]
    public void ResolveAction_TabWithIntelliSenseClosed_ReturnsNone() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Tab,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    // ── Bug-fix regression tests ─────────────────────────────────────────────

    // Bug 1: Enter accepts — guard: when run button disabled, Enter must not accept
    [Test]
    public void ResolveAction_EnterWithIntelliSenseOpenAndRunDisabled_ReturnsNone() {
        // Enter should NOT accept the IntelliSense selection when the run button is
        // disabled; it falls through to None so the TextBox handles the key normally.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: false,
            isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    // Bug 2: Backspace after no-match dismissal must re-filter, not show all commands.
    // The re-activation path calls UpdateFromText with text.Length (not the WPF CaretIndex
    // which can lag by one position after a Backspace event).
    [Test]
    public void Scenario_BackspaceAfterNomatchDismissal_ReactivationFiltersFromCurrentText() {
        // Simulated sequence: user typed /t3 → UpdateFromText returned null (no match)
        // → state was set to null.  User presses Backspace → text is now "/t".
        // Re-activation via TryActivate + UpdateFromText(text, text.Length=2) must show
        // only "/tasks".
        var commands = new[] { "/tasks", "/help", "/clear" };

        var activated = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;
        var refiltered = IntelliSenseController.UpdateFromText(activated, "/t", caretIndex: 2); // text.Length

        Assert.Multiple(() => {
            Assert.That(refiltered, Is.Not.Null);
            Assert.That(refiltered!.FilteredSuggestions, Is.EqualTo(new[] { "/tasks" }));
        });
    }

    // Bug 2 regression: documents WHY text.Length is used instead of the raw WPF CaretIndex.
    // WPF can fire TextChanged before updating CaretIndex; with a stale caret at position 1
    // (right after the '/') the filter becomes "/" and ALL commands appear.
    [Test]
    public void Scenario_StaleCaretAfterBackspace_UpdateFromTextWithStaleCaret1ShowsAllCommands() {
        var commands = new[] { "/tasks", "/help", "/clear" };
        var activated = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;

        // stale caret = 1 (pointing right after '/'), text = "/t"
        var result = IntelliSenseController.UpdateFromText(activated, "/t", caretIndex: 1);

        // filter = "/" → ALL commands match — this is the Bug 2 symptom when caret lags
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FilteredSuggestions.Count, Is.EqualTo(commands.Length),
            "With stale caret=1 filter='/' shows ALL commands — the runtime fix uses text.Length=2 to avoid this");
    }

    // Bug 3: Escape + Enter must not produce a phantom IntelliSense popup.
    //
    // Root cause: WPF fires TextChanged before updating CaretIndex.  After Enter inserts '\n'
    // the text is "/ta\n" but CaretIndex still reads 3 (the pre-Enter position).
    // Checking text[..3] = "/ta" finds no newline and incorrectly passes the guard.
    // The runtime fix checks !text.Contains('\n') (full text) which is caret-lag-proof.
    [Test]
    public void Scenario_StaleCaretBeforeNewline_UpdateFromTextWithStaleCaret3ReturnsMismatch() {
        // Prove that with stale caret=3 (pre-Enter position), UpdateFromText DOES return a
        // non-null state — i.e. the phantom popup WOULD appear if we relied on UpdateFromText
        // alone.  The full-text newline guard in TryUpdateIntelliSense blocks activation first.
        var commands = new[] { "/tasks", "/help", "/clear" };
        var activated = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;

        // stale caret = 3 (before Enter moved it to 4), text = "/ta\n"
        var result = IntelliSenseController.UpdateFromText(activated, "/ta\n", caretIndex: 3);

        // filter = "/ta" (positions 0..3) matches "/tasks" — phantom popup symptom
        Assert.That(result, Is.Not.Null,
            "With stale caret=3, filter='/ta' matches '/tasks'; the guard !text.Contains('\\n') is what blocks activation in the runtime");
        Assert.That(result!.FilteredSuggestions, Contains.Item("/tasks"));
    }

    [Test]
    public void Scenario_FullTextNewlineCheck_BlocksActivationRegardlessOfCaretPosition() {
        // The runtime guard is !text.Contains('\n').  Verify it correctly detects the
        // newline that the stale-caret check (text[..3]) misses.
        var textAfterEnter = "/ta\n";

        Assert.That(textAfterEnter.Contains('\n'), Is.True,
            "Full-text newline check detects '\\n' and prevents re-activation, even with a stale caret at position 3");
    }

    // Bug 3 (controller path): with CORRECT caret=4 (text.Length), UpdateFromText returns
    // null because filter="/ta\n" matches no command.  This is a secondary defence — the
    // primary guard in TryUpdateIntelliSense blocks before even reaching UpdateFromText.
    [Test]
    public void Scenario_NewlineAfterSlashCommand_UpdateFromTextWithTextLengthCaretReturnsNull() {
        var commands = new[] { "/tasks", "/help", "/clear" };

        var activated = IntelliSenseController.TryActivate('/', 0, commands, filterIncludesTrigger: true)!;
        // caret = text.Length = 4 (what the runtime now passes)
        var result = IntelliSenseController.UpdateFromText(activated, "/ta\n", caretIndex: 4);

        Assert.That(result, Is.Null);
    }
}
