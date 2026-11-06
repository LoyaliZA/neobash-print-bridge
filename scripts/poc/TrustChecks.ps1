# Comprobaciones de la POC 01. Este archivo no modifica el sistema al cargarse.
$script:PocPublisher = "CN=Neobash Print Bridge POC"
$script:PocPackageName = "Neobash.PrintBridge.VirtualPrinter"
$script:PocPrinterName = "NPB - Prueba Virtual"
$script:PocPrinterUri = "neobash-print-bridge:npb-prueba-virtual"
$script:PocInputFormat = "application/oxps"
$script:PocTrustedStore = "Cert:\LocalMachine\TrustedPeople"
$script:PocRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Test-PocElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-PocMsixPath {
    $packageRoot = Join-Path $script:PocRoot "src\Windows\VirtualPrinter\AppPackages"
    $files = @(Get-ChildItem -Path $packageRoot -Recurse -Filter "Neobash.PrintBridge.VirtualPrinter_*_x64.msix" -File -ErrorAction SilentlyContinue)
    $parsed = @($files | ForEach-Object {
            if ($_.Name -match '^Neobash\.PrintBridge\.VirtualPrinter_(\d+\.\d+\.\d+\.\d+)_x64\.msix$') {
                [pscustomobject]@{
                    Path    = $_.FullName
                    Version = [version]$Matches[1]
                }
            }
        })
    if ($parsed.Count -eq 0) {
        throw "No hay un MSIX de Neobash.PrintBridge.VirtualPrinter. No se instaló nada."
    }

    $highest = @($parsed | Sort-Object Version -Descending | Select-Object -First 1)
    $tied = @($parsed | Where-Object { $_.Version -eq $highest[0].Version })
    if ($tied.Count -ne 1) {
        throw "Hay varios MSIX con la versión $($highest[0].Version). No se instaló ninguno."
    }
    return $highest[0].Path
}

function Get-PocSignerSignature {
    param([Parameter(Mandatory = $true)][string]$MsixPath)

    $signature = Get-AuthenticodeSignature -FilePath $MsixPath
    $acceptable = @("Valid", "UnknownError", "NotTrusted")
    if ($acceptable -notcontains [string]$signature.Status) {
        throw "La firma del MSIX no es aceptable ($($signature.Status)). $($signature.StatusMessage)"
    }
    if (-not $signature.SignerCertificate) {
        throw "El MSIX no incluye un certificado firmante. No se instaló nada."
    }
    if ($signature.SignerCertificate.Subject -ne $script:PocPublisher) {
        throw "El firmante del MSIX es '$($signature.SignerCertificate.Subject)'. Se esperaba '$($script:PocPublisher)'."
    }
    return $signature
}

function Assert-PocPfxMatchesSigner {
    param([Parameter(Mandatory = $true)][string]$Thumbprint)

    $pfxPath = Join-Path $script:PocRoot "local-certs\npb-poc.pfx"
    $passwordPath = Join-Path $script:PocRoot "local-certs\pfx-password.txt"
    if (-not (Test-Path -LiteralPath $pfxPath)) {
        return
    }
    if (-not (Test-Path -LiteralPath $passwordPath)) {
        throw "Hay un PFX local sin archivo de contraseña. No se usa para confiar el equipo y no se pudo comprobar que sea el mismo certificado."
    }

    $password = Get-Content -LiteralPath $passwordPath -Raw
    $candidates = @($password)
    if ($password.Trim() -ne $password) {
        $candidates += $password.Trim()
    }

    $pfx = $null
    $opened = $false
    foreach ($candidate in $candidates) {
        $secure = ConvertTo-SecureString $candidate -Force -AsPlainText
        try {
            $pfx = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                (Resolve-Path -LiteralPath $pfxPath),
                $secure,
                [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
            $opened = $true
            break
        }
        catch {
            if ($pfx) {
                $pfx.Dispose()
                $pfx = $null
            }
        }
    }

    if (-not $opened) {
        throw "No se pudo abrir el PFX para compararlo con la firma. No se importó ninguna confianza."
    }

    try {
        if ($pfx.Thumbprint -ne $Thumbprint) {
            throw "El PFX local no es el certificado que firmó el MSIX. No se importó ninguna confianza."
        }
    }
    finally {
        $pfx.Dispose()
    }
}

function Export-PocPublicCertificate {
    param([Parameter(Mandatory = $true)]$Signer)

    $certDir = Join-Path $script:PocRoot "local-certs"
    New-Item -ItemType Directory -Force -Path $certDir | Out-Null
    $cerPath = Join-Path $certDir "npb-poc.cer"
    $thumbPath = Join-Path $certDir "npb-poc.thumbprint.txt"
    $publicBytes = $Signer.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)

    if (Test-Path -LiteralPath $cerPath) {
        $existing = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)
        try {
            if ($existing.HasPrivateKey) {
                throw "local-certs\npb-poc.cer contiene clave privada. La confianza solo puede usar el certificado público."
            }
            if ($existing.Thumbprint -ne $Signer.Thumbprint) {
                throw "local-certs\npb-poc.cer no es el certificado que firmó el MSIX. No se reemplazó ni se importó."
            }
        }
        finally {
            $existing.Dispose()
        }
    }
    else {
        [System.IO.File]::WriteAllBytes($cerPath, $publicBytes)
    }

    $recorded = $null
    if (Test-Path -LiteralPath $thumbPath) {
        $recorded = (Get-Content -LiteralPath $thumbPath -Raw).Trim()
    }
    if ($recorded -and $recorded -ne $Signer.Thumbprint) {
        throw "El thumbprint registrado ($recorded) no coincide con el firmante $($Signer.Thumbprint). No se modificó el almacén."
    }
    if (-not $recorded) {
        Set-Content -LiteralPath $thumbPath -Value $Signer.Thumbprint -NoNewline -Encoding ascii
    }

    return [pscustomobject]@{
        CerPath    = $cerPath
        Thumbprint = $Signer.Thumbprint
    }
}

