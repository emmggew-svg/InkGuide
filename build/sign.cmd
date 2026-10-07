@echo off
rem InkGuide signing helper.
rem Signs every .exe/.dll under dist\ when a certificate is available:
rem   set SIGN_CERT_THUMBPRINT=ABC123...  (cert in CurrentUser or LocalMachine store)
rem   or set SIGN_PFX_PATH=C:\path\cert.pfx [+ SIGN_PFX_PASSWORD=...]
rem Otherwise prints how to sign. Certificates are never stored in the repo.
setlocal
cd /d "%~dp0.."

if "%SIGN_PFX_PATH%"=="" if "%SIGN_CERT_THUMBPRINT%"=="" goto :howto

set SIGNTOOL=signtool
where signtool >nul 2>nul
if errorlevel 1 (
  if exist "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" (
    set "SIGNTOOL=%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
  ) else (
    echo signtool.exe not found. Install the Windows SDK (App Development / signing tools).
    exit /b 1
  )
)

if not "%SIGN_CERT_THUMBPRINT%"=="" (
  set SIGNARGS=sign /sha1 "%SIGN_CERT_THUMBPRINT%" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256
) else (
  if "%SIGN_PFX_PASSWORD%"=="" (
    set SIGNARGS=sign /f "%SIGN_PFX_PATH%" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256
  ) else (
    set SIGNARGS=sign /f "%SIGN_PFX_PATH%" /p "%SIGN_PFX_PASSWORD%" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256
  )
)

for /r dist %%F in (*.exe *.dll) do (
  echo Signing %%F
  "%SIGNTOOL%" %SIGNARGS% "%%F"
  if errorlevel 1 exit /b 1
)
echo SIGN OK
exit /b 0

:howto
echo No certificate configured. To sign a release build:
echo   1. Buy/issue a code-signing certificate (see docs\PUBLISHING.md SmartScreen notes).
echo   2. Either import it into the Windows certificate store and set:
echo        set SIGN_CERT_THUMBPRINT=^<thumbprint^>
echo      or point at a PFX file (kept OUTSIDE the repo):
echo        set SIGN_PFX_PATH=C:\secure\cert.pfx
echo        set SIGN_PFX_PASSWORD=...
echo   3. Re-run build\sign.cmd
exit /b 0
