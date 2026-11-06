$ErrorActionPreference = "Stop"
. "$PSScriptRoot\TrustChecks.ps1"
Set-Location $script:PocRoot

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

& $dotnet test "tests\UnitTests\Neobash.PrintBridge.Tests.csproj" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet msbuild "src\Windows\VirtualPrinter\Neobash.PrintBridge.VirtualPrinter.csproj" /restore /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /m /v:m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$msix = Get-ChildItem "src\Windows\VirtualPrinter\AppPackages" -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $msix) {
    throw "No se generó el paquete MSIX."
}

$certDir = Join-Path $script:PocRoot "local-certs"
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
$pfx = Join-Path $certDir "npb-poc.pfx"
$passwordPath = Join-Path $certDir "pfx-password.txt"

if (-not (Test-Path $pfx)) {
    $password = [guid]::NewGuid().ToString("N")
    Set-Content -Path $passwordPath -Value $password -NoNewline
    $secure = ConvertTo-SecureString $password -Force -AsPlainText
    $cert = New-SelfSignedCertificate -Type Custom -Subject "CN=Neobash Print Bridge POC" -KeyUsage DigitalSignature -FriendlyName "Neobash Print Bridge POC" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") -HashAlgorithm SHA256 -KeyExportPolicy Exportable
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
    Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
}

$password = Get-Content $passwordPath -Raw
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
& $signtool sign /fd SHA256 /f $pfx /p $password $msix.FullName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$signature = Get-PocSignerSignature -MsixPath $msix.FullName
Assert-PocPfxMatchesSigner -Thumbprint $signature.SignerCertificate.Thumbprint
$publicCert = Export-PocPublicCertificate -Signer $signature.SignerCertificate

Write-Host "Paquete firmado. No se importó el certificado en ningún almacén:"
Write-Host $msix.FullName
Write-Host "Certificado público: $($publicCert.CerPath)"
Write-Host "Thumbprint: $($publicCert.Thumbprint)"
Write-Host "La instalación queda detenida. Revisar docs\installation.md antes de autorizar la confianza."
