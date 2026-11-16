$ErrorActionPreference = 'Stop'

Write-Host '=== building RaiDriveAdGuard.exe ==='

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    Write-Host "  ERROR: csc.exe not found at $csc" -ForegroundColor Red
    Write-Host '  Install .NET Framework 4.x, or build with any C# compiler.'
    exit 1
}
Write-Host "  compiler: $csc"

Get-Process RaiDriveAdGuard -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  stop    : pid $($_.Id)"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 500

Push-Location $PSScriptRoot
try {
    & $csc -nologo -target:winexe -optimize+ -platform:x64 `
        -out:RaiDriveAdGuard.exe AdGuard.cs
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  BUILD FAILED' -ForegroundColor Red
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}

$out = Join-Path $PSScriptRoot 'RaiDriveAdGuard.exe'
if (Test-Path $out) {
    $size = [math]::Round((Get-Item $out).Length / 1KB, 1)
    Write-Host "  built   : RaiDriveAdGuard.exe ($size KB)" -ForegroundColor Green
} else {
    Write-Host '  ERROR: output missing' -ForegroundColor Red
    exit 1
}
