$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
dotnet run --project src/FitnessCenter.Lite --launch-profile http
