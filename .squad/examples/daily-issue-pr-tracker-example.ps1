<#
.SYNOPSIS
Example implementation of the Daily Issue & PR Tracker maintenance task.

This script demonstrates how the daily-issue-pr-tracker maintenance task would work
in practice. It queries the GitHub API to find new issues (and optionally PRs) posted
to a repository in the last 24 hours.

.PARAMETER IncludePullRequests
If $true, the report will include pull requests. If $false, only issues are reported.

.EXAMPLE
.\daily-issue-pr-tracker-example.ps1 -IncludePullRequests $false
.\daily-issue-pr-tracker-example.ps1 -IncludePullRequests $true

.NOTES
This script uses GitHub CLI (gh). Make sure you have it installed and authenticated.
#>

param(
    [bool]$IncludePullRequests = $false
)

$ErrorActionPreference = "Stop"

# Get the current repository
try {
    $repoInfo = gh repo view --json nameWithOwner,owner,name --jq '.nameWithOwner,.owner.login,.name'
    $repoNameWithOwner = $repoInfo[0]
    $owner = $repoInfo[1]
    $repo = $repoInfo[2]
    Write-Host "Repository: $repoNameWithOwner" -ForegroundColor Gray
}
catch {
    Write-Host "Error: Unable to determine repository. Make sure you're in a GitHub repository." -ForegroundColor Red
    exit 1
}

# Calculate 24 hours ago
$twentyFourHoursAgo = (Get-Date).AddHours(-24).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Query for new issues in the last 24 hours
Write-Host "`nQuerying for new issues since $twentyFourHoursAgo..." -ForegroundColor Gray
$issuesQuery = "repo:$repoNameWithOwner is:issue created:>$twentyFourHoursAgo"
$issues = gh issue list --repo $repoNameWithOwner --search $issuesQuery --json number,title,createdAt,url --limit 100

# Query for new PRs if enabled
$pullRequests = @()
if ($IncludePullRequests) {
    Write-Host "Querying for new pull requests since $twentyFourHoursAgo..." -ForegroundColor Gray
    $prsQuery = "repo:$repoNameWithOwner is:pr created:>$twentyFourHoursAgo"
    $pullRequests = gh pr list --repo $repoNameWithOwner --search $prsQuery --json number,title,createdAt,url --limit 100
}

# Convert JSON responses to objects
$issuesData = if ($issues) { $issues | ConvertFrom-Json } else { @() }
$prsData = if ($pullRequests) { $pullRequests | ConvertFrom-Json } else { @() }

# Format the report
$report = @()
$report += "## Daily Issue & PR Tracker Report"
$report += ""
$report += "**Repository**: [$repoNameWithOwner](https://github.com/$repoNameWithOwner)"
$report += "**Period**: Last 24 hours (since $twentyFourHoursAgo)"
$report += ""

# Summary
$issueCount = if ($issuesData -is [array]) { $issuesData.Count } elseif ($issuesData) { 1 } else { 0 }
$prCount = if ($prsData -is [array]) { $prsData.Count } elseif ($prsData) { 1 } else { 0 }
$report += "### Summary"
$report += "- **New Issues**: $issueCount"
if ($IncludePullRequests) {
    $report += "- **New Pull Requests**: $prCount"
}
$report += ""

# New Issues
if ($issueCount -gt 0) {
    $report += "### New Issues"
    $issuesArray = if ($issuesData -is [array]) { $issuesData } else { @($issuesData) }
    foreach ($issue in $issuesArray) {
        $createdAt = [datetime]::Parse($issue.createdAt).ToString("yyyy-MM-dd HH:mm UTC")
        $report += "- [#$($issue.number) $($issue.title)]($($issue.url)) (created: $createdAt)"
    }
    $report += ""
}
else {
    $report += "### New Issues"
    $report += "No new issues in the last 24 hours."
    $report += ""
}

# New Pull Requests
if ($IncludePullRequests) {
    if ($prCount -gt 0) {
        $report += "### New Pull Requests"
        $prsArray = if ($prsData -is [array]) { $prsData } else { @($prsData) }
        foreach ($pr in $prsArray) {
            $createdAt = [datetime]::Parse($pr.createdAt).ToString("yyyy-MM-dd HH:mm UTC")
            $report += "- [#$($pr.number) $($pr.title)]($($pr.url)) (created: $createdAt)"
        }
        $report += ""
    }
    else {
        $report += "### New Pull Requests"
        $report += "No new pull requests in the last 24 hours."
        $report += ""
    }
}

# Suggested Next Steps
$report += "### Suggested Next Steps"
if ($issueCount -eq 0 -and $prCount -eq 0) {
    $report += "✓ No action needed. The repository is quiet."
}
else {
    $report += "1. **Review new issues** - Assign labels, milestones, and assignees as appropriate"
    if ($IncludePullRequests) {
        $report += "2. **Review new PRs** - Check CI status and provide early feedback"
    }
    $report += "3. **Prioritize** - Identify any critical or urgent items that need immediate attention"
}
$report += ""

# Output the report
$report | ForEach-Object { Write-Host $_ }

# Output JSON for Inbox integration
$inboxMessage = @{
    subject = "Maintenance Report: Daily Issue & PR Tracker"
    from = "argus-weld"
    body = $report -join "`n"
    attachments = @()
} | ConvertTo-Json -Depth 10

Write-Host "`n" -ForegroundColor Gray
Write-Host "INBOX_MESSAGE_JSON:" -ForegroundColor Gray
Write-Host $inboxMessage -ForegroundColor Gray
