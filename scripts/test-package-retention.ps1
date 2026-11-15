$ErrorActionPreference='Stop'
. "$PSScriptRoot/PackageRetention.ps1"
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('fm27-package-retention-'+[Guid]::NewGuid())
$packages=Join-Path $fixtureRoot 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
try {
    $old=Join-Path $packages 'source-20260101-000000'
    $current=Join-Path $packages 'source-20260102-000000'
    $unrelated=Join-Path $packages 'personal'
    New-Item -ItemType Directory -Path $old,$current,$unrelated | Out-Null
    Set-Content ($old+'.zip') 'old'
    $rejected=$false
    try {Remove-SupersededPackages $packages source $current | Out-Null}catch{$rejected=$true}
    if(-not $rejected -or -not(Test-Path $old)){throw 'Incomplete replacement removed the preceding package.'}
    Set-Content ($current+'.zip') 'current'
    $outside=Join-Path $fixtureRoot 'source-20260103-000000'
    New-Item -ItemType Directory -Path $outside | Out-Null
    Set-Content ($outside+'.zip') 'outside'
    $rejected=$false
    try {Remove-SupersededPackages $packages source $outside | Out-Null}catch{$rejected=$true}
    if(-not $rejected -or -not(Test-Path $old)){throw 'Outside-root keep target was accepted.'}
    # Junctions must never allow removal to traverse an independently owned tree.
    New-Item -ItemType Junction -Path (Join-Path $old 'linked') -Target $unrelated | Out-Null
    Set-Content (Join-Path $unrelated 'preserved.txt') 'preserve'
    $rejected=$false
    try {Remove-SupersededPackages $packages source $current | Out-Null}catch{$rejected=$true}
    if(-not $rejected -or -not(Test-Path (Join-Path $unrelated 'preserved.txt'))){throw 'Linked package was not preserved.'}
    Remove-Item -LiteralPath (Join-Path $old 'linked') -Force
    Remove-SupersededPackages $packages source $current | Out-Null
    if((Test-Path $old) -or (Test-Path ($old+'.zip')) -or -not(Test-Path $current) -or -not(Test-Path $unrelated)){throw 'Retention removed an incorrect target.'}
    Write-Output 'PASS: package retention preserves incomplete replacements, unrelated files and outside roots; refuses junction traversal and removes only superseded outputs.'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(-not $resolved.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'fm27-package-retention-*'){throw 'Unsafe fixture cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
