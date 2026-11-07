# Synthetic snapshots only. Never run install.ps1 or mutate Windows.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\scripts\poc\TrustChecks.ps1')
$script:Checks = 0
function Assert-Test {
    param([bool]$Condition, [string]$Message)
    $script:Checks++
    if (-not $Condition) { throw "FAILED: $Message" }
}
function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-Test $rejected $Message
}

$old = 'Neobash.PrintBridge.VirtualPrinter_1.0.0.0_x64__81d0rh61c409w'
$new = 'Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64__81d0rh61c409w'
$foreign = 'Other.App_2.0.0.0_x64__abcdefghijklm'
Test-PocPackageDelta -Before @($old, $foreign) -After @($new, $foreign) -Phase Install -PackageFullName $new
Test-PocPackageDelta -Before @($foreign) -After @($new, $foreign) -Phase Install -PackageFullName $new
Test-PocPackageDelta -Before @($new, $foreign) -After @($new, $foreign) -Phase Install -PackageFullName $new
Test-PocPackageDelta -Before @($old, $new, $foreign) -After @($new, $foreign) -Phase Install -PackageFullName $new
Assert-Test $true 'Upgrade, fresh install, idempotent validation and old-version cleanup accepted'

Assert-Rejected { Test-PocPackageDelta -Before @($old, $foreign) -After @($new) -Phase Install -PackageFullName $new } 'Foreign removal'
Assert-Rejected { Test-PocPackageDelta -Before @($old, $foreign) -After @($new, 'Other.App_2.0.1.0_x64__abcdefghijklm') -Phase Install -PackageFullName $new } 'Foreign update'
Assert-Rejected { Test-PocPackageDelta -Before @($old) -After @($new, $foreign) -Phase Install -PackageFullName $new } 'Foreign addition'
foreach ($unexpected in @(
    'Neobash.PrintBridge.VirtualPrinter_1.0.0.0_x64__differentpub',
    'Neobash.PrintBridge.VirtualPrinter_1.0.0.0_arm64__81d0rh61c409w',
    'Neobash.PrintBridge.VirtualPrinter_1.0.0.0_x64_resource_81d0rh61c409w',
    'Neobash.PrintBridge.VirtualPrinter.Extra_1.0.0.0_x64__81d0rh61c409w',
    'Neobash.PrintBridge.VirtualPrinter_bad_x64__81d0rh61c409w'
)) {
    Assert-Rejected { Test-PocPackageDelta -Before @($unexpected) -After @($new) -Phase Install -PackageFullName $new } "Reject identity substitution: $unexpected"
    Assert-Rejected { Test-PocPackageDelta -Before @() -After @($unexpected) -Phase Install -PackageFullName $unexpected } "Reject unexpected target: $unexpected"
}
Assert-Rejected { Test-PocPackageDelta -Before @($new) -After @($old) -Phase Install -PackageFullName $old } 'Downgrade'
Assert-Rejected { Test-PocPackageDelta -Before @($old) -After @() -Phase Install -PackageFullName $new } 'Missing replacement'
Assert-Rejected { Test-PocPackageDelta -Before @($old) -After @($new, $old) -Phase Install -PackageFullName $old } 'Unexpected additional NPB version'
Test-PocPackageDelta -Before @($new, $foreign) -After @($foreign) -Phase Uninstall -PackageFullName $new
Test-PocPackageDelta -Before @($foreign) -After @($foreign) -Phase Install -PackageFullName $null
Assert-Rejected { Test-PocPackageDelta -Before @($new, $foreign) -After @() -Phase Uninstall -PackageFullName $new } 'Uninstall still protects foreign packages'
Assert-Rejected { Test-PocPackageDelta -Before @($old) -After @() -Phase Install -PackageFullName $null } 'Certificate-only operation cannot remove NPB'

