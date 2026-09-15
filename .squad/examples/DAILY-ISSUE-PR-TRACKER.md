# Daily Issue & PR Tracker

**Task ID**: `daily-issue-pr-tracker`  
**Frequency**: Daily  
**Safety Level**: Report-only (no code changes)

## Overview

The Daily Issue & PR Tracker queries GitHub for issues and pull requests created since its last successful run, with a 10-minute overlap for indexing lag. Results are deduplicated by number. On first run (no watermark), it reports only open items created within the last 60 days. Later runs retain the watermark window even after a long gap and include closed items too.

## What It Does

1. **Queries GitHub API** - Uses GitHub CLI with the saved UTC watermark or first-run fallback
2. **Identifies the Repository** - Dynamically detects your current workspace's repository
3. **Generates a Report** - Details the newest 20 items across issues and PRs, with full counts and a "+N more — View all on GitHub" search link when truncated
4. **Delivers to Inbox** - Sends the report from Argus Weld with label- and volume-based priority

## Configuration Options

### Include Pull Requests (Checkbox)

- **Default**: Enabled (issues and PRs)
- **When enabled**: Report includes both issues and pull requests
- **When disabled**: Report includes issues only

| Option | Default | Purpose |
| --- | --- | --- |
| `maxDetailedItems` | 20 | Shared detailed-item cap; full counts and priority include undisplayed items |
| `escalateCountThreshold` | 10 | Total matching count that triggers high priority |
| `criticalLabels` | `security, critical, P0, sev1, severity: critical` | Critical label-name matches |
| `highPriorityLabels` | `priority: high, high priority, urgent, P1, important` | High-priority label-name matches |

### Inbox Priority

Rules apply in order: **low** for no new items; **critical** if any item matches a critical label; **high** if any item matches a high-priority label or the total reaches the count threshold; otherwise **mid**. Label lists are comma-separated and matched case-insensitively against label names (including substrings). GitHub has no native priority field, so labels provide priority signals. Escalated reports name the triggering labels or count, including matches outside the detailed list.

## Report Format

Each report includes:

- Repository and effective UTC window: "since <timestamp>" (including overlap), or "open issues, last 60 days (first run)". PRs use the same window when enabled.

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

1. Open the Code Health panel.
2. Find **Issue & PR Tracker** (`daily-issue-pr-tracker`).
3. Enable it if disabled (the shipped task is enabled by default).

### Configure Options

Adjust the task options in the Code Health panel. Task-definition option values use this shape:

```yaml
options:
  includePullRequests:
    value: true  # Set to false for issues only
  maxDetailedItems:
    value: 20
  escalateCountThreshold:
    value: 10
```

### Run Manually

Run the task from the Code Health panel to use its persisted watermark and configured options outside the daily schedule.

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

The built-in task ships with SquadDash; no repository-specific query configuration is required.

## Example Output

```
## Daily Issue & PR Tracker Report

**Repository**: [MillerMark/squad-dash](https://github.com/MillerMark/squad-dash)
**Period**: Since 2026-06-02T15:00:00Z (last successful run minus 10 minutes)
**Priority**: mid

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
