<#
Build the Windows binaries using PyInstaller.

Usage:
  - Open a non-admin PowerShell in the project root with your venv activated.
  - Run:  .\build-win32.ps1

This script builds the Orchestrator (windowless CLI) first, then the GUI,
which bundles the Orchestrator under Orchestrator/Orchestrator.exe.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Step {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][string]$Command
    )
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host "    $Command" -ForegroundColor DarkGray
    & powershell -NoProfile -Command $Command
}

function Assert-Path {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Message
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        throw $Message
    }
}

# 1) Build Orchestrator (no console window)
Invoke-Step -Name "Build Orchestrator" -Command "pyinstaller --noconfirm Orchestrator.spec"

$orchestratorExe = Join-Path -Path (Resolve-Path .).Path -ChildPath "dist/Orchestrator.exe"
Assert-Path -Path $orchestratorExe -Message "Expected '$orchestratorExe' after Orchestrator build. Did the build fail?"

# 2) Build GUI (windowed) and bundle the orchestrator exe
Invoke-Step -Name "Build GUI" -Command "pyinstaller --noconfirm --distpath dist/release BWKTSubtitlePipeline_win32.spec"

# Clean the top-level Orchestrator artifact to keep dist/ focused on the GUI
if (Test-Path -LiteralPath "dist/Orchestrator.exe") {
    Remove-Item -LiteralPath "dist/Orchestrator.exe" -Force
}

$guiExe = Join-Path -Path (Resolve-Path .).Path -ChildPath "dist/release/BWKTSubtitlePipeline/BWKTSubtitlePipeline.exe"
Assert-Path -Path $guiExe -Message "Expected '$guiExe' after GUI build. Did the build fail?"

Write-Host "";
Write-Host "Build complete." -ForegroundColor Green
Write-Host "  Orchestrator:        $orchestratorExe"
Write-Host "  GUI (windowed):      $guiExe"
Write-Host ""
