. "$PSScriptRoot/Common.ps1"
function Get-ModDeploymentProfile {
    param([Parameter(Mandatory)]$Report)
    # Known fingerprints qualify evidence, not permission by store/build label.
    # Source deployment must still compile and capture/check its actual native API.
    $profiles=Get-Content -LiteralPath (Join-Path $ProjectRoot 'packaging/native-profiles.json') -Raw | ConvertFrom-Json -AsHashtable

    if($Report.Store -in 'Xbox','Steam','Unknown' -and $Report.Architecture -eq 'x64' -and
        $Report.UnityVersion -like '6000.3.*' -and $Report.MetadataVersion -eq 39 -and
        $Report.LoaderVersion -like '6.0.0-be.793*'){
        foreach($profile in $profiles){
            if($Report.AssemblyHash -eq $profile.Assembly -and $Report.MetadataHash -eq $profile.Metadata){
                if($Report.Store -ne 'Xbox' -or $Report.GameVersion -ne $profile.Version){
                    return @{Version=$Report.GameVersion;Assembly=$Report.AssemblyHash;Metadata=$Report.MetadataHash;Evidence='Reference native pair; different store/build label, live qualification pending. Actual API checks still required.'}
                }
                return $profile
            }
        }
        if(@($profiles | Where-Object {$_.Assembly -eq $Report.AssemblyHash}).Count -and @($profiles | Where-Object {$_.Metadata -eq $Report.MetadataHash}).Count){throw 'Native assembly and metadata form a known mixed pair.'}
        return @{Version=$Report.GameVersion;Assembly=$Report.AssemblyHash;Metadata=$Report.MetadataHash;Evidence='Structurally eligible FM27 variant; compilation/native API checks required before deployment. Live qualification pending.'}
    }
    throw 'Native architecture/Unity family/metadata/loader is incompatible with this adapter.'
}

function Get-VerifiedGameReport {
    param([Parameter(Mandatory)][string]$GamePath)
    Invoke-DotNet -Arguments @('build',(Join-Path $ProjectRoot 'tools/game-inspect'),'-c','Release','--nologo','--verbosity','quiet') | Out-Host
    $output=& $DotNet (Join-Path $ProjectRoot 'tools/game-inspect/bin/Release/net10.0/game-inspect.dll') inspect $GamePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the installation.' }
    $report=$output | ConvertFrom-Json
    if ($report.Architecture -ne 'x64') { throw 'Unsupported native architecture for this loader.' }
    if ($report.UnityVersion -notlike '6000.3.*' -or $report.MetadataVersion -ne 39) { throw 'Unity family/metadata differ from the supported adapter.' }
    return $report
}
