function New-ModPayload {
    param([Parameter(Mandatory)][string]$Destination,
          [Parameter(Mandatory)]$NativeReport,
          [ValidateSet('Debug','Release')][string]$Configuration='Release',
          [switch]$DeveloperTools,[switch]$ReferenceBuild)
    if($DeveloperTools -and $Configuration -ne 'Debug'){throw 'Developer payload requires explicit Debug configuration.'}
    if(Test-Path -LiteralPath $Destination){throw 'Payload destination already exists; preserved.'}
    $plugin=Join-Path $Destination 'BepInEx/plugins/FM27Access'
    New-Item -ItemType Directory -Force "$plugin/companion","$plugin/prism/native/win-x64" | Out-Null
    foreach($name in 'FM27Access.Host','FM27Access.Plugin','FM27Access.Navigation','FM27Access.Speech'){
        Copy-Item -LiteralPath (Join-Path $ProjectRoot "FM27Access.Plugin/bin/$Configuration/net6.0/$name.dll") -Destination $plugin
    }
    $loader=@('winhttp.dll')+@(Get-ChildItem -LiteralPath (Join-Path $NativeReport.Path 'BepInEx/core') -File -Filter '*.dll' | ForEach-Object {[IO.Path]::GetRelativePath($NativeReport.Path,$_.FullName)})
    @{Schema=2;Version=$NativeReport.GameVersion;Assembly=$NativeReport.AssemblyHash;Metadata=$NativeReport.MetadataHash;
        UnityFamily='6000.3.';MetadataVersion=39;ApiFile='required-api.json';
        LoaderFiles=@($loader | ForEach-Object {@{Relative=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $NativeReport.Path $_)).Hash}})} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$plugin/native-build.json" -Encoding utf8
    $contractCommand=if($ReferenceBuild){'api-contract-from-references'}else{'api-contract'}
    Invoke-DotNet -Arguments @((Join-Path $ProjectRoot 'tools/game-inspect/bin/Release/net10.0/game-inspect.dll'),$contractCommand,$plugin,$NativeReport.Path,"$plugin/required-api.json") | Out-Host
    if($DeveloperTools){
        Copy-Item -LiteralPath (Join-Path $ProjectRoot 'lib/mcs.dll') -Destination $plugin
        Copy-Item -LiteralPath (Join-Path $ProjectRoot 'lib/mcs-LICENSE.txt') -Destination $plugin
    }
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'Prism/dynamic/release/bin/prism.dll') -Destination "$plugin/prism/native/win-x64/prism.dll"
    Invoke-DotNet -Arguments @('publish',(Join-Path $ProjectRoot 'FM27Access.CompanionApp'),'-c',$Configuration,"-p:FM27DeveloperTools=$($DeveloperTools.IsPresent.ToString().ToLowerInvariant())",'-o',"$plugin/companion",'--nologo') | Out-Host
    foreach($file in 'LICENSE','CREDITS.md'){Copy-Item -LiteralPath (Join-Path $ProjectRoot $file) -Destination $plugin}
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'packaging/THIRD-PARTY-LICENSES.txt') -Destination $plugin
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'packaging/licenses') -Destination "$plugin/licenses" -Recurse
    $project=[xml](Get-Content -LiteralPath (Join-Path $ProjectRoot 'FM27Access.CompanionApp/FM27Access.CompanionApp.csproj') -Raw)
    $framework=$project.SelectSingleNode('//TargetFramework').InnerText
    $runtime=$project.SelectSingleNode('//RuntimeFrameworkVersion').InnerText
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "FM27Access.CompanionApp/bin/$Configuration/$framework/win-x64/FM27AccessCompanion.runtimeconfig.json") -Destination "$plugin/companion/runtime-profile.json"
    $assets=Get-Content -LiteralPath (Join-Path $ProjectRoot 'FM27Access.CompanionApp/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach($runtimePack in 'microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64'){
        $source=@($assets.packageFolders.Keys | ForEach-Object {Join-Path $_ "$runtimePack/$runtime"} | Where-Object {Test-Path -LiteralPath $_ -PathType Container}) | Select-Object -First 1
        if(!$source){throw "Resolved runtime license directory missing: $runtimePack/$runtime"}
        $notices=@(Get-ChildItem -LiteralPath $source -File | Where-Object {$_.Name -in 'LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT'})
        if(!$notices.Count){throw "Runtime license missing: $runtimePack/$runtime"}
        $destination=Join-Path "$plugin/licenses" "$runtimePack-$runtime"
        New-Item -ItemType Directory -Path $destination | Out-Null
        $notices | Copy-Item -Destination $destination
    }
}
