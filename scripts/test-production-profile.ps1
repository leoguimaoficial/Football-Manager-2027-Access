#Requires -Version 7.2
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    foreach($profile in @(
        @{Arguments=@();Configuration='Release';DeveloperTools='false'},
        @{Arguments=@('-p:Configuration=Debug');Configuration='Debug';DeveloperTools='false'},
        @{Arguments=@('-p:Configuration=Debug','-p:FM27DeveloperTools=true');Configuration='Debug';DeveloperTools='true'})){
        $arguments=@('msbuild','FM27Access.Plugin','-getProperty:Configuration,FM27DeveloperTools')+$profile.Arguments
        $raw=& $DotNet @arguments
        if($LASTEXITCODE -ne 0){throw 'Build profile property evaluation failed.'}
        $properties=($raw | ConvertFrom-Json).Properties
        if($properties.Configuration -ne $profile.Configuration -or $properties.FM27DeveloperTools -ne $profile.DeveloperTools){throw 'Unexpected default or explicit build profile.'}
    }
    $solution=& $DotNet msbuild FM27Access.sln -target:ValidateSolutionConfiguration -verbosity:normal
    if($LASTEXITCODE -ne 0 -or ($solution -join "`n") -notmatch 'Release\|Any CPU'){throw 'Solution does not default to Release.'}
    foreach($properties in @(
        @('-p:Configuration=Release','-p:FM27DeveloperTools=true'),
        @('-p:Configuration=Debug','-p:FM27DeveloperTools=invalid'))){
        $arguments=@('msbuild','FM27Access.Plugin','-target:ValidateFM27BuildProfile','-verbosity:quiet')+$properties
        $rejection=& $DotNet @arguments 2>&1
        if($LASTEXITCODE -eq 0 -or ($rejection -join "`n") -notmatch 'Developer tools require|FM27DeveloperTools must'){throw 'Unsafe build profile was not explicitly rejected.'}
    }
    Write-Output 'PASS: project/solution default to Release; Debug requires explicit developer opt-in; Release refuses diagnostics and invalid flags.'
} finally {Pop-Location}
