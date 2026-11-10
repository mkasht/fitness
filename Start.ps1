$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
dotnet run --project src/FitnessCenter.Web --launch-profile http

