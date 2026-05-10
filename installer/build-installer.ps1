# installer\build-installer.ps1
# Builds the SquadDash installer locally.
#
# Usage:
#   .\installer\build-installer.ps1              # auto-detects version from csproj + git commit count
#   .\installer\build-installer.ps1 -Version 1.0.0.819  # override version explicitly
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
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root      = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
$logs      = Join-Path $artifacts "logs"

# Keep build output friendly to transcript renderers. MSBuild's terminal logger
# emits ANSI cursor updates that look great in a real console but can overwhelm
# captured terminals.
$env:NO_COLOR = "1"
$env:DOTNET_NOLOGO = "1"
$env:npm_config_audit = "false"
$env:npm_config_fund = "false"
$env:npm_config_progress = "false"
$dotnetLogOptions = @("--tl:off", "--verbosity", "minimal", "/nologo")

# Derive version from csproj + git commit count when not supplied
if (-not $Version) {
    $csproj      = Join-Path $root "SquadDash\SquadDash.csproj"
    $xml         = [xml](Get-Content $csproj -Raw)
    $prefix      = $xml.Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
    $commitCount = git -C $root rev-list --count HEAD 2>$null
    if (-not $prefix)      { Write-Error "Could not read VersionPrefix from $csproj"; exit 1 }
    if (-not $commitCount) { Write-Error "Could not determine git commit count.";      exit 1 }
    $Version = "$prefix.$commitCount"
    Write-Host "Auto-detected version: $Version"
}

# ---------------------------------------------------------------------------
# 1. Clean staging directories
# ---------------------------------------------------------------------------
Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$artifacts\publish\launcher" | Out-Null
New-Item -ItemType Directory -Force "$artifacts\publish\app"      | Out-Null
New-Item -ItemType Directory -Force "$artifacts\publish\sdk"      | Out-Null
New-Item -ItemType Directory -Force $logs                         | Out-Null

# ---------------------------------------------------------------------------
# 2. Publish launcher  (SquadDash.exe — the thin wrapper that opens the app)
# ---------------------------------------------------------------------------
Write-Host "`n▶ Publishing launcher..."
dotnet publish "$root\SquadDashLauncher\SquadDashLauncher.csproj" `
    -c Release -r win-x64 --no-self-contained `
    -p:VersionPrefix=$Version `
    -o "$artifacts\publish\launcher" `
    @dotnetLogOptions `
    *>&1 | Tee-Object -FilePath "$logs\publish-launcher.log"

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
    -o "$artifacts\publish\app" `
    @dotnetLogOptions `
    *>&1 | Tee-Object -FilePath "$logs\publish-app.log"

if ($LASTEXITCODE -ne 0) { Write-Error "App publish failed."; exit 1 }

# ---------------------------------------------------------------------------
# 4. Bundle Squad.SDK (production deps only — omit devDependencies)
# ---------------------------------------------------------------------------
Write-Host "`n▶ Bundling Squad.SDK..."
Push-Location "$root\Squad.SDK"
try {
    npm install --omit=dev *>&1 | Tee-Object -FilePath "$logs\npm-install.log"
    if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed."; exit 1 }

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
& $iscc /DAppVersion=$Version "$root\installer\SquadDash.iss" *>&1 | Tee-Object -FilePath "$logs\inno-setup.log"
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
