param([string]$ReceiptPath = "$PSScriptRoot/../artifacts/install/loader.json")
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1" -Force
Import-Module "$PSScriptRoot/../packaging/player/LoggingProfile.psm1" -Force
Uninstall-Loader -ReceiptPath $ReceiptPath
if(Test-Path -LiteralPath "$ReceiptPath.logging.json"){Restore-ProductionLogging -ReceiptPath "$ReceiptPath.logging.json"}
