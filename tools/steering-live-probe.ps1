[CmdletBinding()]
param(
    [ValidateRange(5, 60)]
    [int] $Seconds = 30
)

$startedAt = Get-Date
Write-Output ("STEERING-PROBE START duration={0}s started={1:o}" -f $Seconds, $startedAt)

for ($second = 1; $second -le $Seconds; $second++) {
    Start-Sleep -Seconds 1
    Write-Output ("STEERING-PROBE HEARTBEAT {0}/{1}" -f $second, $Seconds)
}

$finishedAt = Get-Date
Write-Output ("STEERING-PROBE TOOL-FINISHED finished={0:o}" -f $finishedAt)