function Get-PocRecordedThumbprint {
    $cerPath = Join-Path $script:PocRoot "local-certs\npb-poc.cer"
    $thumbPath = Join-Path $script:PocRoot "local-certs\npb-poc.thumbprint.txt"
    if (-not (Test-Path -LiteralPath $cerPath) -or -not (Test-Path -LiteralPath $thumbPath)) {
        throw "Falta el certificado público o su thumbprint. No se eliminó ningún certificado."
    }

    $cer = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $cerPath))
    try {
        if ($cer.HasPrivateKey) {
            throw "El archivo .cer contiene clave privada. No se eliminó ningún certificado."
        }
        $recorded = (Get-Content -LiteralPath $thumbPath -Raw).Trim()
        if ($recorded -ne $cer.Thumbprint -or $cer.Subject -ne $script:PocPublisher) {
            throw "El .cer y el thumbprint registrado no describen el firmante de esta POC. No se eliminó ningún certificado."
        }
        return $cer.Thumbprint
    }
    finally {
        $cer.Dispose()
    }
}

function Read-PocPackageContent {
    param([Parameter(Mandatory = $true)][string]$MsixPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        foreach ($required in @("AppxManifest.xml", "AppxBlockMap.xml", "AppxSignature.p7x")) {
            if (-not $zip.GetEntry($required)) {
                throw "El paquete no contiene $required. No se instaló nada."
            }
        }

        $entry = $zip.GetEntry("AppxManifest.xml")
        $stream = $entry.Open()
        try {
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true)
            try {
                $document = New-Object System.Xml.XmlDocument
                $document.LoadXml($reader.ReadToEnd())
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        $namespaces = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
        $namespaces.AddNamespace("f", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
        $namespaces.AddNamespace("r", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")
        $namespaces.AddNamespace("ps2", "http://schemas.microsoft.com/appx/manifest/printsupport/windows10/2")

        $identity = $document.SelectSingleNode("/f:Package/f:Identity", $namespaces)
        if (-not $identity) {
            throw "El manifiesto empaquetado no tiene identidad."
        }
        if ($identity.GetAttribute("Name") -ne $script:PocPackageName) {
            throw "El paquete se llama '$($identity.GetAttribute("Name"))'. No se instaló nada."
        }
        if ($identity.GetAttribute("Publisher") -ne $script:PocPublisher) {
            throw "El Publisher del manifiesto no coincide con $script:PocPublisher."
        }
        if ($identity.GetAttribute("ProcessorArchitecture") -ne "x64") {
            throw "La arquitectura del paquete no es x64."
        }

        $printers = @($document.SelectNodes("//ps2:PrintSupportVirtualPrinter", $namespaces))
        if ($printers.Count -ne 1) {
            throw "El paquete declara $($printers.Count) impresoras virtuales. Esta POC exige exactamente una."
        }
        $printer = $printers[0]
        if ($printer.GetAttribute("DisplayName") -ne $script:PocPrinterName) {
            throw "La cola declarada es '$($printer.GetAttribute("DisplayName"))'."
        }
        if ($printer.GetAttribute("PrinterUri") -ne $script:PocPrinterUri) {
            throw "La URI de la cola no es $script:PocPrinterUri."
        }
        if ($printer.GetAttribute("PreferredInputFormat") -ne $script:PocInputFormat) {
            throw "El formato preferido no es $script:PocInputFormat."
        }
        if ($document.SelectNodes("//ps2:OutputFileTypes", $namespaces).Count -ne 0) {
            throw "El paquete declara OutputFileTypes. Esta POC no debe pedir una ruta de guardado."
        }

        $pdc = ($printer.GetAttribute("PdcFile") -replace "\\", "/")
        if (-not $pdc -or -not $zip.GetEntry($pdc)) {
            throw "No está el PDC declarado por la cola virtual."
        }

        $capabilities = @($document.SelectNodes("//f:Capability | //r:Capability", $namespaces))
        if ($capabilities.Count -ne 1 -or $capabilities[0].GetAttribute("Name") -ne "runFullTrust") {
            $names = ($capabilities | ForEach-Object { $_.GetAttribute("Name") }) -join ", "
            throw "Las capacidades del paquete no son únicamente runFullTrust ($names)."
        }

        return [pscustomobject]@{
            Name      = $identity.GetAttribute("Name")
            Publisher = $identity.GetAttribute("Publisher")
            Version   = $identity.GetAttribute("Version")
            Printer   = $printer.GetAttribute("DisplayName")
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Get-PocPrinterSnapshot {
    return @(Get-Printer | Select-Object Name, DriverName, PortName)
}

function Get-PocDefaultPrinterName {
    $defaults = @(Get-CimInstance -ClassName Win32_Printer | Where-Object { $_.Default })
    if ($defaults.Count -gt 1) {
        throw "Hay más de una impresora predeterminada. No se continuó."
    }
    if ($defaults.Count -eq 0) {
        return $null
    }
    return $defaults[0].Name
}

function Test-PocPrintersUnchanged {
    param($Before, $After)

    $beforeRows = @($Before)
    $afterRows = @($After)
    if ($beforeRows.Count -ne $afterRows.Count) {
        throw "Cambió la cantidad de impresoras durante una operación que no debía modificarlas."
    }
    foreach ($row in $beforeRows) {
        $match = @($afterRows | Where-Object { $_.Name -eq $row.Name -and $_.DriverName -eq $row.DriverName -and $_.PortName -eq $row.PortName })
        if ($match.Count -ne 1) {
            throw "Cambió la impresora '$($row.Name)' durante una operación que no debía modificarla."
        }
    }
}

function Test-PocPrinterIntegrity {
    param(
        $Before,
        $After,
        [ValidateSet("Install", "Uninstall")]
        [string]$Phase
    )

    $beforeRows = @($Before)
    $afterRows = @($After)
    $removed = @($beforeRows | Where-Object {
            $name = $_.Name
            -not @($afterRows | Where-Object { $_.Name -eq $name })
        })
    $added = @($afterRows | Where-Object {
            $name = $_.Name
            -not @($beforeRows | Where-Object { $_.Name -eq $name })
        })

    $removedOthers = @($removed | Where-Object { $_.Name -ne $script:PocPrinterName })
    if ($removedOthers.Count -gt 0) {
        throw "Desaparecieron impresoras ajenas a esta POC: $((($removedOthers.Name) -join ', '))."
    }
    if ($Phase -eq "Install" -and @($removed | Where-Object { $_.Name -eq $script:PocPrinterName }).Count -gt 0) {
        throw "La instalación retiró la cola $($script:PocPrinterName)."
    }

    $addedOthers = @($added | Where-Object { $_.Name -ne $script:PocPrinterName })
    if ($addedOthers.Count -gt 0) {
        throw "Aparecieron impresoras que esta POC no declara: $((($addedOthers.Name) -join ', '))."
    }

    if ($Phase -eq "Uninstall") {
        $stillThere = @($afterRows | Where-Object { $_.Name -eq $script:PocPrinterName })
        if ($stillThere.Count -gt 0) {
            throw "La cola $($script:PocPrinterName) sigue presente. No se llamó a Remove-Printer sobre ninguna cola."
        }
    }

    foreach ($row in $beforeRows) {
        $match = @($afterRows | Where-Object { $_.Name -eq $row.Name })
        if ($match.Count -ne 1) { continue }
        if ($match[0].DriverName -ne $row.DriverName -or $match[0].PortName -ne $row.PortName) {
            throw "Cambió el controlador o el puerto de '$($row.Name)'."
        }
    }
}

function Restore-PocDefaultPrinter {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    $escaped = $Name.Replace("'", "''")
    $printer = Get-CimInstance -ClassName Win32_Printer -Filter "Name = '$escaped'"
    if (-not $printer) {
        Write-Warning "No se pudo devolver la impresora predeterminada '$Name' porque ya no existe."
        return $false
    }
    $result = Invoke-CimMethod -InputObject $printer -MethodName SetDefaultPrinter
    if ($result.ReturnValue -ne 0) {
        Write-Warning "Windows no aceptó restaurar la impresora predeterminada '$Name'. Código $($result.ReturnValue)."
        return $false
    }
    return $true
}

function Get-PocCertificateThumbprints {
    param([Parameter(Mandatory = $true)][string]$Store)
    return @(Get-ChildItem -Path $Store -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Thumbprint | Sort-Object -Unique)
}

function Get-PocTrustSnapshot {
    $stores = @(
        "Cert:\LocalMachine\TrustedPeople",
        "Cert:\LocalMachine\Root",
        "Cert:\LocalMachine\CA",
        "Cert:\CurrentUser\TrustedPeople",
        "Cert:\CurrentUser\Root",
        "Cert:\CurrentUser\CA",
        "Cert:\CurrentUser\My"
    )
    $snapshot = @{}
    foreach ($store in $stores) {
        $snapshot[$store] = @(Get-PocCertificateThumbprints -Store $store)
    }
    return $snapshot
}

function Test-PocTrustSnapshot {
    param(
        $Before,
        $After,
        [ValidateSet("Add", "Remove", "Unchanged")]
        [string]$Change,
        [string]$Thumbprint
    )

    foreach ($store in @($Before.Keys)) {
        $beforeItems = @($Before[$store])
        $afterItems = @($After[$store])
        $added = @($afterItems | Where-Object { $_ -notin $beforeItems })
        $removed = @($beforeItems | Where-Object { $_ -notin $afterItems })
        $storeMayChange = ($store -eq $script:PocTrustedStore -and $Change -ne "Unchanged")

        if (-not $storeMayChange) {
            if ($added.Count -gt 0 -or $removed.Count -gt 0) {
                throw "Cambió el almacén $store. Esta POC solo puede tocar $script:PocTrustedStore y únicamente el thumbprint registrado."
            }
            continue
        }

        if ($Change -eq "Add") {
            if ($removed.Count -gt 0 -or @($added | Where-Object { $_ -ne $Thumbprint }).Count -gt 0 -or $added.Count -gt 1) {
                throw "La importación modificó más certificados de los previstos en $store."
            }
        }
        elseif ($removed.Count -gt 1 -or $added.Count -gt 0 -or @($removed | Where-Object { $_ -ne $Thumbprint }).Count -gt 0) {
            throw "La limpieza eliminó o agregó certificados distintos de $Thumbprint."
        }
    }
}

function Import-PocTrustedCertificate {
    param(
        [Parameter(Mandatory = $true)][string]$CerPath,
        [Parameter(Mandatory = $true)][string]$Thumbprint
    )

    $existing = @(Get-ChildItem -Path $script:PocTrustedStore | Where-Object { $_.Thumbprint -eq $Thumbprint })
    if ($existing.Count -gt 1) {
        throw "Hay más de un certificado con el thumbprint $Thumbprint. No se importó nada."
    }
    if ($existing.Count -eq 1) {
        if ($existing[0].Subject -ne $script:PocPublisher) {
            throw "El thumbprint ya está en TrustedPeople con otro firmante. No se sustituyó."
        }
        Write-Host "El certificado público ya estaba en $($script:PocTrustedStore)."
        return $false
    }

    $sameSubject = @(Get-ChildItem -Path $script:PocTrustedStore | Where-Object { $_.Subject -eq $script:PocPublisher })
    if ($sameSubject.Count -gt 0) {
        $others = ($sameSubject | ForEach-Object { $_.Thumbprint }) -join ", "
        throw "TrustedPeople ya contiene otro certificado de $($script:PocPublisher) ($others). No se eliminó ni se reemplazó."
    }

    $imported = @(Import-Certificate -FilePath $CerPath -CertStoreLocation $script:PocTrustedStore)
    if ($imported.Count -ne 1 -or $imported[0].Thumbprint -ne $Thumbprint -or $imported[0].HasPrivateKey) {
        throw "La importación no dejó exactamente el certificado público esperado."
    }
    return $true
}

function Remove-PocTrustedCertificate {
    param([Parameter(Mandatory = $true)][string]$Thumbprint)

    $matches = @(Get-ChildItem -Path $script:PocTrustedStore | Where-Object { $_.Thumbprint -eq $Thumbprint })
    if ($matches.Count -eq 0) {
        Write-Host "El thumbprint $Thumbprint no está en $($script:PocTrustedStore). No se modificó ningún almacén."
        return $false
    }
    if ($matches.Count -ne 1 -or $matches[0].Subject -ne $script:PocPublisher) {
        throw "El certificado encontrado no es exactamente el de esta POC. No se eliminó ninguno."
    }

    Remove-Item -LiteralPath (Join-Path $script:PocTrustedStore $matches[0].Thumbprint) -Force
    $still = @(Get-ChildItem -Path $script:PocTrustedStore | Where-Object { $_.Thumbprint -eq $Thumbprint })
    if ($still.Count -ne 0) {
        throw "El certificado $Thumbprint sigue presente."
    }
    return $true
}

function Get-PocPackageFullNames {
    return @(Get-AppxPackage | Select-Object -ExpandProperty PackageFullName)
}

function Test-PocPackageDelta {
    param(
        $Before,
        $After,
        [ValidateSet("Install", "Uninstall")]
        [string]$Phase,
        [string]$PackageFullName
    )

    $removed = @($Before | Where-Object { $_ -notin @($After) })
    $added = @($After | Where-Object { $_ -notin @($Before) })
    $foreignRemoved = @($removed | Where-Object { $_ -notlike "$($script:PocPackageName)_*" })
    if ($foreignRemoved.Count -gt 0) {
        throw "Se retiraron otros paquetes: $($foreignRemoved -join ', ')."
    }
    if ($Phase -eq "Install" -and $removed.Count -gt 0) {
        throw "La instalación retiró paquetes: $($removed -join ', ')."
    }

    $foreignAdded = @($added | Where-Object { $_ -notlike "$($script:PocPackageName)_*" })
    if ($foreignAdded.Count -gt 0) {
        throw "Se registraron otros paquetes: $($foreignAdded -join ', ')."
    }
    if ($Phase -eq "Uninstall" -and $PackageFullName -and $PackageFullName -notin @($removed)) {
        throw "No se retiró el paquete $PackageFullName."
    }
}

function Write-PocInstallFailureGuidance {
    param([Parameter(Mandatory = $true)]$Failure)

    $codes = New-Object System.Collections.Generic.List[string]
    $exception = $Failure.Exception
    while ($null -ne $exception) {
        $codes.Add(("0x{0:X8}" -f [uint32]$exception.HResult))
        $exception = $exception.InnerException
    }
    $codeText = ($codes -join ", ")
    $detail = $Failure.Exception.ToString()
    Write-Host "El registro del paquete no se completó. Códigos: $codeText"
    Write-Host $Failure.Exception.Message

    if ($detail -match "80073CFF" -or $codeText -match "80073CFF") {
        Write-Host "Windows bloqueó el sideloading por directiva. No se activó el modo de desarrollador ni se cambió AllowAllTrustedApps."
        Write-Host "Ese error sí justifica revisar, en una autorización aparte, la directiva de aplicaciones de confianza. El modo de desarrollador solo entra si el texto de Windows lo pide de forma explícita."
        return
    }
    if ($detail -match "800B0109" -or $codeText -match "800B0109") {
        Write-Host "Windows no construyó una cadena de confianza. El certificado no se copió a Trusted Root y el modo de desarrollador sigue sin activarse."
        Write-Host "Cualquier cambio adicional de confianza requiere una autorización distinta."
        return
    }
    if ($detail -match "modo de desarrollador|Developer Mode|developer mode") {
        Write-Host "El mensaje menciona el modo de desarrollador. Este procedimiento no lo activa. Habilitarlo sería otra decisión, posterior a este intento."
        return
    }
    Write-Host "No se aplicó ningún cambio adicional. Conserva este error antes de autorizar otra modificación."
}
