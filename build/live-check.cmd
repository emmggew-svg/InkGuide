@echo off
rem Runs the InkGuide live end-to-end smoke test (needs OAuth for Drive;
rem runs fully offline with --local-provider). See docs\LIVE_TESTING.md.
setlocal
cd /d "%~dp0.."
dotnet run --project src\InkGuide.Tools\InkGuide.Tools.csproj -c Release -- live-check %*
exit /b %errorlevel%
