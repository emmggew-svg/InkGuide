# Runs the InkGuide live end-to-end smoke test (PowerShell mirror).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent)
dotnet run --project src/InkGuide.Tools/InkGuide.Tools.csproj -c Release -- live-check @args
exit $LASTEXITCODE
