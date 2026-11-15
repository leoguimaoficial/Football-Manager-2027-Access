[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$GamePath, [switch]$DeveloperTools)
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    if($DeveloperTools -and $Configuration -ne 'Debug'){throw 'Developer tools require explicit -Configuration Debug.'}
    $referenceGame=if($GamePath){$GamePath}else{$env:FM27_GAME_DIR}
    if($referenceGame){
        Invoke-DotNet -Arguments @('build','tools/game-inspect','-c','Release','--nologo','--verbosity','quiet')
        Invoke-DotNet -Arguments @('tools/game-inspect/bin/Release/net10.0/game-inspect.dll','check-build-reference-baseline',$referenceGame,'packaging/build-references.json')
    }
    $arguments=@('build','FM27Access.sln','-c',$Configuration,"-p:FM27DeveloperTools=$($DeveloperTools.IsPresent.ToString().ToLowerInvariant())",'--nologo')
    if($GamePath){$arguments+="-p:FM27GameDir=$([IO.Path]::GetFullPath($GamePath))"}
    Invoke-DotNet -Arguments $arguments
} finally { Pop-Location }
