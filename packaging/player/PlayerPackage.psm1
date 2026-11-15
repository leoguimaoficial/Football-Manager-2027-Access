Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Loader.psm1"
Import-Module "$PSScriptRoot/InteropContract.psm1"

function Test-PlayerPackage([string]$PackageRoot) {
    $manifest=Get-Content -LiteralPath (Join-Path $PackageRoot 'manifest.json') -Raw | ConvertFrom-Json
    if($manifest.schema -notin 2,3 -or $manifest.developerTools -ne $false -or $manifest.channel -ne 'candidate'){throw 'Unsupported player package manifest.'}
    if(($manifest.schema -eq 2 -and $manifest.native.interopContract -ne 'fm27-interop-metadata-shape-v1') -or ($manifest.schema -eq 3 -and $manifest.native.interopContract -ne 'fm27-required-managed-api-v1')){throw 'Unsupported native compatibility contract.'}
    $payload=Join-Path $PackageRoot 'payload'
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $manifest.files){
        $path=Get-SafeTarget $payload $entry.relative
        if(!$seen.Add([IO.Path]::GetFullPath($path))){throw 'Duplicate package path.'}
        if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256){throw "Package file missing or changed: $($entry.relative)"}
    }
    $actual=@(Get-ChildItem -LiteralPath $payload -Recurse -File)
    if($actual.Count -eq 0 -or $actual.Count -ne $seen.Count){throw 'Unexpected payload files; package preserved.'}
    foreach($file in $actual){if(!$seen.Contains($file.FullName)){throw 'Unlisted payload file.'}}
    return $manifest
}

function Assert-PlayerNativeBuild([string]$GamePath,$Manifest) {
    if($Manifest.schema -eq 3){
        $plugin=Join-Path $PSScriptRoot 'payload/BepInEx/plugins/FM27Access'
        Add-Type -Path (Join-Path $plugin 'FM27Access.Speech.dll')
        $result=[FM27Access.Speech.NativeBuildGuard]::Validate($GamePath,(Join-Path $plugin 'native-build.json'))
        if(!$result.Supported){throw $result.Reason}
        Write-Output $result.Reason
        return
    }
    if(!(Test-Path -LiteralPath (Join-Path $GamePath 'fm.exe') -PathType Leaf)){throw 'Choose the Football Manager game root containing fm.exe.'}
    $config=Join-Path $GamePath 'MicrosoftGame.config'
    if(!(Test-Path -LiteralPath $config)){throw 'This candidate is qualified for the named Xbox PC build only; Steam is pending.'}
    [xml]$identity=Get-Content -LiteralPath $config
    if($identity.Game.Identity.Name -ne $Manifest.native.identity -or $identity.Game.Identity.Version -ne $Manifest.native.version){throw 'Xbox package identity/version differs from the qualified candidate.'}
    foreach($entry in $Manifest.native.files){
        $path=Get-SafeTarget $GamePath $entry.relative
        if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256){throw "Native/loader/interop prerequisite differs: $($entry.relative). Do not copy generated interop from another installation. See README.txt."}
    }
    foreach($entry in $Manifest.native.interop){
        $path=Get-SafeTarget $GamePath $entry.relative
        if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing locally generated interop: $($entry.relative)."}
        # The qualified bytes are a fast path, never a portability requirement.
        if((Get-FileHash -LiteralPath $path).Hash -eq $entry.byteSha256){continue}
        if((Get-InteropContractHash $path) -ne $entry.sha256){throw "Generated interop declarations differ: $($entry.relative). Regenerate locally with the qualified loader; never copy game assemblies from another player."}
    }
}
Export-ModuleMember -Function Test-PlayerPackage,Assert-PlayerNativeBuild
