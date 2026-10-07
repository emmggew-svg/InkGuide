# InkGuide publish (PowerShell mirror of publish.cmd).
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent)

$Out = "dist/InkGuide-$Version-win-x64"
# Stage outside the repo: MSBuild publish file-resolution (MSB3094) breaks when
# the repo path contains characters like apostrophes (see DECISIONS D-019).
$Tmp = Join-Path $env:TEMP "inkguide-publish-$Version"

if (Test-Path dist) { Remove-Item -Recurse -Force dist }
if (Test-Path $Tmp) { Remove-Item -Recurse -Force $Tmp }
New-Item -ItemType Directory $Out | Out-Null

if (Test-Path 'ui/package.json') {
  Push-Location ui
  try {
    if (-not (Test-Path 'node_modules/.bin')) {
      npm ci --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { exit 1 }
    } else {
      Write-Host 'Using existing node_modules; clean installs happen in verify.ps1.'
    }
    npm run build; if ($LASTEXITCODE -ne 0) { exit 1 }
  } finally { Pop-Location }
} else {
  Write-Host 'SKIP: no UI bundle (ui/package.json missing).'
}

dotnet publish src/InkGuide.App/InkGuide.App.csproj -c Release -r win-x64 --self-contained `
  /p:PublishReadyToRun=true /p:PublishTrimmed=false /p:PublishSingleFile=false `
  -o $Tmp --nologo
if ($LASTEXITCODE -ne 0) { exit 1 }

if (-not ((Test-Path "$Tmp/runtimes/win-x64/native/WebView2Loader.dll") -or (Test-Path "$Tmp/WebView2Loader.dll"))) {
  Write-Host 'FAIL: WebView2Loader.dll missing from publish output. See docs/PUBLISHING.md.'
  exit 1
}
Write-Host 'OK: WebView2Loader.dll present.'

Copy-Item "$Tmp/*" $Out -Recurse -Force
if (Test-Path 'ui/dist') { Copy-Item 'ui/dist/*' "$Out/ui" -Recurse -Force }
Copy-Item 'config/*' "$Out/config" -Recurse -Force
Copy-Item 'THIRD_PARTY_NOTICES.md' $Out -Force
$Version | Out-File -Encoding ascii "$Out/VERSION.txt"

Compress-Archive -Path "$Out/*" -DestinationPath "dist/InkGuide-$Version-win-x64.zip" -Force
$h = Get-FileHash -Algorithm SHA256 "dist/InkGuide-$Version-win-x64.zip"
($h.Hash.ToLower() + "  InkGuide-$Version-win-x64.zip") | Out-File -Encoding ascii 'dist/SHA256SUMS.txt'

Write-Host ''
Write-Host "PUBLISH OK: $Out + dist/InkGuide-$Version-win-x64.zip + dist/SHA256SUMS.txt"
