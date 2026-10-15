$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$checkProject = Join-Path $PSScriptRoot 'ProductionSimulationChecks.csproj'
dotnet run --project $checkProject --configuration Release
exit $LASTEXITCODE
