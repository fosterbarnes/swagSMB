#requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$src = "$repoRoot\src"
$project = "$src\swagSMB\swagSMB.csproj"
$version = "$src\version"
$versionContents = ([IO.File]::ReadAllText($version)).Trim()
$readme = "$repoRoot\README.md"
$readmeContents = Get-Content -LiteralPath $readme -Raw
$tag = "v$versionContents"
$installerOutput = "$repoRoot\.installer\Output"
$buildNotes = "$repoRoot\.md\.buildNotes.txt"
$installerDefs = @(
    @{ Rid = 'x64';  Name = 'swagSMB-x64-installer.exe'; Iss = 'swagSMB.x64.installer.iss' }
    @{ Rid = 'x86';  Name = 'swagSMB-x86-installer.exe'; Iss = 'swagSMB.x86.installer.iss' }
    @{ Rid = 'arm64'; Name = 'swagSMB-arm64-installer.exe'; Iss = 'swagSMB.arm64.installer.iss' }
)
$rids = @('x64', 'x86', 'arm64')
$dotnetRid = @{ x64 = 'win-x64'; x86 = 'win-x86'; arm64 = 'win-arm64' }
