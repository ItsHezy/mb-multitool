param(
    [switch]$NoPublish,
    [switch]$NoPathUpdate
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Membran.MultiTool\Membran.MultiTool.Cli\Membran.MultiTool.Cli.csproj'
$installRoot = Join-Path $env:LOCALAPPDATA 'mb'
$binDir = Join-Path $installRoot 'bin'
$cmdDir = Join-Path $installRoot 'cmd'
$cmdPath = Join-Path $cmdDir 'mb.cmd'

New-Item -ItemType Directory -Path $binDir -Force | Out-Null
New-Item -ItemType Directory -Path $cmdDir -Force | Out-Null

if (-not $NoPublish)
{
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $binDir
}

$cmdContent = "@echo off`r`n`"$binDir\\mb.exe`" %*`r`n"
Set-Content -Path $cmdPath -Value $cmdContent -Encoding ASCII

if (-not $NoPathUpdate)
{
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($userPath))
    {
        $parts = $userPath.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)
    }

    if ($parts -notcontains $cmdDir)
    {
        $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $cmdDir } else { "$userPath;$cmdDir" }
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Host "Added to user PATH: $cmdDir"
    }
    else
    {
        Write-Host "Already in user PATH: $cmdDir"
    }
}

Write-Host "Installed mb command shim: $cmdPath"
Write-Host "Open a new CMD/PowerShell window, then run: mb --help"
