#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GamePath,[Parameter(Mandatory)][string]$ReceiptPath)
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1" -Force
Import-Module "$PSScriptRoot/PlayerPackage.psm1" -Force
Import-Module "$PSScriptRoot/LoggingProfile.psm1" -Force
Assert-GameClosed $GamePath
$manifest=Test-PlayerPackage $PSScriptRoot
Assert-PlayerNativeBuild $GamePath $manifest
$loggingReceipt=$ReceiptPath+'.logging.json'
if((Test-Path -LiteralPath $ReceiptPath) -or (Test-Path -LiteralPath $loggingReceipt)){throw 'Receipt already exists. Keep the old receipts; remove the old installation before installing this candidate.'}
# Install-Loader checks all file collisions before writing anything.
Install-Loader -GamePath $GamePath -Payload (Join-Path $PSScriptRoot 'payload') -ReceiptPath $ReceiptPath
try {Set-ProductionLogging -GamePath $GamePath -ReceiptPath $loggingReceipt}
catch {
    # The mod receipt remains independently usable even if configuration failed.
    Write-Warning "Logging setup failed. Keep both receipts and inspect the failure before launching: $($_.Exception.Message)"
    throw
}
Write-Output "Installed candidate $($manifest.version). Start the game normally. Keep both receipts for removal. No saves or game settings were changed."
