<#
    Instala SQL Asistente en SSMS 21 y/o SSMS 22 (para todos los usuarios).

    Uso (PowerShell COMO ADMINISTRADOR, con SSMS cerrado):
        cd E:\ALX\SqlAsistente
        powershell -ExecutionPolicy Bypass -File .\instalar.ps1

    Opcional:
        -Vsix  ruta del .vsix (por defecto busca el de Release y luego el de Debug)
        -Version 22   instala solo en SSMS 22 (o 21)
#>
param(
    [string]$Vsix = "",
    [ValidateSet("", "21", "22")]
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host "Abre PowerShell como administrador (clic derecho > Ejecutar como administrador)." -ForegroundColor Red
    exit 1
}

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    Write-Host "Cierra SSMS antes de instalar." -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrEmpty($Vsix)) {
    $candidatos = @(
        (Join-Path $PSScriptRoot "src\SqlAsistente\bin\Release\SqlAsistente.vsix"),
        (Join-Path $PSScriptRoot "src\SqlAsistente\bin\Debug\SqlAsistente.vsix")
    )
    $Vsix = $candidatos | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrEmpty($Vsix) -or -not (Test-Path $Vsix)) {
    Write-Host "No se encontró SqlAsistente.vsix. Primero compila el proyecto en Visual Studio (Compilar > Compilar solución)." -ForegroundColor Red
    exit 1
}

$versiones = if ($Version) { @($Version) } else { @("22", "21") }
$instalado = $false

foreach ($v in $versiones) {
    $ide = "C:\Program Files\Microsoft SQL Server Management Studio $v\Release\Common7\IDE"
    if (-not (Test-Path (Join-Path $ide "SSMS.exe"))) { continue }

    $destino = Join-Path $ide "Extensions\SqlAsistente"
    Write-Host "Instalando en SSMS $v -> $destino"

    if (Test-Path $destino) { Remove-Item $destino -Recurse -Force }
    New-Item -ItemType Directory -Path $destino | Out-Null

    # Un .vsix es un .zip: se extrae directamente en la carpeta de extensiones.
    $zip = Join-Path $env:TEMP "SqlAsistente_instalar.zip"
    Copy-Item $Vsix $zip -Force
    Expand-Archive -Path $zip -DestinationPath $destino -Force
    Remove-Item $zip -Force
    Get-ChildItem $destino -Recurse | Unblock-File

    # Avisar a SSMS que cambió la configuración de extensiones.
    $marca = Join-Path $ide "Extensions\extensions.configurationchanged"
    Set-Content -Path $marca -Value (Get-Date).ToString("o")

    Write-Host "  Actualizando la configuración de SSMS $v (unos segundos)..."
    Start-Process -FilePath (Join-Path $ide "SSMS.exe") -ArgumentList "/updateconfiguration" -Wait -WindowStyle Hidden

    $instalado = $true
    Write-Host "  Listo en SSMS $v." -ForegroundColor Green
}

if (-not $instalado) {
    Write-Host "No se encontró SSMS 21 ni 22 en C:\Program Files." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Abre SSMS: verás el menú 'SQL Asistente' en la barra de menús." -ForegroundColor Green
