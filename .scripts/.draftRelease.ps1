. "$PSScriptRoot\scriptHelper.ps1"; Set-Location -LiteralPath $repoRoot; & "$PSScriptRoot\.buildAll.ps1"

Write-Host "Version: $versionContents`nIncluded builds:"
$installerDefs | ForEach-Object { Write-Host "$installerOutput\$($_.Name)" }
$rids | ForEach-Object { Write-Host "$repoRoot\publish\$_" }

$releaseNotes = $null
if (Test-Path -LiteralPath $buildNotes) {
    $bn = Get-Content -LiteralPath $buildNotes -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($null -ne $bn -and $bn.Trim().Length -gt 0) {
        $releaseNotes = $bn.Trim()
        Write-Host "`nUsing .md/.buildNotes.md for release notes (title is $tag)." -ForegroundColor Cyan
    }
}
if ($null -eq $releaseNotes) {
    Write-Host "`nEnter release notes (tabs -> spaces; end with two empty lines):" -ForegroundColor Yellow
    $lines = @()
    $empty = 0
    $any = $false
    while ($true) {
        $line = Read-Host ">"
        if ($line -eq "") {
            $empty++
            if ($empty -ge 2) { break }
            $lines += ""
        }
        else {
            $lines += ($line -replace "`t", "    ")
            $empty = 0
            $any = $true
        }
    }
    if (-not $any) {
        Write-Host "Error: No release notes entered." -ForegroundColor Red
        exit 1
    }
    $releaseNotes = $lines -join "`n"
}

$uploadFiles = @()
foreach ($rid in $rids) {
    $zip = "$env:TEMP\swagSMBPortable_${tag}_$rid.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue }
    Compress-Archive -Path "$repoRoot\publish\$rid\*" -DestinationPath $zip -Force
    $uploadFiles += $zip
}
foreach ($d in $installerDefs) {
    $dest = "$env:TEMP\$( 'swagSMBInstaller_{0}_{1}.exe' -f $tag, $d.Rid )"
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue }
    Copy-Item -LiteralPath "$installerOutput\$($d.Name)" -Destination $dest -Force
    $uploadFiles += $dest
}

if (git tag -l $tag) {
    Write-Host "Local tag $tag exists. Deleting..."
    git tag -d $tag
}
$remoteTags = @(git ls-remote --tags origin 2>$null | ForEach-Object { ($_ -split "`t")[1] })
if ($remoteTags -contains "refs/tags/$tag") {
    Write-Host "Remote tag $tag exists. Deleting..."
    git push origin --delete $tag
}

git tag $tag
git push origin $tag

$originUrl = (git config --get remote.origin.url 2>$null).Trim().TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($originUrl) -or $originUrl -notmatch 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(?:\.git)?$') {
    throw "origin.url missing or not a github.com remote: $originUrl"
}
$ghRepo = '{0}/{1}' -f $Matches['owner'], $Matches['repo']

& gh release create $tag @uploadFiles --repo $ghRepo --title "$tag" --notes "$releaseNotes"

$uploadFiles | ForEach-Object { Remove-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue }
