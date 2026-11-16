$ErrorActionPreference = 'SilentlyContinue'

Write-Host '=== RaiDrive AdGuard uninstaller ==='

$dst = Join-Path $env:LOCALAPPDATA 'RaiDriveAdGuard'

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'RaiDriveAdGuard' -ErrorAction SilentlyContinue
Write-Host '  autorun: removed'

Get-Process RaiDriveAdGuard | ForEach-Object {
    Write-Host "  stop   : pid $($_.Id)"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 500

if (Test-Path $dst) {
    Remove-Item $dst -Recurse -Force
    Write-Host "  removed: $dst"
}

Write-Host ''
Write-Host 'Uninstalled. RaiDrive is untouched and will serve ads again.'
