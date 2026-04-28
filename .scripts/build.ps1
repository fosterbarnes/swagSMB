#!/usr/bin/env pwsh
# Publishes `swagSMB` for each Windows RID into `publish/win-*` for Inno Setup (see `.installer\*.iss`).
#requires -Version 7.0
$ErrorActionPreference = "Stop"

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src' 'swagSMB' 'swagSMB.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }

$rids = @('win-x64', 'win-x86', 'win-arm64')
foreach ($rid in $rids) {
    $out = Join-Path $root 'publish' $rid
    if (Test-Path -LiteralPath $out) {
        Remove-Item -LiteralPath $out -Recurse -Force
    }
    Write-Host "Publishing $rid -> $out"
    & dotnet publish $project `
        --configuration Release `
        --runtime $rid `
        --self-contained false `
        --output $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit $LASTEXITCODE)" }
}
Write-Host "Publish complete. Next: pwsh -File .\.scripts\buildInstaller.ps1"
