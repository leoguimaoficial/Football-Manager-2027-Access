[CmdletBinding()]
param([Parameter(Mandatory)][string]$GamePath,[ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$GameClosed,[switch]$DeveloperTools)
if($DeveloperTools -and $Configuration -ne 'Debug'){throw 'Developer tools require explicit -Configuration Debug.'}
if(-not $GameClosed -and -not $DeveloperTools){throw 'Production has no reload endpoint. Use a closed-game installation; live development reload requires -Configuration Debug -DeveloperTools.'}
. "$PSScriptRoot/Common.ps1"
Import-Module "$PSScriptRoot/Loader.psm1" -Force
if($GameClosed){Assert-GameClosed $GamePath}
Push-Location $ProjectRoot
try {
    $receiptPath=(Get-Content -LiteralPath 'artifacts/install/active-mod.txt' -Raw).Trim()
    $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if($receipt.schema -ne 1 -or $receipt.state -ne 'installed' -or [IO.Path]::GetFullPath($receipt.root) -ne [IO.Path]::GetFullPath($GamePath)){throw 'Active receipt does not match this installation.'}
    $relative='BepInEx/plugins/FM27Access/FM27Access.Plugin.dll'
    $entry=@($receipt.files | Where-Object {$_.relative.Replace('\','/') -eq $relative})
    if($entry.Count -ne 1){throw 'The active receipt does not own the reloadable Plugin.'}
    $target=Get-SafeTarget $GamePath $entry[0].relative
    if((Get-FileHash -LiteralPath $target).Hash -ne $entry[0].sha256){throw 'Installed Plugin was changed outside this receipt; preserved.'}
    Invoke-DotNet -Arguments @('build','FM27Access.Plugin','-c',$Configuration,"-p:FM27GameDir=$([IO.Path]::GetFullPath($GamePath))","-p:FM27DeveloperTools=$($DeveloperTools.IsPresent.ToString().ToLowerInvariant())",'--nologo')
    foreach($name in 'FM27Access.Host','FM27Access.Navigation','FM27Access.Speech'){
        $installed=Join-Path $GamePath "BepInEx/plugins/FM27Access/$name.dll"
        $built=Join-Path $ProjectRoot "FM27Access.Plugin/bin/$Configuration/net6.0/$name.dll"
        if((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $built).Hash){throw "$name changed; a cold installation is required."}
    }
    if(-not $GameClosed){
        $before=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 3
        try {
            Invoke-WebRequest 'http://127.0.0.1:8787/' -Method Head -SessionVariable webSession -TimeoutSec 5 | Out-Null
        } catch {
            if([int]$_.Exception.Response.StatusCode -ne 405){throw}
            # Older cores have no HEAD entry. Only this read-only compatibility
            # handshake needs to allow their complete cross-frame page compose.
            Invoke-WebRequest 'http://127.0.0.1:8787/' -SessionVariable webSession -TimeoutSec 25 | Out-Null
        }
    }
    $source=Join-Path $ProjectRoot "FM27Access.Plugin/bin/$Configuration/net6.0/FM27Access.Plugin.dll"
    $stream=[IO.File]::OpenRead($source)
    try{$pe=[System.Reflection.PortableExecutable.PEReader]::new($stream);$md=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe);$mvid=$md.GetGuid($md.GetModuleDefinition().Mvid).ToString()}finally{if($pe){$pe.Dispose()};$stream.Dispose()}
    $backup=Join-Path $ProjectRoot "artifacts/reload-backups/$($entry[0].sha256).dll"
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($backup)) | Out-Null
    if(-not(Test-Path -LiteralPath $backup)){Copy-Item -LiteralPath $target -Destination $backup}
    [IO.File]::Copy($source,$target+'.new',$false)
    [IO.File]::Move($target+'.new',$target,$true)
    $entry[0].sha256=(Get-FileHash -LiteralPath $source).Hash
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    if($GameClosed){
        & "$PSScriptRoot/clean-local-artifacts.ps1"
        Write-Output 'Plugin staged for the next launch; latest recovery backup and receipt preserved.'
        return
    }
    Invoke-WebRequest 'http://127.0.0.1:8787/reload' -Method Post -WebSession $webSession -TimeoutSec 5 | Out-Null
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do {
        try {
            $after=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2
            if($after.instance -ne $before.instance -and $after.coreMvid -eq $mvid -and $after.screen -eq 'Live'){
                & "$PSScriptRoot/clean-local-artifacts.ps1"
                Write-Output "Production Plugin reloaded; module $mvid."
                return
            }
        }catch{Write-Verbose $_.Exception.Message}
        Start-Sleep -Milliseconds 200
    }while([DateTime]::UtcNow -lt $deadline)
    throw 'Reload was not confirmed. Backup and receipt are preserved; inspect game logs.'
} finally { Pop-Location }