# Mocks replace all system queries made by Get-PocInstallationValidation.
function Get-AppxPackage {
    [CmdletBinding()]
    param([string]$Name)
    if ($script:PackageQueryFails) { throw 'Package query denied' }
    return $script:InstalledPackages
}
function Get-PocPackageFullNames { return $script:AfterPackages }
function Get-Printer {
    [CmdletBinding()]
    param()
    $script:PrinterQueries++
    if ($script:PrinterQueryFails) { throw 'Printer query denied' }
    return $script:AfterPrinters
}
function Reset-Scenario {
    $script:InstalledPackages = @([pscustomobject]@{
        Name = $script:PocPackageName; Publisher = $script:PocPublisher
        Version = '1.0.1.0'; PackageFullName = $new; Status = 'Ok'
    })
    $script:BeforePrinters = @([pscustomobject]@{ Name = 'Existing printer'; DriverName = 'Driver'; PortName = 'USB001' })
    $script:AfterPrinters = @($script:BeforePrinters) + @([pscustomobject]@{
        Name = $script:PocPrinterName; DriverName = 'Microsoft Virtual Print Class Driver'; PortName = 'NPB port'
    })
    $script:AfterPackages = @($new, $foreign)
    $script:PrinterQueries = 0
    $script:PackageQueryFails = $false
    $script:PrinterQueryFails = $false
}
function Validate-Scenario {
    Get-PocInstallationValidation -PackagesBefore @($old, $foreign) -PrintersBefore $script:BeforePrinters -ExpectedVersion ([version]'1.0.1.0')
}

Reset-Scenario
$result = Validate-Scenario
Assert-Test ($result.PackageErrors.Count -eq 0 -and $result.PrinterErrors.Count -eq 0) '1.0.0.0 -> 1.0.1.0 with Windows queue succeeds'
Reset-Scenario
$script:AfterPrinters = $script:BeforePrinters
$result = Validate-Scenario
Assert-Test ($result.PackageErrors.Count -eq 0 -and $result.PrinterErrors.Count -gt 0) 'Healthy MSIX without queue is a printer failure'
foreach ($case in @('OldVersion', 'BadStatus', 'MissingStatus', 'WrongPublisher', 'Missing', 'Duplicate', 'QueryFailure')) {
    Reset-Scenario
    switch ($case) {
        'OldVersion' { $script:InstalledPackages[0].Version = '1.0.0.0'; $script:InstalledPackages[0].PackageFullName = $old; $script:AfterPackages = @($old, $foreign) }
        'BadStatus' { $script:InstalledPackages[0].Status = 'Modified' }
        'MissingStatus' { $script:InstalledPackages[0].Status = $null }
        'WrongPublisher' { $script:InstalledPackages[0].Publisher = 'CN=Other' }
        'Missing' { $script:InstalledPackages = @(); $script:AfterPackages = @($foreign) }
        'Duplicate' { $script:InstalledPackages += $script:InstalledPackages[0] }
        'QueryFailure' { $script:PackageQueryFails = $true }
    }
    $result = Validate-Scenario
    Assert-Test ($result.PackageErrors.Count -gt 0) "MSIX failure: $case"
    Assert-Test ($script:PrinterQueries -eq 1 -and $result.PrinterErrors.Count -eq 0) "Independent queue verification: $case"
}
Reset-Scenario
$script:PrinterQueryFails = $true
$result = Validate-Scenario
Assert-Test ($result.PackageErrors.Count -eq 0 -and $result.PrinterErrors[0] -like '*denied*') 'Printer query denial is not an absent queue or MSIX failure'
Reset-Scenario
$script:InstalledPackages[0].Status = 'Modified'
$script:AfterPrinters = $script:BeforePrinters
$result = Validate-Scenario
Assert-Test ($result.PackageErrors.Count -gt 0 -and $result.PrinterErrors.Count -gt 0) 'Both failure categories are preserved'
Reset-Scenario
$script:AfterPackages = @($new)
$result = Validate-Scenario
Assert-Test ($result.PackageErrors.Count -gt 0 -and $script:PrinterQueries -eq 1) 'Foreign package deletion does not skip queue verification'
Reset-Scenario
$script:AfterPrinters[0].PortName = 'CHANGED'
# Replace before snapshot to avoid shared reference in this fixture.
$script:BeforePrinters = @([pscustomobject]@{ Name = 'Existing printer'; DriverName = 'Driver'; PortName = 'USB001' })
$result = Validate-Scenario
Assert-Test ($result.PrinterErrors.Count -gt 0) 'Foreign printer modification'
Write-Output "Passed $script:Checks upgrade validation checks; no system changes."
