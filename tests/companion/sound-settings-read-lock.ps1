param([Parameter(Mandatory)][string]$Target,[Parameter(Mandatory)][string]$Signals)
$ErrorActionPreference='Stop'
$targetPath=[IO.Path]::GetFullPath($Target)
if([IO.Path]::GetFileName($targetPath) -ne 'soundsettings.json' -or [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($targetPath)) -ne 'FM27Access'){
    throw 'Only the actual mod sound settings file may be locked.'
}
# Read only: deny rename while preserving the original bytes. Never change ACLs.
# The upper bound also releases the handle if the browser driver terminates.
$stream=[IO.File]::Open($targetPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
    Set-Content -LiteralPath (Join-Path $Signals 'ready') -Value $PID
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while($timer.Elapsed.TotalSeconds -lt 120 -and -not(Test-Path -LiteralPath (Join-Path $Signals 'release'))){Start-Sleep -Milliseconds 100}
} finally {
    $stream.Dispose()
    Set-Content -LiteralPath (Join-Path $Signals 'released') -Value $PID
}
