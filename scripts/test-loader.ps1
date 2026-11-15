$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1" -Force
$root=Join-Path ([IO.Path]::GetTempPath()) ('fm27-loader-test-'+[Guid]::NewGuid())
$game=Join-Path $root 'game'
$payload=Join-Path $root 'payload'
$receipt=Join-Path $root 'receipt.json'
New-Item -ItemType Directory -Force $game,$payload | Out-Null
Set-Content -LiteralPath (Join-Path $game 'fm.exe') -Value 'synthetic fixture'
Set-Content -LiteralPath (Join-Path $payload 'loader.dll') -Value 'loader fixture'
Install-Loader $game $payload $receipt | Out-Null
if (-not (Test-Path (Join-Path $game 'loader.dll'))) { throw 'Install failed' }
Uninstall-Loader $receipt | Out-Null
if (Test-Path (Join-Path $game 'loader.dll')) { throw 'Removal failed' }
if (-not (Test-Path (Join-Path $game 'fm.exe'))) { throw 'Original removed' }
Set-Content -LiteralPath (Join-Path $game 'loader.dll') -Value 'other mod'
$rejected=$false
try { Install-Loader $game $payload (Join-Path $root 'collision.json') | Out-Null } catch { $rejected=$true }
if (-not $rejected) { throw 'Collision accepted' }
if ((Get-Content (Join-Path $game 'loader.dll')) -ne 'other mod') { throw 'Other mod overwritten' }
$modifiedGame=Join-Path $root 'modified-game'
$modifiedReceipt=Join-Path $root 'modified-receipt.json'
New-Item -ItemType Directory -Force $modifiedGame | Out-Null
Set-Content -LiteralPath (Join-Path $modifiedGame 'fm.exe') -Value 'synthetic fixture'
Install-Loader $modifiedGame $payload $modifiedReceipt | Out-Null
Set-Content -LiteralPath (Join-Path $modifiedGame 'loader.dll') -Value 'modified after install'
$rejected=$false
try { Uninstall-Loader $modifiedReceipt | Out-Null } catch { $rejected=$true }
if (-not $rejected) { throw 'Modified file removed' }
if (-not (Test-Path -LiteralPath (Join-Path $modifiedGame 'loader.dll'))) { throw 'Modified file missing' }
$forged=Join-Path $root 'forged.json'
@{schema=1;root=$game;state='installed';files=@(@{relative='../outside.dll';sha256='0'})} | ConvertTo-Json -Depth 6 | Set-Content $forged
$rejected=$false
try { Uninstall-Loader $forged | Out-Null } catch { $rejected=$true }
if (-not $rejected) { throw 'Path traversal accepted' }
$bundle=Join-Path $root 'development-loader-bundle'
$staged=Join-Path $root 'runtime-only'
New-Item -ItemType Directory -Force "$bundle/BepInEx/core","$bundle/BepInEx/plugins/Example","$bundle/BepInEx/patchers/Example" | Out-Null
Set-Content -LiteralPath "$bundle/winhttp.dll" -Value 'loader proxy fixture'
Set-Content -LiteralPath "$bundle/BepInEx/core/runtime.dll" -Value 'loader runtime fixture'
Set-Content -LiteralPath "$bundle/BepInEx/plugins/Example/smoke.dll" -Value 'diagnostic plugin fixture'
Set-Content -LiteralPath "$bundle/BepInEx/patchers/Example/patcher.dll" -Value 'diagnostic patcher fixture'
$stage=Copy-LoaderRuntimePayload $bundle $staged
if($stage.Copied -ne 2 -or $stage.ExcludedMods -ne 2){throw 'Loader payload boundaries failed.'}
if((Test-Path -LiteralPath "$staged/winhttp.dll") -or (Get-LoaderProxy $staged) -ne 'version.dll' -or
    (Get-FileHash -LiteralPath "$staged/version.dll").Hash -ne (Get-FileHash -LiteralPath "$bundle/winhttp.dll").Hash){throw 'Supported proxy rename did not preserve exact pinned bytes.'}
if((Get-LoaderProxy $bundle) -ne 'winhttp.dll'){throw 'Legacy source proxy discovery failed.'}
if(Test-Path -LiteralPath "$staged/BepInEx/plugins"){throw 'Loader staged a diagnostic plugin.'}
if(Test-Path -LiteralPath "$staged/BepInEx/patchers"){throw 'Loader staged a diagnostic patcher.'}
if((Get-FileHash -LiteralPath "$staged/BepInEx/core/runtime.dll").Hash -ne (Get-FileHash -LiteralPath "$bundle/BepInEx/core/runtime.dll").Hash){throw 'Loader runtime changed while staging.'}
if(-not(Test-Path -LiteralPath "$bundle/BepInEx/plugins/Example/smoke.dll")){throw 'Loader staging modified its source mods.'}
$isolatedGame=Join-Path $root 'loader-isolation-game'
$isolatedReceipt=Join-Path $root 'loader-isolation-receipt.json'
New-Item -ItemType Directory -Force "$isolatedGame/BepInEx/plugins/UserMod" | Out-Null
Set-Content -LiteralPath "$isolatedGame/fm.exe" -Value 'synthetic fixture'
Set-Content -LiteralPath "$isolatedGame/BepInEx/plugins/UserMod/existing.dll" -Value 'independently owned mod fixture'
$otherModHash=(Get-FileHash -LiteralPath "$isolatedGame/BepInEx/plugins/UserMod/existing.dll").Hash
Install-Loader $isolatedGame $staged $isolatedReceipt | Out-Null
if(Test-Path -LiteralPath "$isolatedGame/BepInEx/plugins/Example/smoke.dll"){throw 'Loader restoration installed a bundled diagnostic plugin.'}
Uninstall-Loader $isolatedReceipt | Out-Null
Set-Content -LiteralPath "$isolatedGame/winhttp.dll" -Value 'independently owned proxy'
$rejected=$false;try{Install-Loader $isolatedGame $staged (Join-Path $root 'proxy-conflict.json')|Out-Null}catch{$rejected=$true}
if(-not $rejected -or (Get-Content -LiteralPath "$isolatedGame/winhttp.dll") -ne 'independently owned proxy' -or (Test-Path -LiteralPath "$isolatedGame/version.dll")){throw 'Conflicting proxy was not preserved/refused before installation.'}
if((Get-FileHash -LiteralPath "$isolatedGame/BepInEx/plugins/UserMod/existing.dll").Hash -ne $otherModHash){throw 'Loader restoration/removal changed an independently owned mod.'}
$rejected=$false;try{Copy-LoaderRuntimePayload $bundle (Join-Path $bundle 'nested') | Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Loader staging accepted recursive source/destination containment.'}
$rejected=$false;try{Copy-LoaderRuntimePayload $bundle $staged | Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Loader staging overwrote an existing destination.'}
Write-Output 'PASS: loader-only restoration/removal preserves exact runtime/source/independent-mod bytes, excludes plugins/patchers and refuses recursive/colliding staging.'
Write-Output "PASS: receipt install/removal, original preservation, collision, modified-file guard and path containment. Fixtures: $root"
