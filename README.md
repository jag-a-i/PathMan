# PathManager (`pathman`)

> A modern Windows PATH environment variable & command catalog manager. Automatically prevents PATH bloat, creates short commands for any runnable script or binary, organizes PowerShell completions, and diagnoses PATH health.

---

## ⚡ Features

- **Single Shim Directory on PATH**: Stops Windows PATH from growing uncontrollably. Instead of adding every tool folder to PATH, `pathman` links executables and scripts into a single unified shim directory (`%LOCALAPPDATA%\PathManager\shims`).
- **Link Any Runnable**: Turn `.exe`, `.cmd`, `.bat`, `.ps1`, `.py`, or custom scripts into native terminal commands (`pathman link .\script.py`).
- **Raw `REG_EXPAND_SZ` Registry Updates**: Modifies User PATH directly in Windows Registry (`HKCU\Environment`) while preserving unexpanded environment variables (`%SystemRoot%`, `%USERPROFILE%`). **Never calls `setx`** (preventing `setx`'s 1024-character truncation and data loss).
- **PowerShell Tab Completions Organizer**: Organizes completion scripts into `%LOCALAPPDATA%\PathManager\completions\` and auto-loads them via a lightweight `$PROFILE` hook.
- **Resolution Tracer (`pathman why`)**: Traces command resolution across PathManager shims, PowerShell functions, Machine PATH, User PATH, PATHEXT, and WindowsApps Store aliases to explain which binary runs and identify shadowing conflicts.
- **Doctor & Health Diagnostics (`pathman doctor`)**: Checks PATH character limits (GUI limit 2047, Cmd limit 8191, Win32 limit 32767), detects dead directories (with non-blocking 200ms UNC network timeouts), discovers duplicates, and repairs drift with `--repair`.
- **Instant Rollback (`pathman undo`)**: Snapshots catalog and User PATH before every mutation. Undo mistakes with a single command.

---

## 🚀 Quick Start

### 1. Install

Clone the repository and run `install.ps1` from PowerShell (do not run as Administrator):

```powershell
.\install.ps1
```

### 2. Link a Script or Executable

```powershell
# Link a PowerShell script
pathman link .\hello.ps1

# Link a Python script
pathman link .\my_tool.py

# Link with a custom command name
pathman link "C:\CustomTools\downloader_v2.exe" dl
```

### 3. Run Your Command

Open any terminal or use your current session immediately:

```powershell
hello
my_tool --help
dl
```

---

## 📖 CLI Reference

| Command | Description | Notable Flags |
|---|---|---|
| `pathman` | Shows quick status summary and usage help | `--json`, `--help` |
| `pathman link <file> [name]` | Links a file as a runnable command | `--force`, `--host <exe>`, `--i-trust-this`, `--json` |
| `pathman unlink <name>` | Unlinks a command and deletes its shim | `--json` |
| `pathman list` | Lists all linked commands and their targets | `--json` |
| `pathman why <name>` | Traces command resolution and PATH shadowing | `--json` |
| `pathman doctor` | Inspects PATH limits, dead folders, duplicates, and catalog drift | `--repair`, `--json` |
| `pathman path` | Displays User and Machine PATH entries | `--json` |
| `pathman path add <dir>` | Safely adds a directory to User PATH | `--prepend`, `--json` |
| `pathman path remove <dir>`| Safely removes a directory from User PATH | `--force`, `--json` |
| `pathman undo` | Restores the previous snapshot of catalog and User PATH | `--json` |
| `pathman completions install` | Installs PowerShell tab completions hook into `$PROFILE` | `--windows-powershell`, `--json` |
| `pathman uninstall` | Uninstalls PathManager and cleans User PATH | `--keep-shims`, `--json` |

---

## 🛠️ Architecture

```
%LOCALAPPDATA%\PathManager\
  state.json              # Catalog database (source of truth)
  shims\                  # The single directory added to User PATH
    pathman.exe           # CLI tool
    shim.exe              # Template native runner
    <name>.exe            # Native shim for linked command
    <name>.exe.meta       # Sidecar metadata (recovery backup)
  completions\            # Organized PowerShell completion scripts
    <name>.ps1
  snapshots\              # Automated undo backups
    <timestamp>\
      state.json
      UserPath.txt
  audit.jsonl             # Append-only audit log
```

---

## 🧪 Testing

Run the xUnit test suite (completely isolated from the host machine's real PATH):

```powershell
dotnet test
```
