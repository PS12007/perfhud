<#
.SYNOPSIS
  Builds PerfHud release artifacts.

.EXAMPLE
  ./build.ps1                     # framework-dependent single-file exe (needs .NET 8 Desktop Runtime) + zip
  ./build.ps1 -SelfContained      # also builds a self-contained exe (no runtime needed, larger)
  ./build.ps1 -Installer          # also compiles the Inno Setup installer if ISCC.exe is available
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SelfContained,
    [switch]$Installer
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"

# Pick a dotnet host that actually has an SDK (a machine may have only the runtime on PATH).
$candidates = @()
$onPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($onPath) { $candidates += $onPath.Source }
$candidates += (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"), (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
$dotnet = $candidates | Where-Object { (Test-Path $_) -and ((& $_ --list-sdks 2>$null) -match '^8\.|^9\.|^10\.') } | Select-Object -First 1
if (-not $dotnet) { throw "The .NET 8 SDK was not found. Install it from https://dot.net" }

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dist | Out-Null

function Publish([string]$name, [bool]$sc) {
    $out = Join-Path $dist $name
    & $dotnet publish (Join-Path $root "src/PerfHud/PerfHud.csproj") -c Release -r win-x64 `
        --self-contained:$sc -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:Version=$Version -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $out
    Compress-Archive -Path "$out\*" -DestinationPath (Join-Path $dist "$name-$Version-win-x64.zip") -Force
    Write-Host "Built $out"
}

Publish "PerfHud" $false
if ($SelfContained) { Publish "PerfHud-SelfContained" $true }

if ($Installer) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) { $iscc = Get-Item "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue }
    if (-not $iscc) { Write-Warning "Inno Setup 6 (ISCC.exe) not found; skipping installer." }
    else {
        $isccPath = if ($iscc -is [System.Management.Automation.CommandInfo]) { $iscc.Source } else { $iscc.FullName }
        & $isccPath "/DAppVersion=$Version" "/DSourceDir=$dist\PerfHud" "/O$dist" (Join-Path $root "installer\PerfHud.iss")
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    }
}
Write-Host "Done. Artifacts in $dist"
