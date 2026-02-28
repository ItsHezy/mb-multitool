# Membran MultiTool v1

Windows-first MultiTool focused on two modules:

- IP geolocation lookup (explicitly approximate for IP-only lookups).
- Safe deep uninstall workflow (list -> preview -> execute -> report).

## Projects

- `Membran.MultiTool.Core`: shared models, safety, persistence, command helpers.
- `Membran.MultiTool.Geo`: provider chain (`ipapi.co` -> `ipinfo.io`), timeout/retry, confidence, cache.
- `Membran.MultiTool.Uninstall`: discovery, preview planner, execution engine.
- `Membran.MultiTool.Cli`: command-line interface.
- `Membran.MultiTool.Gui`: WPF GUI with Geo, Uninstall, Reports, Settings tabs.
- `Membran.MultiTool.Tests`: unit tests.

## Build

```powershell
dotnet restore Membran.MultiTool.slnx
dotnet build Membran.MultiTool.slnx -c Release
dotnet test Membran.MultiTool.Tests/Membran.MultiTool.Tests.csproj -c Release
```

## Run CLI

```powershell
dotnet run --project Membran.MultiTool.Cli -- --help
```

## Install `mb` Command (CMD/PowerShell)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-mb.ps1
```

After opening a new terminal:

```powershell
mb --help
```

### Quick `mb` Modes

```powershell
mb ip 192.0.0.1
mb ui "C:/hezy/programms/ui/" --yes --dry-run
mb -IP 192.0.0.1
mb -ui "C:/hezy/programms/ui/" --yes --dry-run
```

- `ip`: quick geolocation lookup command.
- `ui`: quick deep-clean by file/folder path (uses uninstall planner + executor).
- `-IP` and `-ui`: backward-compatible aliases for `ip` and `ui`.
- `--yes`: skip interactive `YES` prompt for `ui`.
- `--dry-run`: simulate cleanup without deleting anything.
- `--json`: JSON output.
- `--exclude`: skip artifacts that match wildcard patterns.
- `--elevate`: relaunch `ui` in an Administrator terminal when needed.

## Build Portable Package

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-portable-mb.ps1
```

Outputs:

- `dist\mb-portable\mb.exe`
- `dist\mb-portable\mb.cmd`
- `dist\mb-portable\install-portable.cmd`
- `dist\mb-portable\remove-portable.cmd`
- `dist\mb-portable\README.txt`
- `dist\mb-portable.zip`

Use `install-portable.cmd` to add that folder to **User PATH** without admin.

### Geo

```powershell
dotnet run --project Membran.MultiTool.Cli -- geo lookup --ip self
dotnet run --project Membran.MultiTool.Cli -- geo lookup --ip 8.8.8.8 --json
dotnet run --project Membran.MultiTool.Cli -- ip 8.8.8.8 --json
dotnet run --project Membran.MultiTool.Cli -- ip batch --file .\ips.txt --json
```

### Uninstall

```powershell
dotnet run --project Membran.MultiTool.Cli -- uninstall list
dotnet run --project Membran.MultiTool.Cli -- uninstall preview --app-id <APP_ID> --json
dotnet run --project Membran.MultiTool.Cli -- uninstall execute --plan-id <PLAN_ID> --confirm
dotnet run --project Membran.MultiTool.Cli -- uninstall report --plan-id <PLAN_ID> --json
dotnet run --project Membran.MultiTool.Cli -- ui "C:/path/to/app" --yes --dry-run
dotnet run --project Membran.MultiTool.Cli -- ui "C:/path/to/app" --yes --exclude "*cache*" --exclude "*logs*"
dotnet run --project Membran.MultiTool.Cli -- ui "C:/path/to/app" --yes --elevate
```

`ui` executes partial cleanup in non-admin sessions (admin-only artifacts are skipped and reported).  
When elevated, `ui` attempts full cleanup for eligible artifacts.

### Config

```powershell
dotnet run --project Membran.MultiTool.Cli -- config get
dotnet run --project Membran.MultiTool.Cli -- config get output_format
dotnet run --project Membran.MultiTool.Cli -- config set output_format json
dotnet run --project Membran.MultiTool.Cli -- config set dry_run_default true
```

### Doctor

```powershell
dotnet run --project Membran.MultiTool.Cli -- doctor
dotnet run --project Membran.MultiTool.Cli -- doctor --json
```

## Run GUI

```powershell
dotnet run --project Membran.MultiTool.Gui
```

## Manual Rule JSON

Use with `uninstall preview --manual-rule <path>` or in GUI.

```json
{
  "namePattern": "MyApp",
  "filePaths": [
    "C:\\\\ProgramData\\\\MyApp",
    "%LocalAppData%\\\\MyApp"
  ],
  "registryPaths": [
    "HKCU\\\\Software\\\\MyApp",
    "HKLM\\\\Software\\\\MyVendor\\\\MyApp"
  ],
  "processNames": [
    "MyApp",
    "myapp-helper.exe"
  ]
}
```

## Safety Notes

- IP geolocation is always labeled `Approximate (IP-based)` and should not be treated as exact physical location.
- The uninstall module is designed for deep cleanup but does **not** attempt anti-forensics (for example, log wiping).
- File deletion is constrained by allowed roots to reduce destructive mistakes.
- Execution requires explicit confirmation (`--confirm` in CLI, checkbox in GUI).
- `ui` runs in non-admin mode with partial cleanup: admin-only operations are skipped and reported.
- `ui --elevate` can relaunch into an Administrator window for fuller cleanup.
