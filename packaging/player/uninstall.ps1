#Requires -Version 7.2
param([Parameter(Mandatory)][string]$ReceiptPath)
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1" -Force
Import-Module "$PSScriptRoot/LoggingProfile.psm1" -Force
$receipt=Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
Assert-GameClosed $receipt.root
Uninstall-Loader -ReceiptPath $ReceiptPath
$loggingReceipt=$ReceiptPath+'.logging.json'
if(Test-Path -LiteralPath $loggingReceipt){Restore-ProductionLogging -ReceiptPath $loggingReceipt}
