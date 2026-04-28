# Release inputs after `.\.scripts\build.ps1` and `.\.scripts\buildInstaller.ps1` (version from `src/version`):
#   - publish/win-x64 | win-x86 | win-arm64
#   - .installer/Output/swagSMB-{x64,x86,arm64}-installer.exe

#requires -Version 7.0
$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "Draft swagSMB Release"

$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root

$versionPath = Join-Path $root "src\version"
if (-not (Test-Path -LiteralPath $versionPath)) { throw "Version file not found: $versionPath" }
$versionContents = ([IO.File]::ReadAllText($versionPath)).Trim()
if ([string]::IsNullOrWhiteSpace($versionContents)) { throw "Version file is empty: $versionPath" }

$installerDir = Join-Path $root ".installer\Output"
$installerDefs = @(
    @{ Rid = 'win-x64';  Name = 'swagSMB-x64-installer.exe' }
    @{ Rid = 'win-x86';  Name = 'swagSMB-x86-installer.exe' }
    @{ Rid = 'win-arm64'; Name = 'swagSMB-arm64-installer.exe' }
)
$rids = @('win-x64', 'win-x86', 'win-arm64')

foreach ($d in $installerDefs) {
    $p = Join-Path $installerDir $d.Name
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Host "Missing installer (run build.ps1 then buildInstaller.ps1): $p" -ForegroundColor Red
        exit 1
    }
}
foreach ($rid in $rids) {
    $pub = Join-Path $root "publish\$rid"
    if (-not (Test-Path -LiteralPath $pub)) {
        Write-Host "Missing publish output (run .\.scripts\build.ps1): $pub" -ForegroundColor Red
        exit 1
    }
    $exe = Join-Path $pub "swagSMB.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        Write-Host "Expected swagSMB.exe not found: $exe" -ForegroundColor Red
        exit 1
    }
}

$null = Get-Command git -ErrorAction Stop
$null = Get-Command gh -ErrorAction Stop

Write-Host "Version: $versionContents"
foreach ($d in $installerDefs) { Write-Host "Installer: $(Join-Path $installerDir $d.Name)" }
foreach ($rid in $rids) { Write-Host "Portable:  $(Join-Path $root "publish\$rid")" }

$v = $versionContents
$tagName = "v$v"
$defaultReleaseName = "swagSMB v$v"
$buildNotesTxt = Join-Path $root ".md\.buildNotes.txt"
$releaseName = $defaultReleaseName
$releaseNotes = ""

$useBuildNotesTxt = $false
if (Test-Path -LiteralPath $buildNotesTxt) {
    $bnRaw = Get-Content -LiteralPath $buildNotesTxt -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($null -ne $bnRaw -and $bnRaw.Trim().Length -gt 0) { $useBuildNotesTxt = $true }
}

if ($useBuildNotesTxt) {
    $bnLines = @(Get-Content -LiteralPath $buildNotesTxt -Encoding UTF8)
    $releaseName = $bnLines[0].Trim()
    if ($bnLines.Count -le 1) { $releaseNotes = "" }
    else { $releaseNotes = ($bnLines[1..($bnLines.Count - 1)] -join "`n") }
    Write-Host "`nUsing .md/.buildNotes.txt: first line = release title; remaining lines = notes (prompt skipped)." -ForegroundColor Cyan
}
else {
    Write-Host "`nEnter release notes:" -ForegroundColor Yellow
    Write-Host "Tabs will be converted to spaces for GitHub formatting." -ForegroundColor Cyan
    Write-Host "End with two consecutive empty lines." -ForegroundColor Cyan
    $releaseNotesLines = @()
    $consecutiveEmptyLines = 0
    $hasReleaseNotes = $false

    while ($true) {
        $line = Read-Host ">"
        if ($line -eq "") {
            $consecutiveEmptyLines++
            if ($consecutiveEmptyLines -ge 2) { break }
            $releaseNotesLines += ""
        }
        else {
            $line = $line -replace "`t", "    "
            $releaseNotesLines += $line
            $consecutiveEmptyLines = 0
            $hasReleaseNotes = $true
        }
    }

    if (-not $hasReleaseNotes) {
        Write-Host "Error: No release notes entered." -ForegroundColor Red
        exit 1
    }

    $releaseNotes = $releaseNotesLines -join "`n"
}

$uploadFiles = @()
foreach ($rid in $rids) {
    $pub = Join-Path $root "publish\$rid"
    $zipName = "swagSMBPortable_${tagName}_${rid}.zip"
    $zipPath = Join-Path $env:TEMP $zipName
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue }
    Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zipPath -Force
    $uploadFiles += $zipPath
}
foreach ($d in $installerDefs) {
    $srcExe = Join-Path $installerDir $d.Name
    $destExe = Join-Path $env:TEMP ("swagSMBInstaller_${tagName}_{0}.exe" -f $d.Rid)
    if (Test-Path -LiteralPath $destExe) { Remove-Item -LiteralPath $destExe -Force -ErrorAction SilentlyContinue }
    Copy-Item -LiteralPath $srcExe -Destination $destExe -Force
    $uploadFiles += $destExe
}

if (git tag -l $tagName) {
    Write-Host "Local tag $tagName exists. Deleting..."
    git tag -d $tagName
}

$remoteTags = @(git ls-remote --tags origin 2>$null | ForEach-Object { ($_ -split "`t")[1] })
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Remote tag $tagName exists. Deleting..."
    git push origin --delete $tagName
}

git tag $tagName
git push origin $tagName

$originUrl = (git config --get remote.origin.url 2>$null).Trim().TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($originUrl)) { throw "git remote origin.url is not set" }
if (-not ($originUrl -match 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(?:\.git)?$')) {
    throw "Could not parse owner/repo from origin for gh (expected github.com HTTPS or SSH URL): $originUrl"
}
$ghRepo = '{0}/{1}' -f $Matches['owner'], $Matches['repo']

& gh release create $tagName @uploadFiles --repo $ghRepo --title "$releaseName" --notes "$releaseNotes"

foreach ($path in $uploadFiles) {
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}
