[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GamePath,
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [switch]$DeveloperTools,
    [string[]]$ReplaceReceipt=@()
)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/GameReport.ps1"
. "$PSScriptRoot/ModPayload.ps1"
if($DeveloperTools -and $Configuration -ne 'Debug'){throw 'Developer tools require explicit -Configuration Debug.'}
Import-Module "$PSScriptRoot/Loader.psm1" -Force
Import-Module "$PSScriptRoot/../packaging/player/LoggingProfile.psm1" -Force
Assert-GameClosed $GamePath
$report=Get-VerifiedGameReport -GamePath $GamePath
$profile=Get-ModDeploymentProfile $report
Write-Output "Native deployment profile $($profile.Version): $($profile.Evidence). See docs/KNOWN_ISSUES.md for actual coverage."
Push-Location $ProjectRoot
try {
    $developerToolsProperty=$DeveloperTools.IsPresent.ToString().ToLowerInvariant()
    Invoke-DotNet -Arguments @('build','FM27Access.sln','-c',$Configuration,"-p:FM27GameDir=$([IO.Path]::GetFullPath($GamePath))","-p:FM27DeveloperTools=$developerToolsProperty",'--nologo')
    $id=[Guid]::NewGuid().ToString('N')
    $payload=Join-Path $ProjectRoot "artifacts/payload-$id"
    New-ModPayload -Destination $payload -Configuration $Configuration -DeveloperTools:$DeveloperTools -NativeReport $report
    # Validate the entire upgrade before removing any owned file.
    $owned=@{}
    foreach($previous in $ReplaceReceipt){
        $old=Get-Content -LiteralPath $previous -Raw | ConvertFrom-Json
        if($old.schema -ne 1 -or $old.state -ne 'installed' -or [IO.Path]::GetFullPath($old.root) -ne [IO.Path]::GetFullPath($GamePath)){throw "Receipt does not match this installation: $previous"}
        foreach($entry in $old.files){
            $target=Get-SafeTarget $GamePath $entry.relative
            if(Test-Path -LiteralPath $target){if((Get-FileHash -LiteralPath $target).Hash -ne $entry.sha256){throw "Modified file preserved: $target"}}
            $owned[$target]=$true
        }
    }
    foreach($file in Get-ChildItem -LiteralPath $payload -Recurse -File){
        $target=Get-SafeTarget $GamePath ([IO.Path]::GetRelativePath($payload,$file.FullName))
        if((Test-Path -LiteralPath $target) -and -not $owned.ContainsKey($target)){throw "Unowned file collision: $target"}
    }
    foreach($previous in $ReplaceReceipt){
        if(Test-Path -LiteralPath "$previous.logging.json"){Restore-ProductionLogging -ReceiptPath "$previous.logging.json"}
        Uninstall-Loader -ReceiptPath $previous
    }
    $receipt=Join-Path $ProjectRoot "artifacts/install/mod-$id.json"
    Install-Loader -GamePath $GamePath -Payload $payload -ReceiptPath $receipt
    Set-Content -LiteralPath (Join-Path $ProjectRoot 'artifacts/install/active-mod.txt') -Value $receipt -Encoding utf8
    if(-not $DeveloperTools){Set-ProductionLogging -GamePath $GamePath -ReceiptPath "$receipt.logging.json"}
    & "$PSScriptRoot/clean-local-artifacts.ps1"
    Write-Output "Host/Plugin installed. Removal receipt: $receipt"
} finally { Pop-Location }
