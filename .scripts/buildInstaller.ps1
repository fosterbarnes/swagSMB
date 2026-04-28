#!/usr/bin/env pwsh
# Requires Inno Setup (ISCC.exe) on PATH. Publish outputs expected under repo `publish/` (see `build.ps1`).
#requires -Version 7.0
$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)][string]$What,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (exit $LASTEXITCODE): $FilePath $($ArgumentList -join ' ')"
    }
}

$root = Split-Path $PSScriptRoot -Parent
$versionPath = Join-Path $root "src\version"
if (-not (Test-Path -LiteralPath $versionPath)) { throw "Version file not found: $versionPath" }
$ver = ([IO.File]::ReadAllText($versionPath)).Trim()
if ([string]::IsNullOrWhiteSpace($ver)) { throw "Version file is empty: $versionPath" }

$required = @(
    @{ Rid = "win-x64"; Iss = "swagSMB.x64.installer.iss" }
    @{ Rid = "win-x86"; Iss = "swagSMB.x86.installer.iss" }
    @{ Rid = "win-arm64"; Iss = "swagSMB.arm64.installer.iss" }
)
foreach ($r in $required) {
    $stage = Join-Path $root "publish\$($r.Rid)"
    if (-not (Test-Path -LiteralPath $stage)) {
        throw "Missing publish output (run .\.scripts\build.ps1 first): $stage"
    }
    $exe = Join-Path $stage "swagSMB.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Expected built app not found: $exe"
    }
}

$out = Join-Path $root ".installer\Output"
if (Test-Path -LiteralPath $out) {
    Write-Host "Cleaning $out"
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Path $out -Force | Out-Null

$iscc = Get-Command ISCC.exe -ErrorAction Stop
Push-Location -LiteralPath $root
try {
    foreach ($r in $required) {
        $iss = Join-Path $root ".installer\$($r.Iss)"
        Write-Host "Building installer for $($r.Rid) (AppVersion=$ver)"
        Invoke-NativeCommand -What "ISCC" -FilePath $iscc.Source -ArgumentList @("/DAppVersion=$ver", $iss)
    }
    Write-Host "Done. Output: $out"
}
finally {
    Pop-Location
}
