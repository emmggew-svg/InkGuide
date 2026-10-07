@echo off
rem InkGuide full verification gate (MASTER_PROMPT Section 20).
rem Exits non-zero on ANY failure. UI stages skip honestly when ui/ is absent.
setlocal
cd /d "%~dp0.."

echo === [1/8] dotnet restore ===
dotnet restore InkGuide.sln --nologo
if errorlevel 1 goto :fail

echo === [2/8] dotnet build (warnings as errors) ===
dotnet build InkGuide.sln -c Release --no-restore --nologo /p:TreatWarningsAsErrors=true
if errorlevel 1 goto :fail

echo === [3/8] dotnet test + coverage ===
dotnet test InkGuide.sln -c Release --no-build --nologo --collect:"XPlat Code Coverage"
if errorlevel 1 goto :fail

echo === [4/8] bridge contract freshness ===
dotnet run --project src\InkGuide.Tools\InkGuide.Tools.csproj -c Release --no-build -- contract-gen --check
if errorlevel 1 goto :fail

echo === [5/8] UI gate ===
if not exist ui\package.json (
  echo SKIP: ui/package.json not present yet; UI owner has not landed.
) else (
  pushd ui
  call npm ci --no-audit --no-fund
  if errorlevel 1 ( popd & goto :fail )
  call npm run typecheck --if-present
  if errorlevel 1 ( popd & goto :fail )
  call npm run lint --if-present
  if errorlevel 1 ( popd & goto :fail )
  call npm run test --if-present
  if errorlevel 1 ( popd & goto :fail )
  call npm run build
  if errorlevel 1 ( popd & goto :fail )
  popd
)

echo === [6/8] production-bundle fixture-leak check ===
if not exist ui\dist (
  echo SKIP: ui/dist not present; nothing to scan.
) else (
  findstr /m /s /i /c:"fixtureBridge" /c:"?bridge=fixture" ui\dist\* 1>nul 2>nul
  if not errorlevel 1 (
    echo FAIL: fixture-bridge strings found in the production bundle.
    goto :fail
  )
  echo OK: no fixture-bridge strings in ui/dist.
)

echo === [7/8] dependency audits ===
dotnet list InkGuide.sln package --vulnerable --include-transitive > "%TEMP%\inkguide-audit-dotnet.txt" 2>&1
if errorlevel 1 goto :fail
findstr /i /c:"has the following vulnerable packages" "%TEMP%\inkguide-audit-dotnet.txt" 1>nul 2>nul
if not errorlevel 1 (
  echo FAIL: vulnerable .NET packages found. See %TEMP%\inkguide-audit-dotnet.txt
  echo Document any accepted exception in docs\KNOWN_LIMITATIONS.md, then re-run.
  goto :fail
)
echo OK: no vulnerable .NET packages reported.
if exist ui\package.json (
  pushd ui
  call npm audit --audit-level=high
  if errorlevel 1 ( popd & goto :fail )
  popd
) else (
  echo SKIP: npm audit; no UI yet.
)

echo === [8/8] live tool smoke (offline backends only) ===
if not exist "%TEMP%\ig-verify-live" mkdir "%TEMP%\ig-verify-live"
dotnet run --project src\InkGuide.Tools\InkGuide.Tools.csproj -c Release --no-build -- live-check --local-provider "%TEMP%\ig-verify-live"
if errorlevel 1 goto :fail

echo.
echo VERIFY OK
exit /b 0

:fail
echo.
echo VERIFY FAILED (see stage above).
exit /b 1
