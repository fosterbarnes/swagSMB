. "$PSScriptRoot\scriptHelper.ps1"; Set-Location -LiteralPath $repoRoot

if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }

foreach ($rid in $rids) {
    $out = "$repoRoot\publish\$rid"
    if (Test-Path -LiteralPath $out) { Remove-Item $out -Recurse -Force }
    $dr = $dotnetRid[$rid]
    Write-Host "Publishing $dr -> $out"
    & dotnet publish $project -c Release -r $dr --no-self-contained -o $out
    if ($LASTEXITCODE) { throw "dotnet publish failed ($dr exit $LASTEXITCODE)" }
}
Write-Host "Publish complete. Next: pwsh -File .\.scripts\buildInstaller.ps1"
