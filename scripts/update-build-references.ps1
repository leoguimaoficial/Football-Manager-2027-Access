#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GamePath)
. "$PSScriptRoot/Common.ps1"
$ErrorActionPreference='Stop'
$repository='leoguimaoficial/FM27Access-Build-References'
$baseline=Join-Path $ProjectRoot 'packaging/build-references.json'
Invoke-DotNet -Arguments @('build',"$ProjectRoot/tools/game-inspect",'-c','Release','--nologo','--verbosity','quiet')
$work=Join-Path $ProjectRoot ('.tools/reference-refresh-'+[Guid]::NewGuid().ToString('N'))
$export=Join-Path $work 'export'
$checkout=Join-Path $work 'private'
New-Item -ItemType Directory -Path $work | Out-Null
$raw=& $DotNet "$ProjectRoot/tools/game-inspect/bin/Release/net10.0/game-inspect.dll" export-build-references ([IO.Path]::GetFullPath($GamePath)) $export
if($LASTEXITCODE -ne 0){throw 'Reference export failed. Let the pinned BepInEx regenerate interop for the updated game first.'}
$result=$raw|ConvertFrom-Json
$pin=Get-Content -LiteralPath $baseline -Raw|ConvertFrom-Json
if($pin.identity -eq $result.identity){Write-Output 'Cloud compiler references are already current.';return}
# Confirm visibility using existing Git Credential Manager authentication. Never
# store this credential in the checkout, remote URL, pipeline or command output.
$credentialLines=@('protocol=https','host=github.com','')| & git credential fill 2>$null
if($LASTEXITCODE -ne 0){throw 'GitHub authentication is required to refresh private references.'}
$credential=@{}
foreach($line in $credentialLines){$pair=$line.Split('=',2);if($pair.Length -eq 2){$credential[$pair[0]]=$pair[1]}}
if(!$credential.password){throw 'Missing GitHub authentication.'}
$headers=@{Authorization='Bearer '+$credential.password;Accept='application/vnd.github+json';'User-Agent'='FM27Access-Build-References'}
$credential.Clear();$credentialLines=$null
try{$remote=Invoke-RestMethod -Uri "https://api.github.com/repos/$repository" -Headers $headers}finally{$headers.Clear()}
if($remote.private -ne $true -or $remote.full_name -ne $repository){throw 'Compiler input repository must be private.'}
& git clone --quiet --branch main --single-branch "https://github.com/$repository.git" $checkout
if($LASTEXITCODE -ne 0){throw 'Could not clone private references.'}
$archive=Join-Path $work 'build-references.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($export,$archive)
if((Get-Item -LiteralPath $archive).Length -ge 95MB){throw 'Reference archive exceeds the private Git input size limit.'}
Copy-Item -LiteralPath $archive -Destination "$checkout/build-references.zip" -Force
@{schema=1;file='build-references.zip';sha256=(Get-FileHash -LiteralPath $archive).Hash} | ConvertTo-Json | Set-Content -LiteralPath "$checkout/reference-index.json" -Encoding utf8
$identityName=(& git -C $ProjectRoot config user.name)
$identityEmail=(& git -C $ProjectRoot config user.email)
if(!$identityName -or !$identityEmail){throw 'Configure the source repository commit identity before refreshing references.'}
& git -C $checkout config user.name $identityName
& git -C $checkout config user.email $identityEmail
& git -C $checkout add -- build-references.zip reference-index.json
& git -C $checkout commit -m 'Refresh generated FM27 compiler references'
if($LASTEXITCODE -ne 0){throw 'Private reference commit failed.'}
& git -C $checkout push origin main
if($LASTEXITCODE -ne 0){throw 'Private reference push failed; public pin was not changed.'}
$manifest=Get-Content -LiteralPath "$export/build-references.json" -Raw|ConvertFrom-Json
@{schema=1;repository=$repository;commit=(& git -C $checkout rev-parse HEAD).Trim();identity=$result.identity;gameVersion=$manifest.Native.GameVersion} |
    ConvertTo-Json | Set-Content -LiteralPath $baseline -Encoding utf8
Write-Output 'Private compiler input refreshed. Review and commit packaging/build-references.json with the tested game adaptation; future cloud builds use that exact input.'
