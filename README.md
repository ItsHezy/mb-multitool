# Membran MultiTool v1

## Auto Install

Run this from the repo root:

```cmd
Auto-Install\setup-all.bat
```

What it does:

1. Checks for `.NET` in `PATH`.
2. Restores/builds the solution.
3. Installs the `mb` command with `scripts\install-mb.ps1`.
4. Verifies by running `mb --help`.

## Manual Install

### Project Structure

- `Membran.MultiTool/`
  - `Membran.MultiTool.Cli/`
  - `Membran.MultiTool.Core/`
  - `Membran.MultiTool.Geo/`
  - `Membran.MultiTool.Gui/`
  - `Membran.MultiTool.Osint/` (currently not exposed by v1 CLI/GUI commands)
  - `Membran.MultiTool.Tests/`
  - `Membran.MultiTool.Uninstall/`
  - `Membran.MultiTool.slnx`
- `scripts/`
- `Auto-Install/`

### Build

```powershell
dotnet restore .\Membran.MultiTool\Membran.MultiTool.slnx
dotnet build .\Membran.MultiTool\Membran.MultiTool.slnx -c Release
dotnet test .\Membran.MultiTool\Membran.MultiTool.Tests\Membran.MultiTool.Tests.csproj -c Release
```

### Install `mb` Command

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-mb.ps1
```

Open a new terminal, then:

```powershell
mb --help
```

### Portable Build

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-portable-mb.ps1
```

Output:

- `dist\mb-portable\mb.exe`
- `dist\mb-portable\mb.cmd`
- `dist\mb-portable\install-portable.cmd`
- `dist\mb-portable\remove-portable.cmd`
- `dist\mb-portable\README.txt`
- `dist\mb-portable.zip`

### Main Commands

```powershell
mb ip 8.8.8.8 --json
mb uninstall list --json
mb ui "C:\Path\To\App" --yes --dry-run --json
mb doctor --json
```

## Safety Notes

- IP geolocation is always approximate (`Approximate (IP-based)`).
- Deep uninstall does not perform anti-forensics (no log wiping or tampering).
- `ui` and uninstall execution require explicit confirmation.
- Non-admin `ui` runs partial cleanup and skips admin-only artifacts.
- `ui --elevate` can relaunch into an Administrator session for deeper cleanup.
