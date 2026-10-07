@echo off
rem InkGuide publish: produces dist\InkGuide-{version}-win-x64\ + ZIP + SHA256SUMS.
rem Usage: build\publish.cmd [version]   (default 0.1.0, must match Directory.Build.props)
rem Published as an unpacked folder on purpose: WebView2Loader.dll (native) does
rem not survive single-file bundling reliably. See docs\PUBLISHING.md.
setlocal
cd /d "%~dp0.."

set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.1.0
set OUT=dist\InkGuide-%VERSION%-win-x64
set TMP=publish\tmp

echo Cleaning...
if exist dist rmdir /s /q dist
if exist publish rmdir /s /q publish
mkdir "%OUT%"

if exist ui\package.json (
  echo Building UI...
  pushd ui
  if not exist node_modules\.bin (
    call npm ci --no-audit --no-fund
    if errorlevel 1 ( popd & exit /b 1 )
  ) else (
    echo Using existing node_modules; clean installs happen in verify.cmd.
  )
  call npm run build
  if errorlevel 1 ( popd & exit /b 1 )
  popd
) else (
  echo SKIP: no UI bundle; ui/package.json is missing. The app will show its missing-UI notice.
)

echo Publishing App (Release, win-x64, self-contained, ReadyToRun, no trim, folder)...
dotnet publish src\InkGuide.App\InkGuide.App.csproj -c Release -r win-x64 --self-contained ^
  /p:PublishReadyToRun=true /p:PublishTrimmed=false /p:PublishSingleFile=false ^
  -o "%TMP%" --nologo
if errorlevel 1 exit /b 1

echo Verifying WebView2 native loader story...
if exist "%TMP%\runtimes\win-x64\native\WebView2Loader.dll" goto :loaderok
if exist "%TMP%\WebView2Loader.dll" goto :loaderok
echo FAIL: WebView2Loader.dll missing from publish output. See docs\PUBLISHING.md.
exit /b 1
:loaderok
echo OK: WebView2Loader.dll present.

echo Assembling %OUT%...
robocopy "%TMP%" "%OUT%" /e /nfl /ndl /njh /njs >nul
if errorlevel 8 exit /b 1
if exist ui\dist (
  mkdir "%OUT%\ui"
  robocopy "ui\dist" "%OUT%\ui" /e /nfl /ndl /njh /njs >nul
  if errorlevel 8 exit /b 1
)
mkdir "%OUT%\config"
robocopy "config" "%OUT%\config" /e /nfl /ndl /njh /njs >nul
if errorlevel 8 exit /b 1
copy "THIRD_PARTY_NOTICES.md" "%OUT%" >nul
if errorlevel 1 exit /b 1
echo %VERSION% > "%OUT%\VERSION.txt"

echo Zipping + hashing...
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath 'dist\InkGuide-%VERSION%-win-x64.zip' -Force"
if errorlevel 1 exit /b 1
powershell -NoProfile -Command "$h = Get-FileHash -Algorithm SHA256 'dist\InkGuide-%VERSION%-win-x64.zip'; ($h.Hash.ToLower() + '  InkGuide-%VERSION%-win-x64.zip') | Out-File -Encoding ascii 'dist\SHA256SUMS.txt'"
if errorlevel 1 exit /b 1

echo.
echo PUBLISH OK: %OUT% + dist\InkGuide-%VERSION%-win-x64.zip + dist\SHA256SUMS.txt
exit /b 0
