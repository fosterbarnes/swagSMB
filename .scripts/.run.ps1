. "$PSScriptRoot\scriptHelper.ps1"; Set-Location -LiteralPath $repoRoot

if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }
dotnet run --project $project --framework net8.0-windows --configuration Release --property:Platform=x64
exit $LASTEXITCODE
