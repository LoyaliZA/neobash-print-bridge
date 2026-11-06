<#
.SYNOPSIS
Comprueba el MSIX firmado y, solo con autorización explícita y una consola elevada, confía el certificado público e instala la cola.
#>
param(
    [switch]$ConfirmSystemChanges
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\TrustChecks.ps1"
Set-Location $script:PocRoot

$msixPath = Get-PocMsixPath
$signature = Get-PocSignerSignature -MsixPath $msixPath
$content = Read-PocPackageContent -MsixPath $msixPath
if ($content.Publisher -ne $signature.SignerCertificate.Subject) {
    throw "El Publisher del manifiesto no es el firmante del MSIX."
}

Assert-PocPfxMatchesSigner -Thumbprint $signature.SignerCertificate.Thumbprint
$publicCert = Export-PocPublicCertificate -Signer $signature.SignerCertificate
$elevated = Test-PocElevated
$unlock = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -ErrorAction SilentlyContinue

Write-Host "Paquete: $msixPath"
Write-Host "Identidad: $($content.Name) $($content.Version)"
Write-Host "Cola declarada: $($content.Printer)"
Write-Host "Firmante: $($signature.SignerCertificate.Subject)"
Write-Host "Thumbprint: $($publicCert.Thumbprint)"
Write-Host "Certificado público: $($publicCert.CerPath)"
Write-Host "Estado Authenticode: $($signature.Status). $($signature.StatusMessage)"
Write-Host "Almacén previsto: $($script:PocTrustedStore)"
$developerMode = if ([string]::IsNullOrWhiteSpace([string]$unlock.AllowDevelopmentWithoutDevLicense)) { "no definido" } else { [string]$unlock.AllowDevelopmentWithoutDevLicense }
$sideloadPolicy = if ([string]::IsNullOrWhiteSpace([string]$unlock.AllowAllTrustedApps)) { "no definido" } else { [string]$unlock.AllowAllTrustedApps }
Write-Host "Modo de desarrollador (solo lectura): AllowDevelopmentWithoutDevLicense=$developerMode. No se exige ni se modifica."
Write-Host "Sideloading de confianza (solo lectura): AllowAllTrustedApps=$sideloadPolicy. No se modifica en este intento."
Write-Host "Consola elevada: $elevated"

if (-not $ConfirmSystemChanges) {
    Write-Host ""
    Write-Host "Comprobación terminada. No se modificó la confianza ni se registró el paquete."
    Write-Host "Para instalar, abre PowerShell elevado con la misma cuenta que imprimirá y ejecuta:"
    Write-Host "powershell -ExecutionPolicy Bypass -File .\scripts\poc\install.ps1 -ConfirmSystemChanges"
    exit 2
}

if (-not $elevated) {
    Write-Host ""
    Write-Host "La autorización está presente, pero esta consola no es de administrador."
    Write-Host "No se importó el certificado ni se registró el paquete. Vuelve a ejecutar el mismo comando en una consola elevada."
    exit 3
}

Write-Host ""
Write-Host "Autorización recibida. Se confiará solo el certificado público y después se intentará el registro estándar del MSIX."

$already = @(Get-AppxPackage -Name $script:PocPackageName)
if ($already.Count -gt 1) {
    throw "Hay más de un paquete $($script:PocPackageName). No se instaló ni se retiró ninguno."
}

$trustBefore = Get-PocTrustSnapshot
$printersBefore = Get-PocPrinterSnapshot
$defaultBefore = Get-PocDefaultPrinterName
$packagesBefore = Get-PocPackageFullNames
$imported = $false

try {
    $imported = Import-PocTrustedCertificate -CerPath $publicCert.CerPath -Thumbprint $publicCert.Thumbprint
    $trustAfterImport = Get-PocTrustSnapshot
    if ($imported) {
        Test-PocTrustSnapshot -Before $trustBefore -After $trustAfterImport -Change Add -Thumbprint $publicCert.Thumbprint
    }
    else {
        Test-PocTrustSnapshot -Before $trustBefore -After $trustAfterImport -Change Unchanged -Thumbprint $publicCert.Thumbprint
    }

    if ($already.Count -eq 0) {
        Add-AppxPackage -Path $msixPath
    }
    else {
        Write-Host "El paquete ya estaba registrado para esta cuenta. No se volvió a instalar."
    }
}
catch {
    if ($imported) {
        Write-Host "El certificado público quedó en $($script:PocTrustedStore)."
        Write-Host "Thumbprint para la limpieza: $($publicCert.Thumbprint)"
        Write-Host "El paquete no quedó registrado. Para retirar solo ese certificado:"
        Write-Host "powershell -ExecutionPolicy Bypass -File .\scripts\poc\uninstall.ps1 -RemoveTrustedCertificate"
    }
    Write-PocInstallFailureGuidance -Failure $_
    exit 1
}

$trustAfter = Get-PocTrustSnapshot
Test-PocTrustSnapshot -Before $trustAfterImport -After $trustAfter -Change Unchanged -Thumbprint $publicCert.Thumbprint

$installed = @(Get-AppxPackage -Name $script:PocPackageName)
if ($installed.Count -ne 1 -or $installed[0].Name -ne $script:PocPackageName) {
    throw "El paquete registrado no es exactamente $($script:PocPackageName)."
}
Test-PocPackageDelta -Before $packagesBefore -After (Get-PocPackageFullNames) -Phase Install -PackageFullName $installed[0].PackageFullName

$printersAfter = Get-PocPrinterSnapshot
Test-PocPrinterIntegrity -Before $printersBefore -After $printersAfter -Phase Install
$queue = @($printersAfter | Where-Object { $_.Name -eq $script:PocPrinterName })
if ($queue.Count -ne 1) {
    throw "El paquete quedó registrado, pero no aparece exactamente una cola $($script:PocPrinterName)."
}

$defaultAfter = Get-PocDefaultPrinterName
if ($defaultBefore -ne $defaultAfter) {
    Write-Warning "La impresora predeterminada cambió de '$defaultBefore' a '$defaultAfter'. Se intenta restaurar la anterior."
    Restore-PocDefaultPrinter -Name $defaultBefore | Out-Null
    $defaultAfter = Get-PocDefaultPrinterName
    if ($defaultBefore -ne $defaultAfter) {
        Write-Warning "La impresora predeterminada sigue siendo '$defaultAfter'."
    }
}

Write-Host "Cola registrada: $($queue[0].Name)"
Write-Host "Thumbprint confiado: $($publicCert.Thumbprint)"
Write-Host "Impresora predeterminada antes: $defaultBefore"
Write-Host "Impresora predeterminada después: $defaultAfter"
