param([Parameter(Mandatory)][string]$GamePath)
. "$PSScriptRoot/Common.ps1"
$out=Join-Path $ProjectRoot ('artifacts/diagnostics/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $out | Out-Null
Invoke-DotNet -Arguments @('run','--project',(Join-Path $ProjectRoot tools/game-inspect),'--','inspect',$GamePath) | Set-Content -LiteralPath "$out/installation.json" -Encoding utf8
# Copy only mod/framework logs. Player.log may contain account identifiers or tokens.
foreach ($relative in 'BepInEx/LogOutput.log','BepInEx/ErrorLog.log') {
    $source=Join-Path $GamePath $relative
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $out }
}
Write-Output "Local diagnostics: $out. Review before sharing; nothing was uploaded."
