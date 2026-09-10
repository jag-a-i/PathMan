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

# 3. Obtain binaries: local publish, zip next to this script, or GitHub Release
$scriptPath = $PSCommandPath
if (-not $scriptPath) { $scriptPath = $MyInvocation.MyCommand.Path }
$scriptDir = if ($scriptPath) { Split-Path -Parent $scriptPath } else { $null }
$repo = if ($env:PATHMAN_REPO) { $env:PATHMAN_REPO } else { 'jag-a-i/PathMan' }

function Copy-PathManExes([string]$fromDir, [string]$toDir) {
    $copied = $false
    foreach ($name in @('pathman.exe', 'shim.exe')) {
        $hit = Get-ChildItem -Path $fromDir -Filter $name -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) {
            Copy-Item $hit.FullName (Join-Path $toDir $name) -Force
            $copied = $true
        }
    }
    return $copied
}

if ($scriptDir -and (Test-Path (Join-Path $scriptDir "src\PathManager.Cli\PathManager.Cli.csproj"))) {
    Write-Host "Publishing binaries from source..." -ForegroundColor DarkGray
    & dotnet publish (Join-Path $scriptDir "src\PathManager.Cli\PathManager.Cli.csproj") -c Release -o $shimsDir --nologo -v q
    & dotnet publish (Join-Path $scriptDir "src\PathManager.Shim\PathManager.Shim.csproj") -c Release -o $shimsDir --nologo -v q
} elseif ($scriptDir -and (Test-Path (Join-Path $scriptDir "pathman.exe"))) {
    Write-Host "Copying binaries next to install.ps1..." -ForegroundColor DarkGray
    [void](Copy-PathManExes $scriptDir $shimsDir)
} else {
    Write-Host "Downloading PathManager from GitHub Releases ($repo)..." -ForegroundColor DarkGray
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    } catch { }

    $headers = @{ 'User-Agent' = 'pathman-install' }
    $asset = $null
    foreach ($api in @(
        "https://api.github.com/repos/$repo/releases/latest",
        "https://api.github.com/repos/$repo/releases/tags/nightly"
    )) {
        try {
            $release = Invoke-RestMethod -Uri $api -Headers $headers
            $asset = @($release.assets) | Where-Object { $_.name -like 'pathman-win-x64*.zip' } | Select-Object -First 1
            if ($asset) { break }
        } catch { }
    }

    if (-not $asset) {
        Write-Error "No pathman-win-x64.zip on GitHub Releases for $repo. Clone the repo and run install.ps1, or wait for CI/CD to publish a release."
        exit 1
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("pathman-rel-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'pathman-win-x64.zip'
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    if (-not (Copy-PathManExes $tmp $shimsDir)) {
        Write-Error "The release zip did not contain pathman.exe."
        exit 1
    }
}

$pathmanExe = Join-Path $shimsDir "pathman.exe"
if (-not (Test-Path $pathmanExe)) {
    Write-Error "pathman.exe not found at $pathmanExe. Hint: run from the repo, or install from a GitHub Release."
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
                if ($elements.Count -le 3) {
                    $compSub = @('install', 'register-file', 'register-command', 'from-help')
                    return $compSub | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
                }
                $compFlags = @('--name', '--force', '--json', '--windows-powershell')
                return $compFlags | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
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
