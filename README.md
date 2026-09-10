# PathManager (`pathman`)

> A modern Windows PATH environment variable & command catalog manager. Automatically prevents PATH bloat, creates short commands for any runnable script or binary, organizes PowerShell completions, and diagnoses PATH health.

---

## ⚡ Features

- **Single Shim Directory on PATH**: Stops Windows PATH from growing uncontrollably. Instead of adding every tool folder to PATH, `pathman` links executables and scripts into a single unified shim directory (`%LOCALAPPDATA%\PathManager\shims`).
- **Link Any Runnable**: Turn `.exe`, `.cmd`, `.bat`, `.ps1`, `.py`, or custom scripts into native terminal commands (`pathman link .\script.py`).
- **Raw `REG_EXPAND_SZ` Registry Updates**: Modifies User PATH directly in Windows Registry (`HKCU\Environment`) while preserving unexpanded environment variables (`%SystemRoot%`, `%USERPROFILE%`). **Never calls `setx`** (preventing `setx`'s 1024-character truncation and data loss).
- **PowerShell Tab Completions Organizer**: One folder (`%LOCALAPPDATA%\PathManager\completions\`) plus one `$PROFILE` hook. Pathman does not invent your tool's Tab completions; it stores `.ps1` completers you generate and loads them in every new pwsh. Use `register-file`, `register-command`, or a best-effort `from-help`.
- **Resolution Tracer (`pathman why`)**: Traces command resolution across PathManager shims, PowerShell functions, Machine PATH, User PATH, PATHEXT, and WindowsApps Store aliases to explain which binary runs and identify shadowing conflicts.
- **Doctor & Health Diagnostics (`pathman doctor`)**: Checks PATH character limits (GUI limit 2047, Cmd limit 8191, Win32 limit 32767), detects dead directories (with non-blocking 200ms UNC network timeouts), discovers duplicates, and repairs drift with `--repair`.
- **Instant Rollback (`pathman undo`)**: Snapshots catalog and User PATH before every mutation. Undo mistakes with a single command.

---

## 🚀 Quick Start

### 1. Install

From PowerShell (do **not** run as Administrator):

```powershell
irm https://github.com/jag-a-i/PathMan/releases/latest/download/install.ps1 | iex
```

That downloads the self-contained win-x64 zip from GitHub Releases, puts `pathman` on your User PATH, and registers completions in this session. From a clone (needs the .NET 8 SDK):

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

## PowerShell completions (new-user guide)

Pathman is a **mailbox for completion scripts**, not a completions author. After install you get:

1. A folder: `%LOCALAPPDATA%\PathManager\completions\`
2. One marked block in `$PROFILE` that, on every new pwsh:
   - prepends the shim directory to `$env:PATH` (so linked names win inside PowerShell)
   - dot-sources every `*.ps1` in that folder
   - registers Tab completion for the `pathman` CLI itself

Linked commands (`pathman link .\hello.ps1`) do **not** automatically get Tab completion. Completers only exist if a `.ps1` is in the folder.

`install.ps1` already runs `pathman completions install` and registers the `pathman` completer in the **current** session. Later shells pick up the hook from `$PROFILE`. If Tab does nothing, run `pathman doctor` — a missing hook is reported as INFO with the fix command.

### 1. Turn the hook on (if you skipped install.ps1)

```powershell
pathman completions install                 # pwsh 7+  ($HOME\Documents\PowerShell\...)
pathman completions install --windows-powershell   # Windows PowerShell 5.1
. $PROFILE                                  # load in this session
```

If the profile file cannot be written, Pathman prints the snippet to paste. Completions are PowerShell scripts, so they are **trusted code** at shell start.

### 2. Put a completer in the folder (Stage 1 — you generate, Pathman stores)

Most tools can emit a PowerShell completer. Redirect that to a file, then register it:

```powershell
# Examples of generators (the tool's, not Pathman's):
someprogram --completions powershell > .\someprogram.ps1
gh completion powershell > .\gh.ps1
rustup completions powershell > .\rustup.ps1

pathman completions register-file .\someprogram.ps1
pathman completions register-file .\gh.ps1 --name gh
. $PROFILE
```

`register-file` **copies** the `.ps1` to `%LOCALAPPDATA%\PathManager\completions\<name>.ps1`. The command name defaults to the file name without extension. Use `--name` to override. Use `--force` to overwrite an existing completer.

If that name is already in the catalog (`pathman link`), Pathman stores `completer: completions/<name>.ps1` on the catalog row. The hook still loads **every** `*.ps1` in the folder, catalog or not.

You can also drop a file in by hand:

```powershell
Copy-Item .\someprogram.ps1 $env:LOCALAPPDATA\PathManager\completions\
```

### 3. Let Pathman run the generator (Stage 2)

Same result, one step. Pathman runs the command in pwsh, captures **stdout**, and writes the folder file:

```powershell
pathman completions register-command "someprogram --completions powershell"
pathman completions register-command "gh completion powershell" --name gh
pathman completions register-command rustup completions powershell --name rustup
```

The command name defaults to the first token (basename without `.exe`). Non-zero exit or empty stdout fails with a named error and a `pathman doctor` hint.

This runs whatever you pass. Only use generators you trust — the captured script is executed at every future shell start.

### 4. Best-effort parse of `--help` (Stage 3)

When a tool has no completer generator, Pathman can scrape flags (and a `Commands:` section) from `--help`, falling back to `-h`:

```powershell
pathman completions from-help git
pathman completions from-help "docker compose" --name docker
```

This is a **heuristic**. It will miss nested flags, value completions, and unusual help layouts. Prefer `register-file` / `register-command` when the tool can emit a real script.

### After registering

- New pwsh windows load the folder automatically.
- This session: `. $PROFILE`
- Commands still run if the hook is missing; Tab does not. `pathman doctor` says so.

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
| `pathman completions install` | Installs the `$PROFILE` hook (load folder + `pathman` Tab) | `--windows-powershell`, `--json` |
| `pathman completions register-file <file.ps1>` | Copies a completer `.ps1` into the completions folder | `--name`, `--force`, `--json` |
| `pathman completions register-command <cmd>` | Runs a generator command and stores its stdout as the completer | `--name`, `--force`, `--json` |
| `pathman completions from-help <cmd>` | Best-effort native completer from `--help` / `-h` | `--name`, `--force`, `--json` |
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

Every push and pull request runs restore → Release build → `dotnet test` → publish (`.github/workflows/ci.yml`).

CD (`.github/workflows/release.yml`) then ships a self-contained `pathman-win-x64.zip`:

- Push to `master` / `main` updates the **nightly** pre-release
- A version tag publishes a stable GitHub Release

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Install from the stable release with `irm …/releases/latest/download/install.ps1 | iex`. The zip is also attached to the Actions run.
