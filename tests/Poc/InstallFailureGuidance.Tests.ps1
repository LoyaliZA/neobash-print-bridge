# Pruebas del diagnóstico de instalación de la POC 01.
# No importan certificados, no registran paquetes y no leen el almacén del equipo.
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\..\scripts\poc\TrustChecks.ps1")

$script:Failures = New-Object System.Collections.Generic.List[string]

function Add-TestFailure {
    param([string]$Name, [string]$Detail)
    $script:Failures.Add("${Name}: $Detail")
}

function Assert-Equal {
    param([string]$Name, $Actual, $Expected)
    if ([string]$Actual -ne [string]$Expected) {
        Add-TestFailure -Name $Name -Detail "se obtuvo '$Actual' y se esperaba '$Expected'."
    }
}

function Assert-Contains {
    param([string]$Name, [string]$Text, [string]$Expected)
    if ($Text -notlike "*$Expected*") {
        Add-TestFailure -Name $Name -Detail "el texto no contiene '$Expected'."
    }
}

function Assert-NotContains {
    param([string]$Name, [string]$Text, [string]$Unexpected)
    if ($Text -like "*$Unexpected*") {
        Add-TestFailure -Name $Name -Detail "el texto contiene '$Unexpected'."
    }
}

function Set-PocTestHResult {
    param(
        [Parameter(Mandatory = $true)]$Exception,
        [Parameter(Mandatory = $true)][int]$Value
    )

    $property = [System.Exception].GetProperty(
        "HResult",
        [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic)
    $property.SetValue($Exception, $Value)
}

function New-PocTestFailure {
    param([Parameter(Mandatory = $true)]$Exception)

    return [System.Management.Automation.ErrorRecord]::new(
        $Exception,
        "DeploymentError",
        [System.Management.Automation.ErrorCategory]::NotSpecified,
        $null)
}

function Get-PocTestFailureText {
    param(
        [Parameter(Mandatory = $true)]$Failure,
        $AppxLogQuery
    )

    $parameters = @{ Failure = $Failure }
    if ($PSBoundParameters.ContainsKey("AppxLogQuery")) {
        $parameters.AppxLogQuery = $AppxLogQuery
    }
    return (@(Get-PocInstallFailureLines @parameters) -join [Environment]::NewLine)
}

$negativeHResult = -2146233087
$positiveHResult = 42

Assert-Equal -Name "HRESULT negativo" -Actual (ConvertTo-PocHResultHex -Value $negativeHResult) -Expected "0x80131501"
Assert-Equal -Name "HRESULT positivo" -Actual (ConvertTo-PocHResultHex -Value $positiveHResult) -Expected "0x0000002A"
Assert-Equal -Name "HRESULT positivo amplio" -Actual (ConvertTo-PocHResultHex -Value 2148734209) -Expected "0x80131501"

$inner = New-Object System.IO.IOException "No se encontró el manifiesto del paquete."
Set-PocTestHResult -Exception $inner -Value -2147024894
$outer = New-Object System.Exception "Deployment failed with HRESULT: 0x80131501.", $inner
Set-PocTestHResult -Exception $outer -Value $negativeHResult
$negativeText = Get-PocTestFailureText -Failure (New-PocTestFailure -Exception $outer)

Assert-Contains -Name "mensaje original" -Text $negativeText -Expected "Deployment failed with HRESULT: 0x80131501."
Assert-Contains -Name "HRESULT negativo visible" -Text $negativeText -Expected "0x80131501"
Assert-Contains -Name "HRESULT negativo decimal" -Text $negativeText -Expected "-2146233087"
Assert-Contains -Name "error interno" -Text $negativeText -Expected "No se encontró el manifiesto del paquete."
Assert-Contains -Name "HRESULT interno" -Text $negativeText -Expected "0x80070002"
Assert-Contains -Name "HRESULT interno decimal" -Text $negativeText -Expected "-2147024894"

$positiveException = New-Object System.Exception "El registro informó un código positivo."
Set-PocTestHResult -Exception $positiveException -Value $positiveHResult
$positiveText = Get-PocTestFailureText -Failure (New-PocTestFailure -Exception $positiveException)
Assert-Contains -Name "mensaje positivo" -Text $positiveText -Expected "El registro informó un código positivo."
Assert-Contains -Name "HRESULT positivo visible" -Text $positiveText -Expected "0x0000002A"
Assert-NotContains -Name "positivo sin decimal negativo" -Text $positiveText -Unexpected "-2146233087"

$activityId = "a80e1223-2787-0000-dc3e-12a98727d301"
$deployment = New-Object System.Exception "Deployment failed. Look for [ActivityId] $activityId in the Event Log."
Set-PocTestHResult -Exception $deployment -Value -2147009281
$script:AppxLogCalls = @()
function Get-AppxLog {
    param(
        [string]$ActivityId,
        [switch]$All
    )

    $script:AppxLogCalls += $ActivityId
    return @([pscustomobject]@{
            TimeCreated = [datetime]"2026-11-06T17:40:00"
            Id          = 404
            Message     = "error de registro del paquete"
        })
}

$activityText = Get-PocTestFailureText -Failure (New-PocTestFailure -Exception $deployment)
Assert-Contains -Name "ActivityId" -Text $activityText -Expected $activityId
Assert-Contains -Name "comando Get-AppxLog" -Text $activityText -Expected "Get-AppxLog -ActivityId $activityId"
Assert-Contains -Name "registro consultado" -Text $activityText -Expected "error de registro del paquete"
Assert-Contains -Name "marca del registro" -Text $activityText -Expected "2026-11-06 17:40:00"
Assert-Equal -Name "Get-AppxLog recibió el ActivityId" -Actual ($script:AppxLogCalls -join ",") -Expected $activityId
Assert-Contains -Name "directiva de sideloading" -Text $activityText -Expected "Windows bloqueó el sideloading por directiva."
Assert-NotContains -Name "sin activar modo de desarrollador" -Text $activityText -Unexpected "Este procedimiento activó el modo de desarrollador"

$withProperty = New-Object System.Exception "Fallo con identificador de actividad."
Set-PocTestHResult -Exception $withProperty -Value $positiveHResult
$withProperty | Add-Member -NotePropertyName ActivityId -NotePropertyValue "bbbbbbbb-cccc-dddd-eeee-ffffffffffff"
$propertyText = Get-PocTestFailureText -Failure (New-PocTestFailure -Exception $withProperty) -AppxLogQuery {
    param($Id)
    return @([pscustomobject]@{ Message = "registro por propiedad $Id" })
}
Assert-Contains -Name "ActivityId de la propiedad" -Text $propertyText -Expected "bbbbbbbb-cccc-dddd-eeee-ffffffffffff"
Assert-Contains -Name "registro de la consulta inyectada" -Text $propertyText -Expected "registro por propiedad bbbbbbbb-cccc-dddd-eeee-ffffffffffff"

$trustException = New-Object System.Exception "La cadena de confianza no se construyó."
Set-PocTestHResult -Exception $trustException -Value -2146762487
$trustText = Get-PocTestFailureText -Failure (New-PocTestFailure -Exception $trustException)
Assert-Contains -Name "HRESULT de cadena" -Text $trustText -Expected "0x800B0109"
Assert-Contains -Name "guía de cadena" -Text $trustText -Expected "Windows no construyó una cadena de confianza."

$hidden = New-Object System.Exception "mensaje que no debe perderse. ActivityId: 11111111-2222-3333-4444-555555555555"
Set-PocTestHResult -Exception $hidden -Value $negativeHResult
$preserved = Get-PocTestFailureText -Failure (New-PocTestFailure -Exception $hidden) -AppxLogQuery {
    param($Id)
    throw "log no disponible"
}
Assert-Contains -Name "mensaje conservado si el registro falla" -Text $preserved -Expected "mensaje que no debe perderse"
Assert-Contains -Name "HRESULT conservado si el registro falla" -Text $preserved -Expected "0x80131501"
Assert-Contains -Name "fallo de consulta" -Text $preserved -Expected "log no disponible"

$guidanceFailure = New-PocTestFailure -Exception $outer
try {
    Write-PocInstallFailureGuidance -Failure $guidanceFailure -AppxLogQuery { param($Id) @() } | Out-Null
}
catch {
    Add-TestFailure -Name "guía sin excepción secundaria" -Detail $_.Exception.Message
}

if ($script:Failures.Count -gt 0) {
    $script:Failures | ForEach-Object { Write-Output $_ }
    exit 1
}

Write-Output "Pruebas del diagnóstico de instalación superadas."
