param([string]$ArtifactsDirectory = (Join-Path $PSScriptRoot '../../artifacts'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
# A fresh directory prevents stale framework-dependent files from entering the ZIP.
$stage = Join-Path $artifacts ('c1-publish-' + [guid]::NewGuid().ToString('N'))
& dotnet publish (Join-Path $root 'src/Windows/Desktop/Neobash.PrintBridge.Desktop.csproj') `
    -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:PublishSelfContained=true -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o $stage
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed: $LASTEXITCODE" }

# App-local VC++ CRT from the redistributable directory, never from System32.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installations = & $vswhere -products '*' -property installationPath
if ($LASTEXITCODE -ne 0) { throw 'vswhere failed.' }
$crt = @($installations | ForEach-Object {
    Get-ChildItem (Join-Path $_ 'VC/Redist/MSVC/*/x64/Microsoft.VC*.CRT') -Directory -ErrorAction SilentlyContinue
} | Sort-Object FullName -Descending | Select-Object -First 1)
if ($crt.Count -ne 1) { throw 'Install the Visual C++ x64 redistributable build component on the build machine.' }
Copy-Item -Path (Join-Path $crt[0].FullName '*.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'docs/C1-portable.md') -Destination (Join-Path $stage 'LEEME.md')

$required = @('Neobash.PrintBridge.Desktop.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
    'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.WindowsAppRuntime.pri',
    'Neobash.PrintBridge.Desktop.pri', 'pdfium.dll', 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $file))) { throw "Missing portable dependency: $file" }
}
$runtime = Get-Content (Join-Path $stage 'Neobash.PrintBridge.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $runtime.runtimeOptions.PSObject.Properties.Name -contains 'frameworks' -or
    -not ($runtime.runtimeOptions.PSObject.Properties.Name -contains 'includedFrameworks')) {
    throw 'The publication requires an external .NET runtime.'
}
if (Get-ChildItem $stage -Filter 'Neobash.PrintBridge.Agent*') { throw 'Agent must not enter C1.' }
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
@{ sourceCommit = $commit; dotnetSdk = (& dotnet --version); crtSource = $crt[0].Name;
   crtVersion = (Get-Item (Join-Path $stage 'vcruntime140.dll')).VersionInfo.FileVersion;
   runtime = $runtime.runtimeOptions.includedFrameworks } | ConvertTo-Json -Depth 5 |
    Set-Content (Join-Path $stage 'build-info.json') -Encoding utf8
Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, [IO.Path]::GetRelativePath($stage, $_.FullName)
} | Set-Content (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8
$zip = Join-Path $artifacts 'NeobashPrintBridge-C1-win-x64-portable.zip'
# ZipFile includes the complete tree, including all native DLLs, resources and notices.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = Join-Path $artifacts ('c1-' + [guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
Get-FileHash $zip -Algorithm SHA256 | Format-List
Write-Host "Portable ZIP: $zip"
Write-Host "Publication: $stage"
