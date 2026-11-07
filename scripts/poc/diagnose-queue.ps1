<#
.SYNOPSIS
Informa el estado de la cola de la POC 01 sin modificar paquetes, impresoras ni el registro.
#>
$ErrorActionPreference = "Continue"
. "$PSScriptRoot\TrustChecks.ps1"

Write-Host "Diagnóstico de solo lectura. No se desinstala el paquete, no se eliminan impresoras y no se escribe en el registro."
Write-Host ""

$service = Get-Service -Name Spooler -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Spooler: $($service.Status)"
}
else {
    Write-Host "Spooler: no se pudo consultar."
}

$packages = @()
try {
    $packages = @(Get-AppxPackage -Name $script:PocPackageName -ErrorAction Stop)
}
catch {
    Write-Host "No se pudo consultar el paquete; su ausencia no está confirmada: $($_.Exception.Message)"
}
if ($packages.Count -eq 0) {
    Write-Host "La consulta no devolvió un paquete $($script:PocPackageName) para esta cuenta."
}
else {
    foreach ($package in $packages) {
        Write-Host "Paquete: $($package.PackageFullName)"
        Write-Host "Estado: $($package.Status)"
        Write-Host "Versión: $($package.Version)"
    }
}

try {
    $printers = @(Get-Printer -ErrorAction Stop | Where-Object { $_.Name -eq $script:PocPrinterName })
    Write-Host "Colas con el nombre $($script:PocPrinterName): $($printers.Count)"
    foreach ($printer in $printers) {
        Write-Host "Controlador: $($printer.DriverName). Puerto: $($printer.PortName)"
    }
}
catch {
    Write-Host "No se pudieron consultar las colas; su ausencia no está confirmada: $($_.Exception.Message)"
}

$portRoot = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Virtual Port Monitor\Ports"
$portNames = @()
try {
    if (Test-Path -LiteralPath $portRoot) {
        $portNames = @(Get-ChildItem -LiteralPath $portRoot -ErrorAction Stop | Where-Object {
                $_.PSChildName -like "*Neobash*" -or
                $_.PSChildName -like "*npb-prueba*" -or
                $_.PSChildName -like "*PrintBridge*" -or
                $_.PSChildName -like "*neobash-print-bridge*"
            } | ForEach-Object { $_.PSChildName })
    }
    else {
        Write-Host "No existe la clave de puertos del monitor virtual, o esta cuenta no puede leerla."
    }
}
catch {
    Write-Host "No se pudo leer la clave de puertos del monitor virtual: $($_.Exception.Message)"
    Write-Host "La lectura falló. No se intentó borrar ninguna clave."
}

if ($portNames.Count -eq 0) {
    Write-Host "No hay puertos del monitor virtual cuyo nombre coincida con esta POC."
}
else {
    Write-Host "Puertos del monitor virtual que conservan el nombre de esta POC:"
    foreach ($portName in $portNames) {
        Write-Host $portName
    }
    Write-Host "Esos registros no se eliminan aquí. Si la cola sigue ausente después de la actualización, la limpieza queda para otra autorización."
}
