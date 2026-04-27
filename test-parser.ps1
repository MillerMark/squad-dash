# Quick test to verify SUMMARY.md parsing
$summaryPath = "D:\Drive\Source\SquadUI\docs\SUMMARY.md"
$lines = Get-Content $summaryPath

Write-Host "=== ALL SUMMARY LINES ===" -ForegroundColor Cyan
$lineNo = 0
foreach ($line in $lines) {
    $lineNo++
    if ($line -match '^\s*\*\s+\[([^\]]+)\]\(([^)]+)\)\s*$') {
        $title = $matches[1]
        $path = $matches[2]
        $indent = ($line -replace '^(\s*).*','$1').Length
        $isTopLevel = $indent -lt 2
        $level = if ($isTopLevel) { "TOP" } else { "CHILD" }
        Write-Host "$lineNo : [$level] indent=$indent : $title" -ForegroundColor $(if ($isTopLevel) { "Green" } else { "Yellow" })
    }
}

Write-Host "`n=== GETTING STARTED SECTION ===" -ForegroundColor Cyan
$inGettingStarted = $false
$gettingStartedParent = $null
foreach ($line in $lines) {
    if ($line -match '\* \[Getting Started\]') {
        $inGettingStarted = $true
        Write-Host "Parent: Getting Started" -ForegroundColor Green
    }
    elseif ($inGettingStarted -and $line -match '^\s{2}\*\s+\[([^\]]+)\]') {
        $childTitle = $matches[1]
        Write-Host "  Child: $childTitle" -ForegroundColor Yellow
    }
    elseif ($inGettingStarted -and $line -match '^\*\s+\[' -and $line -notmatch '^\s{2}') {
        break  # Hit next top-level item
    }
}
