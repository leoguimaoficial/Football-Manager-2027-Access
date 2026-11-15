param(
    [Parameter(Mandatory)][string]$GamePath,
    [string]$LoaderPath = "$PSScriptRoot/../.tools/bepinex-793",
    [string]$ReceiptPath = "$PSScriptRoot/../artifacts/install/loader.json"
)
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1" -Force
. "$PSScriptRoot/GameReport.ps1"
Assert-GameClosed $GamePath
$report=Get-VerifiedGameReport -GamePath $GamePath
Write-Output "Loader candidate: $($report.Store), Unity $($report.UnityVersion), metadata $($report.MetadataVersion). Verify the production host after installation."
$staging=Join-Path $ProjectRoot ('artifacts/loader-runtime-'+[Guid]::NewGuid().ToString('N'))
$proxyName=if($report.Store -eq 'Xbox'){'winhttp.dll'}else{'version.dll'}
$payload=Copy-LoaderRuntimePayload -Source $LoaderPath -Destination $staging -ProxyName $proxyName
Write-Output "Loader-only payload: $($payload.Copied) runtime files; $($payload.ExcludedMods) bundled plugin/patcher files excluded."
Install-Loader -GamePath $GamePath -Payload $payload.Path -ReceiptPath $ReceiptPath
