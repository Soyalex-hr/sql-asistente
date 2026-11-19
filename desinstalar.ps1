<#
    Quita SQL Asistente de SSMS 21 y 22.
    Uso (PowerShell COMO ADMINISTRADOR, con SSMS cerrado):
        powershell -ExecutionPolicy Bypass -File .\desinstalar.ps1
#>
$ErrorActionPreference = "Stop"

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    Write-Host "Cierra SSMS antes de desinstalar." -ForegroundColor Red
    exit 1
}

foreach ($v in @("22", "21")) {
    $ide = "C:\Program Files\Microsoft SQL Server Management Studio $v\Release\Common7\IDE"
    $destino = Join-Path $ide "Extensions\SqlAsistente"
    if (Test-Path $destino) {
        Remove-Item $destino -Recurse -Force
        Set-Content -Path (Join-Path $ide "Extensions\extensions.configurationchanged") -Value (Get-Date).ToString("o")
        Start-Process -FilePath (Join-Path $ide "SSMS.exe") -ArgumentList "/updateconfiguration" -Wait -WindowStyle Hidden
        Write-Host "Quitado de SSMS $v." -ForegroundColor Green
    }
}
