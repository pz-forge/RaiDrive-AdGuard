$ErrorActionPreference = 'Stop'

Write-Host '=== RaiDrive AdGuard installer ==='

$src = Join-Path $PSScriptRoot 'RaiDriveAdGuard.exe'
if (-not (Test-Path $src)) {
    Write-Host "  ERROR: RaiDriveAdGuard.exe not found next to this script." -ForegroundColor Red
    Write-Host '  Run build.ps1 first.'
    exit 1
}

$dst = Join-Path $env:LOCALAPPDATA 'RaiDriveAdGuard'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Write-Host "  target : $dst"

Get-Process RaiDriveAdGuard -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  stop   : pid $($_.Id)"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 500

Copy-Item $src (Join-Path $dst 'RaiDriveAdGuard.exe') -Force
Write-Host '  copied : RaiDriveAdGuard.exe'

if (Test-Path (Join-Path $PSScriptRoot 'README.md')) {
    Copy-Item (Join-Path $PSScriptRoot 'README.md') (Join-Path $dst 'README.md') -Force
}

$exe = Join-Path $dst 'RaiDriveAdGuard.exe'
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'RaiDriveAdGuard' -Value $exe -Type String
Write-Host '  autorun: HKCU\...\Run\RaiDriveAdGuard'

Start-Process $exe
Start-Sleep -Seconds 1

$p = Get-Process RaiDriveAdGuard -ErrorAction SilentlyContinue
if ($p) {
    Write-Host "  running: pid $($p.Id)" -ForegroundColor Green
} else {
    Write-Host '  WARNING: process not running' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Done. Log: ' -NoNewline
Write-Host (Join-Path $dst 'guard.log')
Write-Host 'To uninstall, run uninstall.ps1'
