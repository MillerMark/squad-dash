# installer\build-installer.ps1
# Builds the SquadDash installer locally.
#
# Usage:
#   .\installer\build-installer.ps1 -Version 1.0.0
#
# Prerequisites:
#   - .NET 10 SDK
#   - Node.js / npm (for npm ci)
#   - Inno Setup 6  (https://jrsoftware.org/isdl.php)
#
# NOTE: Node.js is a RUNTIME prerequisite for SquadDash — it is NOT bundled in
# the installer. SquadSdkProcess.cs calls `node runPrompt.js` from PATH.
# The WinGet manifest should declare Node.js as a dependency, or the README
# should document installing it first.

param(
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root      = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"

# ---------------------------------------------------------------------------
# 1. Clean staging directories
# ---------------------------------------------------------------------------
Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$artifacts\publish\launcher" | Out-Null
New-Item -ItemType Directory -Force "$artifacts\publish\app"      | Out-Null
New-Item -ItemType Directory -Force "$artifacts\publish\sdk"      | Out-Null

# ---------------------------------------------------------------------------
# 2. Publish launcher  (SquadDash.exe — the thin wrapper that opens the app)
# ---------------------------------------------------------------------------
Write-Host "`n▶ Publishing launcher..."
dotnet publish "$root\SquadDashLauncher\SquadDashLauncher.csproj" `
    -c Release -r win-x64 --no-self-contained `
    -p:VersionPrefix=$Version `
    -o "$artifacts\publish\launcher"

if ($LASTEXITCODE -ne 0) { Write-Error "Launcher publish failed."; exit 1 }

# ---------------------------------------------------------------------------
# 3. Publish app payload  (SquadDash.App.exe + all DLLs / assets)
#
#    EnableRunSlotDeployment defaults to false in Release already (see
#    SquadDash.csproj), so no extra property is needed.  The flag is
#    only true in Debug to support the A/B hot-swap dev workflow.
# ---------------------------------------------------------------------------
Write-Host "`n▶ Publishing app payload..."
dotnet publish "$root\SquadDash\SquadDash.csproj" `
    -c Release -r win-x64 --no-self-contained `
    -p:VersionPrefix=$Version `
    -o "$artifacts\publish\app"

if ($LASTEXITCODE -ne 0) { Write-Error "App publish failed."; exit 1 }

# ---------------------------------------------------------------------------
# 4. Bundle Squad.SDK (production deps only — omit devDependencies)
# ---------------------------------------------------------------------------
Write-Host "`n▶ Bundling Squad.SDK..."
Push-Location "$root\Squad.SDK"
try {
    npm ci --omit=dev
    if ($LASTEXITCODE -ne 0) { Write-Error "npm ci failed."; exit 1 }

    # Copy runtime JS files and package manifest
    Get-ChildItem -File -Filter "*.js" | Copy-Item -Destination "$artifacts\publish\sdk\"
    Copy-Item "package.json" -Destination "$artifacts\publish\sdk\"

    # Copy production node_modules
    Copy-Item "node_modules" -Destination "$artifacts\publish\sdk\" -Recurse -Force
}
finally {
    Pop-Location
}

# ---------------------------------------------------------------------------
# 5. Compile installer with Inno Setup 6
# ---------------------------------------------------------------------------
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    Write-Error "Inno Setup 6 not found at '$iscc'. Install it from https://jrsoftware.org/isdl.php"
    exit 1
}

Write-Host "`n▶ Compiling installer..."
& $iscc /DAppVersion=$Version "$root\installer\SquadDash.iss"
if ($LASTEXITCODE -ne 0) { Write-Error "ISCC compilation failed."; exit 1 }

# ---------------------------------------------------------------------------
# 6. Report result
# ---------------------------------------------------------------------------
$output = "$artifacts\SquadDash-$Version-Setup.exe"
if (Test-Path $output) {
    # Get-FileHash requires PowerShell 4+; fall back to .NET SHA256 if unavailable
    if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
        $hash = (Get-FileHash $output -Algorithm SHA256).Hash
    } else {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $stream = [System.IO.File]::OpenRead($output)
        try   { $bytes = $sha256.ComputeHash($stream) }
        finally { $stream.Dispose(); $sha256.Dispose() }
        $hash = ($bytes | ForEach-Object { $_.ToString("X2") }) -join ""
    }
    Write-Host ""
    Write-Host "✅ Installer built: $output"
    Write-Host "   SHA256: $hash"
} else {
    Write-Error "Installer not found at expected path: $output"
    exit 1
}
