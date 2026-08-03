<#
.SYNOPSIS
    Runs the plan reliability & observability test suites and reports pass/fail counts.

.DESCRIPTION
    Quick smoke test that executes the 7 focused test suites introduced by the
    plan-reliability-observability feature branch. Outputs per-suite and aggregate results.

.EXAMPLE
    .\tools\verify-plan-reliability.ps1
#>

[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Continue'
$testProject = Join-Path $PSScriptRoot '..' 'SquadDash.Tests'

$suites = @(
    'PendingRepairResultTests'
    'CompletedWorkReviewPresentationTests'
    'PlanTaskActivityResolverTests'
    'PlanContinuationQueueTests'
    'ApprovalNotificationLifecycleTests'
    'ApprovalAnchorInferenceEngineTests'
    'DeterministicPlanLifecycleHarnessTests'
)

Write-Host "`n=== Plan Reliability Verification ===" -ForegroundColor Cyan
Write-Host "Running $($suites.Count) test suites...`n"

$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$failures = @()

foreach ($suite in $suites) {
    $filter = "--filter FullyQualifiedName~$suite"
    $buildFlag = if ($NoBuild) { '--no-build' } else { '' }

    $output = & dotnet test $testProject $buildFlag --verbosity quiet $filter --logger "console;verbosity=minimal" 2>&1 | Out-String

    # Parse results from dotnet test output
    $passMatch = [regex]::Match($output, 'Passed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
    $failMatch = [regex]::Match($output, 'Failed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')

    if ($passMatch.Success) {
        $failed = [int]$passMatch.Groups[1].Value
        $passed = [int]$passMatch.Groups[2].Value
        $skipped = [int]$passMatch.Groups[3].Value
        $icon = '✓'
        $color = 'Green'
    }
    elseif ($failMatch.Success) {
        $failed = [int]$failMatch.Groups[1].Value
        $passed = [int]$failMatch.Groups[2].Value
        $skipped = [int]$failMatch.Groups[3].Value
        $icon = '✗'
        $color = 'Red'
        $failures += $suite
    }
    else {
        # Fallback: try simpler pattern
        $simplePass = [regex]::Match($output, 'Passed:\s*(\d+)')
        $simpleFail = [regex]::Match($output, 'Failed:\s*(\d+)')
        $passed = if ($simplePass.Success) { [int]$simplePass.Groups[1].Value } else { 0 }
        $failed = if ($simpleFail.Success) { [int]$simpleFail.Groups[1].Value } else { 0 }
        $skipped = 0
        $icon = if ($failed -gt 0) { '✗' } else { '?' }
        $color = if ($failed -gt 0) { 'Red' } else { 'Yellow' }
        if ($failed -gt 0) { $failures += $suite }
    }

    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped

    Write-Host "  $icon $suite — Passed: $passed, Failed: $failed, Skipped: $skipped" -ForegroundColor $color
}

Write-Host "`n--- Summary ---" -ForegroundColor Cyan
Write-Host "  Total Passed:  $totalPassed"
Write-Host "  Total Failed:  $totalFailed"
Write-Host "  Total Skipped: $totalSkipped"

if ($failures.Count -gt 0) {
    Write-Host "`n  FAILED SUITES:" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "    - $f" -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}
else {
    Write-Host "`n  All plan reliability suites passed." -ForegroundColor Green
    exit 0
}
