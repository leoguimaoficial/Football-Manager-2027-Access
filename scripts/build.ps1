[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$GamePath, [switch]$DeveloperTools)
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    if($DeveloperTools -and $Configuration -ne 'Debug'){throw 'Developer tools require explicit -Configuration Debug.'}
    $arguments=@('build','FM27Access.sln','-c',$Configuration,"-p:FM27DeveloperTools=$($DeveloperTools.IsPresent.ToString().ToLowerInvariant())",'--nologo')
    if($GamePath){$arguments+="-p:FM27GameDir=$([IO.Path]::GetFullPath($GamePath))"}
    Invoke-DotNet -Arguments $arguments
} finally { Pop-Location }
