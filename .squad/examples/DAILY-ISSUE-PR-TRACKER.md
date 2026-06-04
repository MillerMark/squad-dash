# Daily Issue & PR Tracker

**Task ID**: `daily-issue-pr-tracker`  
**Frequency**: Daily  
**Safety Level**: Report-only (no code changes)

## Overview

The Daily Issue & PR Tracker is a maintenance task that automatically queries GitHub for new issues and pull requests posted to your repository in the last 24 hours. It provides a structured report delivered to your Inbox panel, helping you stay on top of incoming activity.

## What It Does

1. **Queries GitHub API** - Uses GitHub CLI to search for issues/PRs created in the last 24 hours
2. **Identifies the Repository** - Dynamically detects your current workspace's repository
3. **Generates a Report** - Formats findings as a clear, actionable markdown report
4. **Delivers to Inbox** - Sends the report to your SquadDash Inbox for easy access

## Configuration Options

### Include Pull Requests (Checkbox)

- **Default**: Disabled (issues only)
- **When enabled**: Report includes both issues and pull requests
- **When disabled**: Report includes issues only

## Report Format

Each report includes:

### Summary
- Count of new issues (and PRs if enabled)

### New Issues
- Issue number and title
- Creation timestamp (UTC)
- Direct link to GitHub

### New Pull Requests (when enabled)
- PR number and title
- Creation timestamp (UTC)
- Direct link to GitHub

### Suggested Next Steps
- Actionable recommendations for triage/review
- Prioritization suggestions

## Usage

### Enable the Task

1. Open `.squad/maintenance.md`
2. Find the `daily-issue-pr-tracker` task
3. Change `enabled: false` to `enabled: true`

### Configure Options

In the same task definition, adjust the `options` section:

```yaml
options:
  includePullRequests:
    value: false  # Set to true to include PRs
```

### Run Manually

To test the task or run it outside the maintenance schedule:

```powershell
# From the repository root
.\.squad\examples\daily-issue-pr-tracker-example.ps1 -IncludePullRequests $false
```

## Requirements

- GitHub CLI (`gh`) must be installed and authenticated
- You must have read access to the repository
- The task runs in your current workspace's repository context

## Distribution to Other Repos

This task is designed to be **repo-agnostic** and can be distributed to any GitHub repository. It:

- Dynamically detects the current repository using `gh repo view`
- Works with any repository size or structure
- Requires no repo-specific configuration
- Respects the safety level (`report-only`)

Simply copy the task definition from `.squad/maintenance.md` to another repository's maintenance configuration, and it will work immediately.

## Example Output

```
## Daily Issue & PR Tracker Report

**Repository**: [MillerMark/squad-dash](https://github.com/MillerMark/squad-dash)
**Period**: Last 24 hours

### Summary
- **New Issues**: 2
- **New Pull Requests**: 1

### New Issues
- [#42 Add type safety to components](https://github.com/MillerMark/squad-dash/issues/42) (created: 2026-06-03 09:30 UTC)
- [#43 Document API endpoints](https://github.com/MillerMark/squad-dash/issues/43) (created: 2026-06-03 14:15 UTC)

### New Pull Requests
- [#44 Fix authentication flow](https://github.com/MillerMark/squad-dash/pull/44) (created: 2026-06-03 11:20 UTC)

### Suggested Next Steps
1. **Review new issues** - Assign labels, milestones, and assignees as appropriate
2. **Review new PRs** - Check CI status and provide early feedback
3. **Prioritize** - Identify any critical or urgent items that need immediate attention
```

## Troubleshooting

### "Error: Unable to determine repository"
- Ensure you're running the task from within a GitHub repository
- Verify GitHub CLI is installed: `gh --version`
- Verify authentication: `gh auth status`

### No issues/PRs reported despite recent activity
- Check the GitHub API search filters are working
- Verify issues/PRs are public (if querying a public repo)
- Note: Searches use UTC timezone

### Report doesn't include recent issues/PRs
- GitHub's search index may have a slight delay (typically < 1 minute)
- Try running the task again after a minute

## See Also

- [Maintenance Mode Documentation](../../docs/features/maintenance-mode.md)
- [GitHub CLI Documentation](https://cli.github.com/)
