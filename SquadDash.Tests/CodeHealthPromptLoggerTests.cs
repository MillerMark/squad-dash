using NUnit.Framework;
using System.IO;

namespace SquadDash.Tests;

[TestFixture]
public sealed class CodeHealthPromptLoggerTests {

    private string _testWorkspacePath = null!;
    private string _diagnosticsPath = null!;

    [SetUp]
    public void SetUp() {
        _testWorkspacePath = Path.Combine(Path.GetTempPath(), "SquadDashTests", Guid.NewGuid().ToString());
        _diagnosticsPath = Path.Combine(_testWorkspacePath, ".squad", "diagnostics", "prompts");
        Directory.CreateDirectory(_testWorkspacePath);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_testWorkspacePath))
            Directory.Delete(_testWorkspacePath, recursive: true);
    }

    [Test]
    public void LogPrompt_WhenNotInDeveloperMode_DoesNotCreateFile() {
        // This test assumes we're not running in developer mode (no squad-dash.slnx in temp folder)
        var logger = new CodeHealthPromptLogger(_testWorkspacePath);
        
        logger.LogPrompt("test-task", "Sample prompt text");

        Assert.That(Directory.Exists(_diagnosticsPath), Is.False, 
            "Diagnostics directory should not be created when not in developer mode");
    }

    [Test]
    public void SanitizeFileName_HandlesInvalidCharacters() {
        // We can't directly test the private method, but we can test the behavior
        // by checking what files are created
        var logger = new CodeHealthPromptLogger(_testWorkspacePath);
        
        // Create a task name with invalid characters
        var taskNameWithInvalidChars = "task/with\\invalid:chars*?";
        
        // The method should sanitize these to dashes
        // We would need developer mode to actually test this, so this is more of a smoke test
        Assert.DoesNotThrow(() => logger.LogPrompt(taskNameWithInvalidChars, "test"));
    }

    [Test]
    public void LogPrompt_WithMetadata_FormatsCorrectly() {
        // This test would require developer mode to verify file contents
        // For now, we just verify it doesn't throw
        var logger = new CodeHealthPromptLogger(_testWorkspacePath);
        var metadata = "Task: test-task\nTimestamp: 2025-01-01 12:00:00";
        
        Assert.DoesNotThrow(() => logger.LogPrompt("test-task", "Sample prompt", metadata));
    }

    [Test]
    public void LogPrompt_WithEmptyTaskName_DoesNotThrow() {
        var logger = new CodeHealthPromptLogger(_testWorkspacePath);
        
        Assert.DoesNotThrow(() => logger.LogPrompt("", "Sample prompt"));
        Assert.DoesNotThrow(() => logger.LogPrompt("   ", "Sample prompt"));
    }

    [Test]
    public void LogPrompt_WithNullMetadata_DoesNotThrow() {
        var logger = new CodeHealthPromptLogger(_testWorkspacePath);
        
        Assert.DoesNotThrow(() => logger.LogPrompt("test-task", "Sample prompt", null));
    }
}
