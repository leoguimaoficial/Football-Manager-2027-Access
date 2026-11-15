$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1" -Force
function Assert-Rejected([scriptblock]$Action,[string]$Label){$rejected=$false;try{& $Action | Out-Null}catch{$rejected=$true};if(!$rejected){throw "Expected refusal: $Label"}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('fm27-player-test-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $root 'game';$package=Join-Path $root 'package';$payload=Join-Path $package 'payload'
New-Item -ItemType Directory -Force $game,$payload | Out-Null
Copy-Item "$PSScriptRoot/../packaging/player/*" -Destination $package
Copy-Item -LiteralPath "$PSScriptRoot/Loader.psm1" -Destination $package
Import-Module "$package/PlayerPackage.psm1" -Force
Import-Module "$package/LoggingProfile.psm1" -Force
Import-Module "$package/InteropContract.psm1" -Force
Set-Content -LiteralPath "$game/fm.exe" -Value 'synthetic fixture'
Set-Content -LiteralPath "$game/MicrosoftGame.config" -Value '<Game><Identity Name="Synthetic.Test" Version="1.2.3.4" /></Game>'
Set-Content -LiteralPath "$game/native.bin" -Value 'native fixture'
Set-Content -LiteralPath "$payload/owned.dll" -Value 'mod fixture'
Add-Type -TypeDefinition 'public class Fm27PlayerInteropContractFixture { public static int Read(int value) => value; }' -OutputAssembly "$game/interop.dll"
$shape=Get-InteropContractHash "$game/interop.dll"
$stream=[IO.File]::OpenRead("$game/interop.dll");$pe=[Reflection.PortableExecutable.PEReader]::new($stream)
try {$metadata=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe);$mvid=$metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToByteArray();$timestamp=$pe.PEHeaders.CoffHeaderStartOffset+4}
finally {$pe.Dispose();$stream.Dispose()}
function Find-Bytes([byte[]]$Bytes,[byte[]]$Pattern){
    for($i=0;$i -le $Bytes.Length-$Pattern.Length;$i++){$match=$true;for($j=0;$j -lt $Pattern.Length;$j++){if($Bytes[$i+$j] -ne $Pattern[$j]){$match=$false;break}};if($match){return $i}}
    throw 'Fixture marker missing.'
}
$changed=[IO.File]::ReadAllBytes("$game/interop.dll")
$originalByteHash=(Get-FileHash "$game/interop.dll").Hash
[Array]::Copy([Guid]::NewGuid().ToByteArray(),0,$changed,(Find-Bytes $changed $mvid),16)
$changed[$timestamp]=$changed[$timestamp] -bxor 1
[IO.File]::WriteAllBytes("$game/interop.dll",$changed)
if((Get-FileHash "$game/interop.dll").Hash -eq $originalByteHash -or (Get-InteropContractHash "$game/interop.dll") -ne $shape){throw 'Machine generation MVID/timestamp changed the declaration contract.'}
$manifest=@{schema=2;version='test';channel='candidate';developerTools=$false;native=@{identity='Synthetic.Test';version='1.2.3.4';interopContract='fm27-interop-metadata-shape-v1';interop=@(@{relative='interop.dll';sha256=$shape;byteSha256=$originalByteHash});files=@(@{relative='native.bin';sha256=(Get-FileHash "$game/native.bin").Hash})};files=@(@{relative='owned.dll';sha256=(Get-FileHash "$payload/owned.dll").Hash})}
$manifest | ConvertTo-Json -Depth 8 | Set-Content "$package/manifest.json"
$checked=Test-PlayerPackage $package
Assert-PlayerNativeBuild $game $checked
$entryReceipt=Join-Path $root 'entrypoint-mod.json'
& "$package/install.ps1" -GamePath $game -ReceiptPath $entryReceipt | Out-Null
if(!(Test-Path "$entryReceipt.logging.json") -or !(Test-Path "$game/owned.dll")){throw 'Player installer entrypoint failed.'}
& "$package/uninstall.ps1" -ReceiptPath $entryReceipt | Out-Null
if((Test-Path "$game/owned.dll") -or (Test-Path "$game/BepInEx/config/BepInEx.cfg")){throw 'Player uninstaller did not restore the original absent configuration.'}
$member=[Text.Encoding]::UTF8.GetBytes('Read')
[Array]::Copy([Text.Encoding]::UTF8.GetBytes('Edit'),0,$changed,(Find-Bytes $changed $member),$member.Length)
[IO.File]::WriteAllBytes("$game/interop.dll",$changed)
Assert-Rejected {Assert-PlayerNativeBuild $game $checked} 'changed interop API'
# Subsequent native-file mismatch must be independent of the API mutation.
$checked.native.interop[0].sha256=Get-InteropContractHash "$game/interop.dll"
$receipt=Join-Path $root 'mod.json'
Install-Loader $game $payload $receipt | Out-Null
Uninstall-Loader $receipt | Out-Null
if(!(Test-Path "$game/fm.exe") -or (Test-Path "$game/owned.dll")){throw 'Player payload removal lost ownership boundaries.'}
Set-Content "$payload/extra.dll" 'unexpected'
Assert-Rejected {Test-PlayerPackage $package} 'unlisted payload'
Remove-Item -LiteralPath "$payload/extra.dll"
Set-Content "$payload/owned.dll" 'tampered'
Assert-Rejected {Test-PlayerPackage $package} 'tampered payload'
Set-Content "$game/native.bin" 'different pair'
Assert-Rejected {Assert-PlayerNativeBuild $game $checked} 'mixed native pair'
$manifest.files[0].relative='../escape.dll'
$manifest | ConvertTo-Json -Depth 8 | Set-Content "$package/manifest.json"
Assert-Rejected {Test-PlayerPackage $package} 'escaped payload'
$cfg=Join-Path $game 'BepInEx/config/BepInEx.cfg'
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($cfg)) | Out-Null
$original=[Text.Encoding]::UTF8.GetBytes("# preserve comment`r`n[Other]`r`nValue = unchanged`r`n[Logging.Console]`r`nEnabled = true`r`n[Logging.Disk]`r`nEnabled = false`r`nLogLevels = Error`r`n")
[IO.File]::WriteAllBytes($cfg,$original)
$logReceipt=Join-Path $root 'logging.json'
Set-ProductionLogging $game $logReceipt
$text=Get-Content $cfg -Raw
if($text -notmatch '(?s)\[Other\].*?Value = unchanged' -or $text -notmatch '(?s)\[Logging.Console\].*?Enabled = false' -or $text -notmatch '(?s)\[Logging.Disk\].*?Enabled = true'){throw 'Production logging profile lost settings.'}
if($text -notmatch '(?m)^LogLevels = Fatal, Error, Warning\r?$'){throw 'Production disk logging includes verbose diagnostics or omits actionable errors.'}
Restore-ProductionLogging $logReceipt
if([Convert]::ToBase64String([IO.File]::ReadAllBytes($cfg)) -ne [Convert]::ToBase64String($original)){throw 'Configuration backup did not restore exact bytes.'}
$second=Join-Path $root 'logging-modified.json'
Set-ProductionLogging $game $second
Add-Content $cfg '# user changed configuration'
Assert-Rejected {Restore-ProductionLogging $second} 'modified configuration'
Assert-Rejected {Get-ProductionLoggingText "[Logging.Console]`nEnabled = true`nEnabled = false`n"} 'ambiguous settings'
$diag=Join-Path $root 'diagnostics'
New-Item -ItemType Directory -Force "$game/BepInEx" | Out-Null
Set-Content "$game/BepInEx/LogOutput.log" "Cookie: fm27session=DO_NOT_EXPORT`npath C:\Users\PrivateName\game"
& "$PSScriptRoot/collect-player-diagnostics.ps1" -GamePath $game -OutputPath $diag | Out-Null
$diagnostic=Get-Content "$diag/LogOutput.log" -Raw
if($diagnostic -match 'DO_NOT_EXPORT|PrivateName'){throw 'Known session/user-path fields leaked into diagnostics.'}
$sourceGame=Join-Path $root 'source-entrypoint-game'
$sourceReceipt=Join-Path $root 'source-entrypoint.json'
New-Item -ItemType Directory -Force $sourceGame | Out-Null
Set-Content -LiteralPath "$sourceGame/fm.exe" -Value 'synthetic source install fixture'
Install-Loader -GamePath $sourceGame -Payload $payload -ReceiptPath $sourceReceipt | Out-Null
Set-ProductionLogging -GamePath $sourceGame -ReceiptPath "$sourceReceipt.logging.json"
& "$PSScriptRoot/uninstall-loader.ps1" -ReceiptPath $sourceReceipt
if((Test-Path -LiteralPath "$sourceGame/owned.dll") -or (Test-Path -LiteralPath "$sourceGame/BepInEx/config/BepInEx.cfg")){throw 'Source removal entrypoint did not restore paired logging ownership.'}
Write-Output "PASS: player payload install/removal, source paired-logging removal, integrity, native mismatch, MVID/timestamp-independent interop shape, changed API refusal, path escape, exact logging restoration, changed-config preservation and bounded/redacted local diagnostics. Fixtures: $root"
