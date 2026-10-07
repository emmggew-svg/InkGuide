# InkGuide signing helper (PowerShell mirror of sign.cmd).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent)

if (-not $env:SIGN_CERT_THUMBPRINT -and -not $env:SIGN_PFX_PATH) {
  Write-Host 'No certificate configured. To sign a release build:'
  Write-Host '  1. Buy/issue a code-signing certificate (see docs/PUBLISHING.md SmartScreen notes).'
  Write-Host '  2. $env:SIGN_CERT_THUMBPRINT="<thumbprint>"  (cert in the Windows store)'
  Write-Host '     or $env:SIGN_PFX_PATH="C:\secure\cert.pfx" [+ $env:SIGN_PFX_PASSWORD=...]'
  Write-Host '  3. Re-run build/sign.ps1'
  exit 0
}

$signtool = (Get-Command signtool -ErrorAction SilentlyContinue)?.Source
if (-not $signtool) {
  $candidate = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
  if (Test-Path $candidate) { $signtool = $candidate }
  else { Write-Host 'signtool.exe not found. Install the Windows SDK.'; exit 1 }
}

if ($env:SIGN_CERT_THUMBPRINT) {
  $sig = @('sign', '/sha1', $env:SIGN_CERT_THUMBPRINT, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
} elseif ($env:SIGN_PFX_PASSWORD) {
  $sig = @('sign', '/f', $env:SIGN_PFX_PATH, '/p', $env:SIGN_PFX_PASSWORD, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
} else {
  $sig = @('sign', '/f', $env:SIGN_PFX_PATH, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
}

Get-ChildItem -Recurse dist -Include *.exe, *.dll | ForEach-Object {
  Write-Host "Signing $($_.FullName)"
  & $signtool @sig $_.FullName
  if ($LASTEXITCODE -ne 0) { exit 1 }
}
Write-Host 'SIGN OK'
