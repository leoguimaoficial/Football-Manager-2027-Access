# Generated workspace files only. Never selects saves, installed game files,
# source/reference trees, Git data, licenses or the pinned SDK.
function Get-WorkspaceCleanupTargets {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    if(-not(Test-Path -LiteralPath (Join-Path $rootPath 'FM27Access.sln')) -or
       -not(Test-Path -LiteralPath (Join-Path $rootPath 'global.json'))){throw 'Expected the FM27 source workspace.'}
    $relative=@(
        'tests/companion/node_modules','Prism/static','Prism/dynamic/debug',
        'Prism/dynamic/release/lib','.upstream/releases'
    )
    foreach($project in 'FM27Access.CompanionApp','FM27Access.Host','FM27Access.Navigation',
        'FM27Access.Plugin','FM27Access.Speech','FM27Access.Tests','tools/game-inspect'){
        $relative+= "$project/bin","$project/obj"
    }
    $toolsRoot=Join-Path $rootPath '.tools'
    if(Test-Path -LiteralPath $toolsRoot){
        $knownCaches='diagnostics','bepinex-793','dbgeng','ilspy','web-tests','symsrv',
            'bepinex-793.zip','dbgeng.zip','symsrv.zip','dotnet-install.ps1'
        $relative+=@(Get-ChildItem -LiteralPath $toolsRoot -Force | Where-Object Name -In $knownCaches |
            ForEach-Object {[IO.Path]::GetRelativePath($rootPath,$_.FullName)})
    }
    # The pinned SDK is 10.0.403. Version8 was used by retired diagnostic tools.
    $sdk=Get-Content -LiteralPath (Join-Path $rootPath 'global.json') -Raw | ConvertFrom-Json
    if($sdk.sdk.version -ne '10.0.403'){throw 'Review obsolete SDK cleanup for the new pinned version.'}
    foreach($parent in '.tools/dotnet/sdk','.tools/dotnet/sdk-manifests',
        '.tools/dotnet/shared/Microsoft.NETCore.App','.tools/dotnet/shared/Microsoft.AspNetCore.App',
        '.tools/dotnet/shared/Microsoft.WindowsDesktop.App','.tools/dotnet/templates'){
        $directory=Join-Path $rootPath $parent
        if(Test-Path -LiteralPath $directory){$relative+=@(Get-ChildItem -LiteralPath $directory -Directory |
            Where-Object Name -Match '^8\.' | ForEach-Object {[IO.Path]::GetRelativePath($rootPath,$_.FullName)})}
    }
    $packs=Join-Path $rootPath '.tools/dotnet/packs'
    if(Test-Path -LiteralPath $packs){
        foreach($pack in Get-ChildItem -LiteralPath $packs -Directory){
            $relative+=@(Get-ChildItem -LiteralPath $pack.FullName -Directory | Where-Object Name -Match '^8\.' |
                ForEach-Object {[IO.Path]::GetRelativePath($rootPath,$_.FullName)})
        }
    }
    $artifacts=Join-Path $rootPath 'artifacts'
    if(Test-Path -LiteralPath $artifacts){
        $keep='install','current-verification.json','workspace-cleanup-plan.json','workspace-cleanup-latest.json'
        $relative+=@(Get-ChildItem -LiteralPath $artifacts -Force | Where-Object Name -NotIn $keep |
            ForEach-Object {[IO.Path]::GetRelativePath($rootPath,$_.FullName)})
    }
    # Refuse generated directories that an ownership receipt still references.
    $receiptStrings=@()
    $receiptRoot=Join-Path $artifacts 'install'
    if(Test-Path -LiteralPath $receiptRoot){
        foreach($receipt in Get-ChildItem -LiteralPath $receiptRoot -File -Filter '*.json'){
            $data=Get-Content -LiteralPath $receipt.FullName -Raw | ConvertFrom-Json
            foreach($entry in $data.files){foreach($property in 'source','original','backup'){
                if($entry.$property -is [string] -and [IO.Path]::IsPathRooted($entry.$property)){
                    $receiptStrings+=[IO.Path]::GetFullPath($entry.$property)
                }
            }}
        }
    }
    $targets=@()
    foreach($item in $relative | Select-Object -Unique){
        $candidate=[IO.Path]::GetFullPath((Join-Path $rootPath $item))
        if(-not $candidate.StartsWith($rootPath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup escaped the source workspace.'}
        if(-not(Test-Path -LiteralPath $candidate)){continue}
        foreach($reference in $receiptStrings){
            if($reference -eq $candidate -or $reference.StartsWith($candidate+'\',[StringComparison]::OrdinalIgnoreCase)){
                throw "Ownership receipt still references generated output: $candidate"
            }
        }
        $targets+=$candidate
    }
    return $targets
}

function Remove-WorkspaceCleanupTargets {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Targets)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    $allowed=@(Get-WorkspaceCleanupTargets -Root $rootPath)
    $validated=@()
    foreach($target in $Targets){
        $absolute=[IO.Path]::GetFullPath($target)
        if(-not $absolute.StartsWith($rootPath+'\',[StringComparison]::OrdinalIgnoreCase) -or $absolute -notin $allowed){
            throw "Target is not a recognized generated workspace output: $absolute"
        }
        # Check every ancestor before enumerating, then every descendant before
        # deleting anything. A generated bin/obj name never authorizes a junction.
        $current=Get-Item -LiteralPath $absolute -Force
        while($current -and $current.FullName.StartsWith($rootPath,[StringComparison]::OrdinalIgnoreCase)){
            if($current.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked cleanup target preserved: $absolute"}
            if($current.FullName -eq $rootPath){break}
            $current=Get-Item -LiteralPath ([IO.Path]::GetDirectoryName($current.FullName)) -Force
        }
        $entry=Get-Item -LiteralPath $absolute -Force
        $files=if($entry.PSIsContainer){@(Get-ChildItem -LiteralPath $absolute -Recurse -Force)}else{@($entry)}
        if($files | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw "Linked descendant preserved: $absolute"}
        $bytes=($files | Where-Object {-not $_.PSIsContainer} | Measure-Object Length -Sum).Sum
        $validated+= [pscustomobject]@{path=$absolute;bytes=[long]$bytes}
    }
    $removed=@()
    foreach($entry in $validated){
        if($PSCmdlet.ShouldProcess($entry.path,'Remove verified generated workspace output')){
            Remove-Item -LiteralPath $entry.path -Recurse -Force -ErrorAction Stop
            $removed+=$entry
        }
    }
    return $removed
}
