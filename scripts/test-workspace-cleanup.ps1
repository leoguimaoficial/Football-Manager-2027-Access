$ErrorActionPreference='Stop'
. "$PSScriptRoot/WorkspaceCleanup.ps1"
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('fm27-workspace-test-'+[Guid]::NewGuid().ToString('N'))
$outside=Join-Path $fixtureRoot 'outside'
$fixture=Join-Path $fixtureRoot 'source'
New-Item -ItemType Directory -Path $fixture,$outside -Force | Out-Null
function Put([string]$Name,[string]$Content='retained'){
    $path=Join-Path $fixture $Name
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
    [IO.File]::WriteAllText($path,$Content)
}
function Require([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
Put 'FM27Access.sln'
Put 'global.json' '{"sdk":{"version":"10.0.403"}}'
foreach($name in 'FM27Access.Plugin/source.cs','.git/HEAD','.upstream/fm26/source.cs',
    '.tools/dotnet/sdk/10.0.403/compiler','.tools/dotnet/shared/Microsoft.NETCore.App/10.0.14/runtime',
    'Prism/dynamic/release/bin/prism.dll','Prism/NOTICE','artifacts/install/current.json','artifacts/current-verification.json','.tools/custom-source/readme.txt'){
    Put $name $(if($name.EndsWith('.json')){'{}'}else{'retained'})
}
foreach($name in 'FM27Access.Plugin/bin/output.dll','FM27Access.Plugin/obj/cache',
    'tests/companion/node_modules/module','.tools/dotnet/sdk/8.0.427/compiler',
    '.tools/diagnostics/tool','Prism/static/lib','artifacts/packages/old.zip','artifacts/old.log'){Put $name 'generated'}
$targets=@(Get-WorkspaceCleanupTargets -Root $fixture)
$outsideFile=Join-Path $outside 'personal.txt';[IO.File]::WriteAllText($outsideFile,'outside')
$refused=$false;try{Remove-WorkspaceCleanupTargets -Root $fixture -Targets @($outside) | Out-Null}catch{$refused=$true}
Require $refused 'Outside roots must be refused.'
$linked=Join-Path $fixture 'FM27Access.Host/bin'
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($linked)) -Force | Out-Null
New-Item -ItemType Junction -Path $linked -Target $outside | Out-Null
$refused=$false;try{Remove-WorkspaceCleanupTargets -Root $fixture -Targets @($linked) | Out-Null}catch{$refused=$true}
Require $refused 'Junctions must be refused before deleting any output.'
Remove-Item -LiteralPath $linked -Force
$nestedLink=Join-Path $fixture 'FM27Access.Plugin/bin/linked'
New-Item -ItemType Junction -Path $nestedLink -Target $outside | Out-Null
$refused=$false;try{Remove-WorkspaceCleanupTargets -Root $fixture -Targets $targets | Out-Null}catch{$refused=$true}
Require $refused 'Nested links must be refused before the first deletion.'
Require (Test-Path -LiteralPath (Join-Path $fixture 'FM27Access.Plugin/bin/output.dll')) 'Preflight must not partially delete outputs.'
Remove-Item -LiteralPath $nestedLink -Force
$receiptFile=Join-Path $fixture 'artifacts/install/current.json'
@{files=@(@{source=(Join-Path $fixture 'artifacts/packages/old.zip')})} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptFile
$refused=$false;try{Get-WorkspaceCleanupTargets -Root $fixture | Out-Null}catch{$refused=$true}
Require $refused 'Receipt-owned source must be preserved.'
Set-Content -LiteralPath $receiptFile -Value '{}'
$preview=@(Remove-WorkspaceCleanupTargets -Root $fixture -Targets $targets -WhatIf)
Require ($preview.Count -eq 0) 'WhatIf must not report removals.'
Require (Test-Path -LiteralPath (Join-Path $fixture 'FM27Access.Plugin/bin/output.dll')) 'WhatIf deleted an output.'
$removed=@(Remove-WorkspaceCleanupTargets -Root $fixture -Targets $targets)
Require ($removed.Count -eq $targets.Count) 'Recognized generated outputs should be removed.'
foreach($name in 'FM27Access.Plugin/source.cs','.git/HEAD','.upstream/fm26/source.cs',
    '.tools/dotnet/sdk/10.0.403/compiler','Prism/dynamic/release/bin/prism.dll','Prism/NOTICE',
    'artifacts/install/current.json','artifacts/current-verification.json','.tools/custom-source/readme.txt'){
    Require (Test-Path -LiteralPath (Join-Path $fixture $name)) "Required file lost: $name"
}
Require ((Get-Content -LiteralPath $outsideFile -Raw) -eq 'outside') 'Outside content changed.'
# Only this uniquely created fixture is removed; never follow the tested link.
$absoluteFixture=[IO.Path]::GetFullPath($fixtureRoot)
if([IO.Path]::GetDirectoryName($absoluteFixture) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or
   [IO.Path]::GetFileName($absoluteFixture) -notmatch '^fm27-workspace-test-[0-9a-f]{32}$'){throw 'Unsafe fixture cleanup.'}
Remove-Item -LiteralPath $absoluteFixture -Recurse -Force
Write-Output 'PASS: generated outputs removed; source/Git/baseline/SDK10/Prism/receipts retained; outside paths, junctions and receipt-referenced output refused.'
