#Requires -Version 7.2
[CmdletBinding()]
param([string]$OutputPath="$PSScriptRoot/../artifacts/installer")
. "$PSScriptRoot/../scripts/Common.ps1"
Invoke-DotNet -Arguments @('publish',"$PSScriptRoot/FM27Access.Installer",'-c','Release','-p:FM27DeveloperTools=false','-o',[IO.Path]::GetFullPath($OutputPath),'--nologo')
$exe=Join-Path $OutputPath 'FM27Access-Installer.exe'
if(!(Test-Path -LiteralPath $exe) -or (Get-ChildItem -LiteralPath $OutputPath -Filter '*.pdb')){throw 'Invalid installer publish output.'}
$bytes=[IO.File]::ReadAllBytes($exe)
if([BitConverter]::ToUInt16($bytes,[BitConverter]::ToInt32($bytes,60)+4) -ne 0x8664){throw 'Installer is not x64.'}
Write-Output "Universal installer: $([IO.Path]::GetFullPath($exe))"
