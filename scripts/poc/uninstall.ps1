<#
.SYNOPSIS
Quita solo el paquete de la POC 01. El certificado público se retira únicamente con -RemoveTrustedCertificate.
#>
param(
    [switch]$RemoveTrustedCertificate
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\TrustChecks.ps1"
Set-Location $script:PocRoot

$thumbprint = $null
if ($RemoveTrustedCertificate) {
    $thumbprint = Get-PocRecordedThumbprint
    if (-not (Test-PocElevated)) {
        Write-Host "Para retirar el certificado hace falta una consola elevada. No se modificó el sistema."
        exit 3
    }
}

$packages = @(Get-AppxPackage -Name $script:PocPackageName)
if ($packages.Count -gt 1) {
    throw "Hay más de un paquete $($script:PocPackageName). No se retiró ninguno."
}

if ($packages.Count -eq 0 -and -not $RemoveTrustedCertificate) {
    Write-Host "El paquete $($script:PocPackageName) no está instalado. No se modificó ninguna impresora ni ningún certificado."
    exit 0
}

if ($packages.Count -eq 0) {
    $printersBefore = Get-PocPrinterSnapshot
    $packagesBefore = Get-PocPackageFullNames
    $trustBefore = Get-PocTrustSnapshot
    $removed = Remove-PocTrustedCertificate -Thumbprint $thumbprint
    $expectedChange = "Unchanged"
    if ($removed) { $expectedChange = "Remove" }
    Test-PocTrustSnapshot -Before $trustBefore -After (Get-PocTrustSnapshot) -Change $expectedChange -Thumbprint $thumbprint
    Test-PocPackageDelta -Before $packagesBefore -After (Get-PocPackageFullNames) -Phase Install -PackageFullName $null
    Test-PocPrintersUnchanged -Before $printersBefore -After (Get-PocPrinterSnapshot)
    Write-Host "No había paquete instalado. La limpieza de confianza quedó limitada al thumbprint $thumbprint."
    exit 0
}

$trustBefore = Get-PocTrustSnapshot
$printersBefore = Get-PocPrinterSnapshot
$defaultBefore = Get-PocDefaultPrinterName
$packagesBefore = Get-PocPackageFullNames

if ($packages.Count -eq 1) {
    $package = $packages[0]
    if ($package.Name -ne $script:PocPackageName) {
        throw "Se rechazó la desinstalación porque el nombre del paquete no coincide."
    }

    Remove-AppxPackage -Package $package.PackageFullName

    $remaining = @(Get-AppxPackage -Name $script:PocPackageName)
    if ($remaining.Count -ne 0) {
        throw "El paquete sigue registrado. No se llamó a Remove-Printer."
    }
    Test-PocPackageDelta -Before $packagesBefore -After (Get-PocPackageFullNames) -Phase Uninstall -PackageFullName $package.PackageFullName
    Test-PocPrinterIntegrity -Before $printersBefore -After (Get-PocPrinterSnapshot) -Phase Uninstall
    Test-PocTrustSnapshot -Before $trustBefore -After (Get-PocTrustSnapshot) -Change Unchanged -Thumbprint $thumbprint

    $defaultAfter = Get-PocDefaultPrinterName
    if ($defaultBefore -and $defaultBefore -ne $script:PocPrinterName -and $defaultBefore -ne $defaultAfter) {
        Write-Warning "La impresora predeterminada cambió de '$defaultBefore' a '$defaultAfter'. Se intenta restaurar la anterior."
        Restore-PocDefaultPrinter -Name $defaultBefore | Out-Null
        $defaultAfter = Get-PocDefaultPrinterName
    }
    elseif ($defaultBefore -eq $script:PocPrinterName -and $defaultAfter -ne $defaultBefore) {
        Write-Warning "La predeterminada era la cola de prueba y Windows pasó a '$defaultAfter'."
    }

    Write-Host "Se quitó solo $($package.PackageFullName)."
    Write-Host "Impresora predeterminada antes: $defaultBefore"
    Write-Host "Impresora predeterminada después: $defaultAfter"
    Write-Host "Impresoras restantes:"
    Get-PocPrinterSnapshot | Format-Table -AutoSize
}

if ($RemoveTrustedCertificate) {
    $printersBeforeCert = Get-PocPrinterSnapshot
    $packagesBeforeCert = Get-PocPackageFullNames
    $removed = Remove-PocTrustedCertificate -Thumbprint $thumbprint
    $expectedChange = "Unchanged"
    if ($removed) { $expectedChange = "Remove" }
    Test-PocTrustSnapshot -Before $trustBefore -After (Get-PocTrustSnapshot) -Change $expectedChange -Thumbprint $thumbprint
    Test-PocPrintersUnchanged -Before $printersBeforeCert -After (Get-PocPrinterSnapshot)
    Test-PocPackageDelta -Before $packagesBeforeCert -After (Get-PocPackageFullNames) -Phase Install -PackageFullName $null
    Write-Host "Limpieza de confianza limitada al thumbprint $thumbprint en $($script:PocTrustedStore)."
}
