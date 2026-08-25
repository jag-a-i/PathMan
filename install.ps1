# PathManager (pathman) Installation Script for Windows PowerShell & pwsh
# Requires: Windows, Non-Elevated execution

$ErrorActionPreference = 'Stop'

# 1. Security Gate: Refuse elevated installation
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Write-Error "Security Policy Refusal: Do not run install.ps1 as Administrator. PathManager manages User-level PATH in HKCU for your individual user account."
    exit 1
}

# 2. Setup PM_HOME directory structure
$pmHome = if ($env:PM_HOME) { $env:PM_HOME } else { Join-Path $env:LOCALAPPDATA "PathManager" }
$shimsDir = Join-Path $pmHome "shims"
$completionsDir = Join-Path $pmHome "completions"
$snapshotsDir = Join-Path $pmHome "snapshots"

New-Item -ItemType Directory -Force -Path $shimsDir | Out-Null
New-Item -ItemType Directory -Force -Path $completionsDir | Out-Null
New-Item -ItemType Directory -Force -Path $snapshotsDir | Out-Null

Write-Host "Installing PathManager to $pmHome..." -ForegroundColor Cyan

# 3. Build & Publish Binaries if running locally, or copy existing binaries
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path (Join-Path $scriptDir "src\PathManager.Cli\PathManager.Cli.csproj")) {
    Write-Host "Publishing binaries..." -ForegroundColor DarkGray
    & dotnet publish (Join-Path $scriptDir "src\PathManager.Cli\PathManager.Cli.csproj") -c Release -o $shimsDir --nologo -v q
    & dotnet publish (Join-Path $scriptDir "src\PathManager.Shim\PathManager.Shim.csproj") -c Release -o $shimsDir --nologo -v q
}

$pathmanExe = Join-Path $shimsDir "pathman.exe"
if (-not (Test-Path $pathmanExe)) {
    Write-Error "pathman.exe not found at $pathmanExe. Please ensure dotnet SDK is installed or run from the repo root."
    exit 1
}

# 4. Ensure Shims directory is in HKCU User PATH (via raw registry REG_EXPAND_SZ)
$regKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
if ($null -eq $regKey) {
    $regKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Environment")
}
$currentPath = $regKey.GetValue("Path", "", [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
$currentEntries = if ($currentPath) { $currentPath -split ';' | Where-Object { $_ -ne '' } } else { @() }

if ($currentEntries -notcontains $shimsDir) {
    $newPath = "$shimsDir;" + ($currentEntries -join ';')
    $regKey.SetValue("Path", $newPath, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    Write-Host "Added $shimsDir to User PATH (HKCU\Environment)." -ForegroundColor Green
}
$regKey.Close()

# 5. Live update current session PATH
if ($env:PATH -notlike "$shimsDir;*") {
    $env:PATH = "$shimsDir;$env:PATH"
}

# 6. Install profile completions and register in current session
& $pathmanExe completions install | Out-Null

if (Get-Command Register-ArgumentCompleter -ErrorAction SilentlyContinue) {
    Register-ArgumentCompleter -Native -CommandName pathman -ScriptBlock {
        param($wordToComplete, $commandAst, $cursorPosition)
        $elements = $commandAst.CommandElements
        $verbs = @('link', 'unlink', 'list', 'why', 'doctor', 'path', 'undo', 'completions', 'uninstall', 'upgrade')
        $flags = @('--json', '--help', '-h', '--version', '-v')

        if ($elements.Count -le 2) {
            return ($verbs + $flags) | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        }

        $verb = $elements[1].Value
        switch ($verb) {
            'link' {
                $linkFlags = @('--force', '--host', '--i-trust-this', '--json')
                return $linkFlags | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
            }
            'path' {
                $pathSub = @('add', 'remove')
                return $pathSub | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
            }
            'doctor' {
                $docFlags = @('--repair', '--json')
                return $docFlags | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
            }
            'completions' {
                $compSub = @('install')
                return $compSub | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
            }
            'uninstall' {
                $unFlags = @('--keep-shims', '--json')
                return $unFlags | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
            }
        }
    }
}

# 7. Write demo hello.ps1 in cwd if missing
$demoScript = "hello.ps1"
if (-not (Test-Path $demoScript)) {
    @"
Write-Host "Hello from PathManager!" -ForegroundColor Green
Write-Host "This script is running as a first-class Windows command via native shim."
"@ | Set-Content -Path $demoScript -Encoding utf8
}

Write-Host ""
Write-Host "PathManager installed successfully!" -ForegroundColor Green
Write-Host "  Location:      $pmHome"
Write-Host "  Shims:         $shimsDir"
Write-Host "  This session:  live"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Link the demo script:"
Write-Host "       pathman link .\hello.ps1" -ForegroundColor Yellow
Write-Host "  2. Run it from anywhere:"
Write-Host "       hello" -ForegroundColor Yellow
Write-Host ""
