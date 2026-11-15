#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory,ParameterSetName='Game')][string]$GamePath,
      [Parameter(Mandatory,ParameterSetName='References')][string]$BuildReferencesPath,
      [string]$OutputPath="$PSScriptRoot/../artifacts/distribution",
      [string]$RunId='local', [string]$Commit)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/GameReport.ps1"
. "$PSScriptRoot/ModPayload.ps1"
$ErrorActionPreference='Stop'
if(!$Commit){$Commit=(& git -C $ProjectRoot rev-parse HEAD).Trim()}
if($Commit -notmatch '^[0-9a-f]{40}$' -or $RunId -notmatch '^(local|[0-9]+)$'){throw 'Invalid provenance.'}
if((& git -C $ProjectRoot rev-parse HEAD).Trim() -ne $Commit){throw 'Checkout differs from the requested commit.'}
$sourceDirty=@(& git -C $ProjectRoot status --porcelain).Count -gt 0
if($RunId -ne 'local' -and $sourceDirty){throw 'Official build requires a clean checkout, including untracked source.'}
$out=[IO.Path]::GetFullPath($OutputPath)
if(Test-Path -LiteralPath $out){throw 'Distribution destination already exists; use a fresh directory.'}
$work=Join-Path $ProjectRoot ('artifacts/distribution-work-'+[Guid]::NewGuid().ToString('N'))
$payload=Join-Path $work 'payload'
New-Item -ItemType Directory -Path $work,$out | Out-Null
try {
    $lock=Get-Content -LiteralPath "$ProjectRoot/packaging/loader.lock.json" -Raw | ConvertFrom-Json
    $loaderZip=Join-Path $work 'loader.zip'
    Invoke-WebRequest -Uri $lock.url -OutFile $loaderZip
    if((Get-FileHash -LiteralPath $loaderZip).Hash -ne $lock.sha256){throw 'Pinned loader archive checksum mismatch.'}
    Expand-Archive -LiteralPath $loaderZip -DestinationPath "$work/loader"
    if($BuildReferencesPath){
        Invoke-DotNet -Arguments @('build',"$ProjectRoot/tools/game-inspect",'-c','Release','--nologo','--verbosity','quiet')
        Invoke-DotNet -Arguments @("$ProjectRoot/tools/game-inspect/bin/Release/net10.0/game-inspect.dll",'check-private-reference-baseline',$BuildReferencesPath,"$ProjectRoot/packaging/build-references.json")
        $raw=& $DotNet "$ProjectRoot/tools/game-inspect/bin/Release/net10.0/game-inspect.dll" validate-build-references ([IO.Path]::GetFullPath($BuildReferencesPath))
        if($LASTEXITCODE -ne 0){throw 'Private build references failed validation.'}
        $report=$raw|ConvertFrom-Json
        $GamePath=$report.Path
        New-Item -ItemType Directory -Force "$GamePath/BepInEx"|Out-Null
        Copy-Item -LiteralPath "$work/loader/BepInEx/core" -Destination "$GamePath/BepInEx/core" -Recurse
        Copy-Item -LiteralPath "$work/loader/winhttp.dll" -Destination "$GamePath/winhttp.dll"
    }else{
        $report=Get-VerifiedGameReport $GamePath
        Invoke-DotNet -Arguments @("$ProjectRoot/tools/game-inspect/bin/Release/net10.0/game-inspect.dll",'check-build-reference-baseline',$GamePath,"$ProjectRoot/packaging/build-references.json")
    }
    $profile=Get-ModDeploymentProfile $report
    Invoke-DotNet -Arguments @('build',"$ProjectRoot/FM27Access.sln",'-c','Release',"-p:FM27GameDir=$([IO.Path]::GetFullPath($GamePath))",'-p:FM27DeveloperTools=false','--nologo')
    New-ModPayload -Destination $payload -NativeReport $report -Configuration Release -ReferenceBuild:([bool]$BuildReferencesPath)
    # Add only the pristine redistributable loader. Never copy a local installation,
    # generated interop, caches, Unity libraries, plugins, patchers or game binaries.
    foreach($name in 'winhttp.dll','doorstop_config.ini','.doorstop_version','dotnet','BepInEx/core'){
        $source=Join-Path "$work/loader" $name
        if(!(Test-Path -LiteralPath $source)){throw "Required pinned loader component missing: $name"}
        $target=Join-Path $payload $name
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Recurse
    }
    $plugin=Join-Path $payload 'BepInEx/plugins/FM27Access'
    New-Item -ItemType Directory -Force "$payload/BepInEx/config" | Out-Null
    Copy-Item -LiteralPath "$ProjectRoot/packaging/BepInEx.production.cfg" -Destination "$payload/BepInEx/config/BepInEx.cfg"
    $deployment=Get-Content -LiteralPath "$plugin/native-build.json" -Raw | ConvertFrom-Json
    foreach($file in $deployment.LoaderFiles){
        $path=Join-Path $payload $file.Relative
        if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.Sha256){throw "Build references differ from pinned loader: $($file.Relative)"}
    }
    # Reuse the existing production binary/profile inspection unchanged.
    $version=([xml](Get-Content -LiteralPath "$ProjectRoot/Directory.Build.props" -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    @{native=@{version=$deployment.Version;files=@(@{relative='GameAssembly.dll';sha256=$deployment.Assembly},@{relative='fm_Data/il2cpp_data/Metadata/global-metadata.dat';sha256=$deployment.Metadata})}} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$work/manifest.json" -Encoding utf8
    & "$PSScriptRoot/test-player-binaries.ps1" -PackageRoot $work
    & "$ProjectRoot/installer/get-distribution-installer.ps1" -OutputPath "$work/installer"
    Copy-Item -LiteralPath "$work/installer/FM27Access-Installer.exe" -Destination $out
    Copy-Item -LiteralPath "$ProjectRoot/docs/user/FM27Access-UserGuide.html" -Destination $out
    $package="FM27Access-v$version.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,(Join-Path $out $package))
    $files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/')
        @{path=$relative;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;size=$_.Length;kind=$(if($relative.StartsWith('BepInEx/plugins/FM27Access/')){'mod'}elseif($relative -eq 'BepInEx/config/BepInEx.cfg'){'config'}else{'loader'})}
    })
    $manifest=@{schema=1;version=$version;minimumInstallerVersion='1.0.0';package=$package;packageSha256=(Get-FileHash -LiteralPath (Join-Path $out $package)).Hash;
        packageSize=(Get-Item -LiteralPath (Join-Path $out $package)).Length;bepInExVersion=$lock.version;platforms=@('Steam','Xbox','Epic','Manual');
        compatibility='Windows x64; Unity 6000.3.x; IL2CPP metadata 39; fresh locally generated interop and the bundled required managed API contract. Xbox has targeted live evidence; Steam/Epic gameplay and clean-machine first launch remain unqualified.';files=$files}
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$out/FM27Access-Manifest.json" -Encoding utf8
    $engine=([xml](Get-Content -LiteralPath "$ProjectRoot/installer/InstallerVersion.props" -Raw)).Project.PropertyGroup.InstallerEngineVersion
    @{schema=1;version=$engine;sha256=(Get-FileHash -LiteralPath "$out/FM27Access-Installer.exe").Hash;size=(Get-Item -LiteralPath "$out/FM27Access-Installer.exe").Length;
        url="https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/download/v$version/FM27Access-Installer.exe"} | ConvertTo-Json | Set-Content -LiteralPath "$out/FM27Access-Installer.json" -Encoding utf8
    $referencePin=Get-Content -LiteralPath "$ProjectRoot/packaging/build-references.json" -Raw|ConvertFrom-Json
    @{schema=1;version=$version;installerVersion=$engine;commit=$Commit;sourceDirty=$sourceDirty;runId=$RunId;configuration='Release';developerTools=$false;referencesCommit=$referencePin.commit;referencesIdentity=$referencePin.identity;createdUtc=[DateTime]::UtcNow.ToString('o')} |
        ConvertTo-Json | Set-Content -LiteralPath "$out/build-info.json" -Encoding utf8
    # Source is taken from Git's allowlist, never from the build machine's prerequisites.
    & "$PSScriptRoot/package-source.ps1"
    $source=Get-ChildItem -LiteralPath "$ProjectRoot/artifacts/packages" -Filter 'source-*.zip' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    Copy-Item -LiteralPath $source.FullName -Destination "$out/FM27Access-v$version-source.zip"
    Get-ChildItem -LiteralPath $out -File | Sort-Object Name | ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $($_.Name)"} |
        Set-Content -LiteralPath "$out/SHA256SUMS.txt" -Encoding utf8
    & "$PSScriptRoot/verify-distribution.ps1" -Path $out -ExpectedCommit $Commit -ExpectedRunId $RunId -ExpectedVersion $version
    Write-Output "Prepared distribution candidate: $out. No release or tag created."
} finally {
    $verifiedWork=[IO.Path]::GetFullPath($work)
    $workspaceArtifacts=[IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
    if(!$verifiedWork.StartsWith($workspaceArtifacts,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe staging cleanup target.'}
    if(Test-Path -LiteralPath $verifiedWork){Remove-Item -LiteralPath $verifiedWork -Recurse -Force}
}
