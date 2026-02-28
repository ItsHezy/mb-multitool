@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"
set "SOLUTION=%REPO_ROOT%\Membran.MultiTool\Membran.MultiTool.slnx"
set "INSTALL_SCRIPT=%REPO_ROOT%\scripts\install-mb.ps1"

echo [1/4] Checking .NET SDK...
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet was not found in PATH.
    echo Install .NET 8 SDK, then re-run this file.
    exit /b 1
)

echo [2/4] Restoring and building solution...
dotnet restore "%SOLUTION%"
if errorlevel 1 (
    echo ERROR: dotnet restore failed.
    exit /b 1
)

dotnet build "%SOLUTION%" -c Release
if errorlevel 1 (
    echo ERROR: dotnet build failed.
    exit /b 1
)

echo [3/4] Installing mb command shim...
powershell -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%"
if errorlevel 1 (
    echo ERROR: install-mb.ps1 failed.
    exit /b 1
)

echo [4/4] Verifying install...
where mb >nul 2>&1
if errorlevel 1 (
    echo WARNING: mb was installed but current shell may not see PATH changes yet.
    echo Open a new terminal and run: mb --help
) else (
    mb --help
)

echo Setup completed.
exit /b 0
