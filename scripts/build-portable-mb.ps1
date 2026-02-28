param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Membran.MultiTool\Membran.MultiTool.Cli\Membran.MultiTool.Cli.csproj'
$distRoot = Join-Path $repoRoot 'dist'
$portableRoot = Join-Path $distRoot 'mb-portable'
$publishDir = Join-Path $portableRoot '_publish'
$zipPath = Join-Path $distRoot 'mb-portable.zip'

if (Test-Path $portableRoot)
{
    Remove-Item -Path $portableRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "Publishing portable single-file mb.exe..."
dotnet publish $project -c Release -r win-x64 -o $publishDir `
    -p:SelfContained=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

$publishedExe = Join-Path $publishDir 'mb.exe'
if (-not (Test-Path $publishedExe))
{
    throw "Publish succeeded but mb.exe was not found: $publishedExe"
}

Copy-Item -Path $publishedExe -Destination (Join-Path $portableRoot 'mb.exe') -Force

$mbCmd = @" 
@echo off
"%~dp0mb.exe" %*
"@
Set-Content -Path (Join-Path $portableRoot 'mb.cmd') -Value $mbCmd -Encoding ASCII

$installCmd = @" 
@echo off
setlocal

set "TARGET=%~dp0"
for %%I in ("%TARGET%") do set "TARGET=%%~fI"
if "%TARGET:~-1%"=="\\" set "TARGET=%TARGET:~0,-1%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "`$target='%TARGET%'; `$path=[Environment]::GetEnvironmentVariable('Path','User'); if([string]::IsNullOrWhiteSpace(`$path)){`$newPath=`$target}else{`$parts=`$path.Split(';',[System.StringSplitOptions]::RemoveEmptyEntries); if(`$parts -contains `$target){`$newPath=`$path}else{`$newPath=`$path + ';' + `$target}}; [Environment]::SetEnvironmentVariable('Path',`$newPath,'User')"

echo Added to USER PATH: %TARGET%
echo Open a new CMD/PowerShell and run: mb --help
endlocal
"@
Set-Content -Path (Join-Path $portableRoot 'install-portable.cmd') -Value $installCmd -Encoding ASCII

$removeCmd = @" 
@echo off
setlocal

set "TARGET=%~dp0"
for %%I in ("%TARGET%") do set "TARGET=%%~fI"
if "%TARGET:~-1%"=="\\" set "TARGET=%TARGET:~0,-1%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "`$target='%TARGET%'; `$path=[Environment]::GetEnvironmentVariable('Path','User'); if([string]::IsNullOrWhiteSpace(`$path)){exit 0}; `$parts=`$path.Split(';',[System.StringSplitOptions]::RemoveEmptyEntries) | Where-Object { `$_ -ne `$target }; `$newPath=[string]::Join(';',`$parts); [Environment]::SetEnvironmentVariable('Path',`$newPath,'User')"

echo Removed from USER PATH: %TARGET%
endlocal
"@
Set-Content -Path (Join-Path $portableRoot 'remove-portable.cmd') -Value $removeCmd -Encoding ASCII

$readme = @" 
mb portable package

Files:
- mb.exe
- mb.cmd
- install-portable.cmd
- remove-portable.cmd

Usage:
1) Double-click install-portable.cmd (adds this folder to USER PATH)
2) Open a new terminal
3) Run: mb --help

Commands:
- mb ip <ip|self> [--json]
- mb ip batch --file <path> [--json]
- mb ui "<path>" [--yes] [--dry-run] [--json] [--no-restore-point] [--exclude <pattern>] [--elevate]
- mb config get [key] [--json]
- mb config set <key> <value>
- mb doctor [--json]

Aliases:
- mb -IP <ip>
- mb -ui "<path>"

Notes:
- ui in non-admin mode skips admin-only artifacts and reports them.
- use --elevate to relaunch with Administrator privileges.
"@
Set-Content -Path (Join-Path $portableRoot 'README.txt') -Value $readme -Encoding ASCII

Remove-Item -Path $publishDir -Recurse -Force

if (-not $NoZip)
{
    if (Test-Path $zipPath)
    {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $portableRoot '*') -DestinationPath $zipPath
    Write-Host "Created zip: $zipPath"
}

Write-Host "Portable output: $portableRoot"
