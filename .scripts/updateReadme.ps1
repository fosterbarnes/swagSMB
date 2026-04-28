. "$PSScriptRoot\scriptHelper.ps1"; Set-Location -LiteralPath $repoRoot

if ([string]::IsNullOrWhiteSpace($readmeContents)) { throw "README missing or empty: $readme" }
if ([string]::IsNullOrWhiteSpace($versionContents)) { throw "src/version is empty." }
if ($readmeContents -notmatch '(?m)^version\s*=\s*(\d+\.\d+\.\d+)\s*$') {
    throw 'Could not find a line like "version = x.y.z" in README.md.'
}
$previousVersion = $Matches[1]
if ($previousVersion -eq $versionContents) {
    Write-Host "README already at version $versionContents; no changes."
    exit 0
}

Set-Content -LiteralPath $readme -Value $readmeContents.Replace($previousVersion, $versionContents) -NoNewline -Encoding utf8NoBOM
Write-Host "Updated README.md: $previousVersion -> $versionContents"
