. "$PSScriptRoot\scriptHelper.ps1"; Set-Location -LiteralPath $repoRoot

foreach ($d in $installerDefs) {
    $exe = "$repoRoot\publish\$($d.Rid)\swagSMB.exe"
    if (-not (Test-Path -LiteralPath $exe)) { throw "Missing publish output (run build.ps1 first): $exe" }
}

if (Test-Path -LiteralPath $installerOutput) { Remove-Item $installerOutput -Recurse -Force }
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

$iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source
Push-Location -LiteralPath $repoRoot
try {
    foreach ($d in $installerDefs) {
        $iss = "$repoRoot\.installer\$($d.Iss)"
        Write-Host "Building $($d.Rid) (AppVersion=$versionContents)"
        & $iscc "/DAppVersion=$versionContents" $iss
        if ($LASTEXITCODE) { throw "ISCC failed ($LASTEXITCODE): $iss" }
    }
    Write-Host "Done. Output: $installerOutput"
}
finally { Pop-Location }
