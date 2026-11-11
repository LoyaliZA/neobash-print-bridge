param([string]$AgentPath = "$PSScriptRoot\..\..\artifacts\b1-audit\agent\Neobash.PrintBridge.Agent.exe")
$ErrorActionPreference = 'Stop'
$AgentPath = (Resolve-Path -LiteralPath $AgentPath).Path
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('npb-agent-smoke-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($probeRoot) | Out-Null
function Start-Probe([string[]]$Arguments = @()) {
    $info = [Diagnostics.ProcessStartInfo]::new($AgentPath)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = Split-Path -Parent $AgentPath
    $info.Environment['Agent__DataDirectory'] = $probeRoot
    $info.Environment['Agent__HostName'] = 'localhost'
    $info.Environment['Agent__ListenAddress'] = '127.0.0.1'
    $info.Environment['Agent__Port'] = '0'
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($info)
}
function Wait-Listening($Process) {
    $pin = $null
    while ($true) {
        $read = $Process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(30000)) { throw 'Agent start timed out.' }
        $line = $read.Result
        if ($null -eq $line) { throw ('Agent exited: ' + $Process.StandardError.ReadToEnd()) }
        if ($line.StartsWith('Huella: ')) { $pin = $line.Substring(8) }
        if ($line.StartsWith('Escucha: ')) { return $pin }
    }
}
function Invoke-Admin([string[]]$Arguments) {
    $command = Start-Probe $Arguments
    try {
        if (-not $command.WaitForExit(30000)) { throw 'Administrative command timed out.' }
        $text = $command.StandardOutput.ReadToEnd()
        if ($command.ExitCode -ne 0) { throw $command.StandardError.ReadToEnd() }
        return $text
    } finally {
        if (-not $command.HasExited) { $command.Kill($true); $command.WaitForExit() }
        $command.Dispose()
    }
}
$first = $null; $second = $null; $restart = $null
try {
    $first = Start-Probe
    $pin = Wait-Listening $first
    $second = Start-Probe
    if (-not $second.WaitForExit(30000)) { throw 'Second agent failed to exit.' }
    $secondLog = $second.StandardOutput.ReadToEnd() + $second.StandardError.ReadToEnd()
    if ($second.ExitCode -eq 0 -or -not $secondLog.Contains('Ya hay un agente')) { throw 'Second start was not rejected by the database lock.' }
    if (-not (Invoke-Admin @('pair')).Contains($pin)) { throw 'Administrative pair changed the active identity.' }
    if (-not (Invoke-Admin @('stage')).Contains('Huella nueva:')) { throw 'stage failed.' }
    Invoke-Admin @('revoke', 'nonexistent-probe-device') | Out-Null
    $first.Kill($true); $first.WaitForExit()
    $restart = Start-Probe
    if ((Wait-Listening $restart) -ne $pin) { throw 'Restart changed certificate identity.' }
    $restart.Kill($true); $restart.WaitForExit()
    Invoke-Admin @('renew') | Out-Null
    $restart.Dispose()
    $restart = Start-Probe
    if ((Wait-Listening $restart) -eq $pin) { throw 'Explicit certificate replacement failed.' }
    Write-Output 'PASS: published Windows Agent start, second-start rejection, pair/stage/revoke/renew, crash and restart. No print job submitted.'
    Write-Output ('Evidence directory: ' + $probeRoot)
} finally {
    foreach ($process in @($first, $second, $restart)) {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }
    }
}
