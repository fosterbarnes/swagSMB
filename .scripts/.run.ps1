#!/usr/bin/env pwsh
#requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $repoRoot

$project = Join-Path $repoRoot 'src' 'swagSMB' 'swagSMB.csproj'
if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

dotnet run `
    --project $project `
    --framework net8.0-windows `
    --configuration Release `
    --property:Platform=x64

exit $LASTEXITCODE
