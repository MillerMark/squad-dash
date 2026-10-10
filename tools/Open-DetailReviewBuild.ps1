[CmdletBinding()]
param([switch]$ValidateOnly)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$exe = Join-Path $root 'SquadDash\bin\Debug\net10.0-windows\SquadDash.App.exe'
$dll = Join-Path $root 'SquadDash\bin\Debug\net10.0-windows\SquadDash.App.dll'
foreach ($path in @($exe, $dll, (Join-Path $root 'squad-dash.slnx'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required tested-build artifact missing: $path"
    }
}
Write-Output "Workspace and application root: $root"
Write-Output "Direct tested executable: $exe"
Write-Output "Application SHA256: $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)"
if ($ValidateOnly) { return }

# Match WorkspaceOwnershipLease; never forward this review to an already-running build.
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hash = [BitConverter]::ToString($sha.ComputeHash(
        [System.Text.Encoding]::UTF8.GetBytes("$root`n$root"))).Replace('-', '').ToLowerInvariant()
} finally {
    $sha.Dispose()
}
$mutex = [System.Threading.Mutex]::new($false, "Local\SquadDash.Workspace.$($hash.Substring(0, 24))")
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) {
        throw 'Close the existing SquadDash window for this workspace before launching the detail review build.'
    }
} finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

$process = Start-Process -FilePath $exe -WorkingDirectory $root `
    -ArgumentList @('--app-root', "`"$root`"", '--workspace', "`"$root`"") -PassThru
if (-not $process.WaitForInputIdle(30000)) {
    throw "Review process $($process.Id) did not become input-ready. No process was terminated."
}
Start-Sleep -Seconds 2
$process.Refresh()
if ($process.HasExited) {
    throw 'The direct review process exited (possibly forwarded to another instance). Do not use another window as the tested build.'
}
if (-not [string]::Equals($process.MainModule.FileName, $exe, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The running executable does not match the tested build.'
}
Write-Output "Review process: $($process.Id); executable confirmed: $($process.MainModule.FileName)"
