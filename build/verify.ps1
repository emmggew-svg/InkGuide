# InkGuide full verification gate (PowerShell mirror of verify.cmd).
# Exits non-zero on ANY failure.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent)

function Invoke-Step([string]$Name, [scriptblock]$Body) {
  Write-Host "=== $Name ==="
  & $Body
  if ($LASTEXITCODE -ne 0) { Write-Host ''; Write-Host 'VERIFY FAILED (see stage above).'; exit 1 }
}

Invoke-Step '[1/8] dotnet restore' { dotnet restore InkGuide.sln --nologo }
Invoke-Step '[2/8] dotnet build (warnings as errors)' { dotnet build InkGuide.sln -c Release --no-restore --nologo /p:TreatWarningsAsErrors=true }
Invoke-Step '[3/8] dotnet test + coverage' { dotnet test InkGuide.sln -c Release --no-build --nologo --collect:'XPlat Code Coverage' }
Invoke-Step '[4/8] bridge contract freshness' { dotnet run --project src/InkGuide.Tools/InkGuide.Tools.csproj -c Release --no-build -- contract-gen --check }

if (Test-Path 'ui/package.json') {
  Push-Location ui
  try {
    Invoke-Step '[5/8] UI gate' {
      npm ci --no-audit --no-fund
      if ($LASTEXITCODE -ne 0) { exit 1 }
      npm run typecheck --if-present
      if ($LASTEXITCODE -ne 0) { exit 1 }
      npm run lint --if-present
      if ($LASTEXITCODE -ne 0) { exit 1 }
      npm run test --if-present
      if ($LASTEXITCODE -ne 0) { exit 1 }
      npm run build
    }
  } finally { Pop-Location }
} else {
  Write-Host 'SKIP: ui/package.json not present yet (UI owner has not landed).'
}

if (Test-Path 'ui/dist') {
  $leak = Get-ChildItem -Recurse ui/dist -File | Select-String -Pattern 'fixtureBridge|\?bridge=fixture' -Quiet
  if ($leak) { Write-Host 'FAIL: fixture-bridge strings found in the production bundle.'; exit 1 }
  Write-Host 'OK: no fixture-bridge strings in ui/dist.'
} else {
  Write-Host 'SKIP: ui/dist not present; nothing to scan.'
}

Invoke-Step '[7/8] .NET audit' {
  dotnet list InkGuide.sln package --vulnerable --include-transitive | Out-File "$env:TEMP/inkguide-audit-dotnet.txt"
  if (Select-String -Path "$env:TEMP/inkguide-audit-dotnet.txt" -Pattern 'has the following vulnerable packages' -Quiet) { exit 1 }
}
if (Test-Path 'ui/package.json') {
  Push-Location ui
  try { Invoke-Step '[8a/8] npm audit' { npm audit --audit-level=high } } finally { Pop-Location }
}

$liveDir = Join-Path $env:TEMP 'ig-verify-live'
if (-not (Test-Path $liveDir)) { New-Item -ItemType Directory $liveDir | Out-Null }
Invoke-Step '[8b/8] live tool smoke (offline backends only)' { dotnet run --project src/InkGuide.Tools/InkGuide.Tools.csproj -c Release --no-build -- live-check --local-provider $liveDir }

Write-Host ''
Write-Host 'VERIFY OK'
