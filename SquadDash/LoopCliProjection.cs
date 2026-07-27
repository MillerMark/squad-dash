using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SquadDash;

/// <summary>
/// Builds the temporary, Squad-CLI-compatible form of a SquadDash loop file.
/// The source loop remains untouched so native mode can retain fractional timing.
/// </summary>
internal sealed class LoopCliProjection : IDisposable
{
    private const string ProjectionFilePattern = "loop-cli-*.md";

    private LoopCliProjection(string sourcePath, string filePath)
    {
        SourcePath = sourcePath;
        FilePath = filePath;
    }

    public string SourcePath { get; }
    public string FilePath { get; }

    public static LoopCliProjection Create(
        string sourcePath,
        LoopMdConfig config,
        string workspacePath,
        string? filterText,
        IReadOnlyList<string>? featureGroups,
        string? projectionDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["copilot_trailer"] = LoopController.CopilotTrailer,
            ["workspace_path"] = workspacePath,
            ["build_command"] = LoopController.DetectBuildCommand(workspacePath),
            ["feature_groups"] = LoopController.BuildFeatureGroupsBlock(featureGroups),
            ["[**FILTER**]"] = LoopMdParser.BuildFilterInstruction(filterText),
        };

        var body = LoopMdParser.BuildMergedBody(config, substitutions);
        var cliConfig = config with
        {
            IntervalMinutes = NormalizeCliMinutes(config.IntervalMinutes, nameof(config.IntervalMinutes)),
            TimeoutMinutes = NormalizeCliMinutes(config.TimeoutMinutes, nameof(config.TimeoutMinutes)),
        };
        var content = LoopMdParser.BuildMergedFull(cliConfig, body);

        var directory = projectionDirectory ?? Path.Combine(
            SquadDashPaths.WorkspaceStateDirectory(workspacePath),
            "loop-cli");
        Directory.CreateDirectory(directory);
        DeleteStaleProjections(directory);

        var filePath = Path.Combine(directory, $"loop-cli-{Guid.NewGuid():N}.md");
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        SquadDashTrace.Write(
            "Loop",
            $"Created Squad CLI loop projection source={sourcePath} projection={filePath} " +
            $"interval={cliConfig.IntervalMinutes.ToString(CultureInfo.InvariantCulture)} " +
            $"timeout={cliConfig.TimeoutMinutes.ToString(CultureInfo.InvariantCulture)}");
        return new LoopCliProjection(sourcePath, filePath);
    }

    internal static int NormalizeCliMinutes(double minutes, string parameterName = "minutes")
    {
        if (!double.IsFinite(minutes) || minutes <= 0)
            throw new ArgumentOutOfRangeException(parameterName, minutes, "Loop timing must be a positive finite number of minutes.");

        var rounded = Math.Ceiling(minutes);
        if (rounded > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, minutes, "Loop timing is too large for Squad CLI.");

        return Math.Max(1, checked((int)rounded));
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            SquadDashTrace.Write("Loop", $"Deleted Squad CLI loop projection path={FilePath}");
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Loop", $"Failed to delete Squad CLI loop projection path={FilePath}: {ex.Message}");
        }
    }

    private static void DeleteStaleProjections(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, ProjectionFilePattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(path);
                SquadDashTrace.Write("Loop", $"Deleted stale Squad CLI loop projection path={path}");
            }
            catch (Exception ex)
            {
                SquadDashTrace.Write("Loop", $"Failed to delete stale Squad CLI loop projection path={path}: {ex.Message}");
            }
        }
    }
}
