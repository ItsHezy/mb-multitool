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

### Command Reference

#### Root

```powershell
mb --help
mb --version
```

#### Geo

```powershell
mb geo --help
mb geo lookup --ip self
mb geo lookup --ip 8.8.8.8 --json
```

#### IP (Quick Geo)

```powershell
mb ip --help
mb ip
mb ip 8.8.8.8 --json
mb ip batch --file .\ips.txt
mb ip batch --file .\ips.txt --json
```

Aliases:

```powershell
mb -IP 8.8.8.8
mb --IP 8.8.8.8
mb /IP 8.8.8.8
```

#### Uninstall

```powershell
mb uninstall --help
mb uninstall list
mb uninstall list --include-system --json
mb uninstall preview --app-id <APP_ID> --json
mb uninstall preview --app-id <APP_ID> --manual-rule .\rule.json --restore-point --json
mb uninstall execute --plan-id <PLAN_ID> --confirm
mb uninstall execute --plan-id <PLAN_ID> --confirm --dry-run --restore-point --json
mb uninstall report --plan-id <PLAN_ID>
mb uninstall report --plan-id <PLAN_ID> --json
```

#### UI (Quick Uninstall by Path)

```powershell
mb ui --help
mb ui "C:\Path\To\App"
mb ui "C:\Path\To\App" --yes --dry-run --json
mb ui "C:\Path\To\App" --yes --exclude "*cache*" --exclude "*logs*"
mb ui "C:\Path\To\App" --yes --no-restore-point --elevate
```

Aliases:

```powershell
mb -ui "C:\Path\To\App"
mb --ui "C:\Path\To\App"
mb /ui "C:\Path\To\App"
```

#### Config

```powershell
mb config --help
mb config get
mb config get output_format
mb config get output_format --json
mb config set output_format json
mb config set dry_run_default true
```

#### Doctor

```powershell
mb doctor --help
mb doctor
mb doctor --json
```

## Safety Notes

- IP geolocation is always approximate (`Approximate (IP-based)`).
- Deep uninstall does not perform anti-forensics (no log wiping or tampering).
- `ui` and uninstall execution require explicit confirmation.
- Non-admin `ui` runs partial cleanup and skips admin-only artifacts.
- `ui --elevate` can relaunch into an Administrator session for deeper cleanup.
