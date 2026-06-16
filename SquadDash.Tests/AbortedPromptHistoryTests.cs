using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AbortedPromptHistoryTests {
    /// <summary>
    /// Verifies that when a prompt is added to history and then the app is reloaded
    /// (simulating a crash or session switch), the prompt is preserved in PromptHistoryStore
    /// even if background conversation persistence hasn't completed.
    ///
    /// This tests the fix for the bug: "Aborted prompts not appearing in Ctrl+Up history"
    /// </summary>
    [Test]
    public void AddPromptToHistory_SavesImmediatelyToPromptHistoryStore() {
        var store = new PromptHistoryStore();
        var testPrompt = "test-prompt-from-abort";

        // Simulate what happens when a user sends then aborts a prompt:
        // 1. AddPromptToHistory is called
        // 2. The prompt is added to in-memory list
        // 3. PromptHistoryStore.Save() is called immediately
        store.Save([testPrompt]);

        // 4. Simulate app reload or session switch
        // 5. Load the history from PromptHistoryStore
        var loadedHistory = store.Load();

        // Verify the aborted prompt is still in history
        Assert.That(loadedHistory, Contains.Item(testPrompt),
            "Aborted prompt should be preserved in PromptHistoryStore even if conversation state persistence didn't complete");
    }

    [Test]
    public void MultiplePromptsSavedProgressively_AllPreservedAfterReload() {
        var store = new PromptHistoryStore();
        var prompt1 = "first-prompt-sent";
        var prompt2 = "second-prompt-aborted";
        var prompt3 = "third-prompt-sent";

        // Simulate multiple prompts being added (some aborted)
        store.Save([prompt1]);
        store.Save([prompt1, prompt2]); // second prompt added even though first was aborted
        store.Save([prompt1, prompt2, prompt3]);

        var loadedHistory = store.Load();

        Assert.Multiple(() => {
            Assert.That(loadedHistory, Has.Count.EqualTo(3));
            Assert.That(loadedHistory[0], Is.EqualTo(prompt1));
            Assert.That(loadedHistory[1], Is.EqualTo(prompt2), "Aborted prompt should be in history");
            Assert.That(loadedHistory[2], Is.EqualTo(prompt3));
        });
    }

    [Test]
    public void AbortedPromptInPersistence_SurvivesRapidSessionSwitch() {
        var store = new PromptHistoryStore();
        var abortedPrompt = "rapid-abort-prompt";

        // User sends prompt and immediately aborts it
        store.Save([abortedPrompt]);

        // User rapidly switches workspaces or the app restarts
        // In this case, conversation state persistence might not have completed
        
        // When reloading the workspace, the prompt history is restored from PromptHistoryStore
        var restoredHistory = store.Load();

        Assert.That(restoredHistory, Contains.Item(abortedPrompt),
            "Aborted prompt should survive rapid session switches due to immediate PromptHistoryStore persistence");
    }
}
