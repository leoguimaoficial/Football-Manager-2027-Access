[CmdletBinding()]
param([Parameter(Mandatory)][string]$GamePath)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/GameReport.ps1"
. "$PSScriptRoot/ModPayload.ps1"
. "$PSScriptRoot/PackageRetention.ps1"
$report=Get-VerifiedGameReport $GamePath
$profile=Get-ModDeploymentProfile $report
Push-Location $ProjectRoot
try {
    Invoke-DotNet -Arguments @('build','FM27Access.sln','-c','Release',"-p:FM27GameDir=$([IO.Path]::GetFullPath($GamePath))",'-p:FM27DeveloperTools=false','--nologo') | Out-Host
    $version=([xml](Get-Content Directory.Build.props -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    $out=Join-Path $ProjectRoot ('artifacts/packages/FM27Access-'+$version+'-candidate-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $out | Out-Null
    New-ModPayload -Destination (Join-Path $out 'payload') -Configuration Release -NativeReport $report
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'packaging/player') -Destination "$out/player-scripts" -Recurse
    Get-ChildItem -LiteralPath "$out/player-scripts" -File | Move-Item -Destination $out
    Remove-Item -LiteralPath "$out/player-scripts"
    Copy-Item -LiteralPath "$PSScriptRoot/Loader.psm1" -Destination $out
    Copy-Item -LiteralPath "$PSScriptRoot/collect-player-diagnostics.ps1" -Destination "$out/collect-diagnostics.ps1"
    Import-Module "$out/InteropContract.psm1" -Force
    $nativeFiles=@('GameAssembly.dll','fm_Data/il2cpp_data/Metadata/global-metadata.dat',(Get-LoaderProxy $GamePath))
    $nativeFiles+=@(Get-ChildItem -LiteralPath (Join-Path $GamePath 'BepInEx/core') -File -Filter '*.dll' | ForEach-Object {[IO.Path]::GetRelativePath($GamePath,$_.FullName)})
    $manifest=[ordered]@{schema=3;version=$version;channel='candidate';developerTools=$false;createdUtc=[DateTime]::UtcNow.ToString('o');companionRuntime='10.0.15';
        native=@{identity=$report.Identity;version=$profile.Version;evidence=$profile.Evidence;interopContract='fm27-required-managed-api-v1';files=@($nativeFiles | ForEach-Object {@{relative=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $GamePath $_)).Hash}})};
        files=@(Get-ChildItem -LiteralPath "$out/payload" -Recurse -File | ForEach-Object {@{relative=[IO.Path]::GetRelativePath("$out/payload",$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$out/manifest.json" -Encoding utf8
    Import-Module "$out/PlayerPackage.psm1" -Force
    Test-PlayerPackage $out | Out-Null
    & "$PSScriptRoot/test-player-binaries.ps1" -PackageRoot $out | Out-Host
    Compress-Archive -Path "$out/*" -DestinationPath "$out.zip"
    Remove-SupersededPackages -PackageRoot ([IO.Path]::GetDirectoryName($out)) -Kind player -Keep $out
    Get-FileHash -LiteralPath "$out.zip" | Select-Object Hash,Path
    Write-Output "Candidate only; clean-machine first install, Steam and full-career qualification remain pending. Package: $out.zip"
} finally {Pop-Location}
