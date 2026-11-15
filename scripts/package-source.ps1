. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/PackageRetention.ps1"
Push-Location $ProjectRoot
try {
    $out=Join-Path $ProjectRoot ('artifacts/packages/source-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force $out | Out-Null
    # Allowlist: no BepInEx, game installation, interop, .upstream, artifacts or build outputs.
    $folders='.agents','FM27Access.CompanionApp','FM27Access.Host','FM27Access.Navigation','FM27Access.Plugin','FM27Access.Sounds','FM27Access.Speech','FM27Access.Tests','docs','scripts','tests','tools','lib','packaging','Prism'
    foreach ($folder in $folders) {
        # Share Git's exclusion policy, including local credentials/configuration.
        $files=@(& git -C $ProjectRoot ls-files --cached --others --exclude-standard -- $folder)
        if($LASTEXITCODE -ne 0){throw 'Cannot enumerate publication files through Git.'}
        foreach($relative in $files) {
            $target=Join-Path $out $relative
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
            Copy-Item -LiteralPath (Join-Path $ProjectRoot $relative) -Destination $target
        }
    }
    foreach ($name in 'AGENTS.md','BUILDING.md','CHANGELOG.md','CONTRIBUTING.md','CREDITS.md','INSTALL.md','README.md','Directory.Build.props','Directory.Solution.props','FM27Access.sln','global.json','LICENSE','.gitignore') {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot $name) -Destination $out
    }
    # Compress-Archive omits hidden files, including the publication .gitignore.
    [IO.Compression.ZipFile]::CreateFromDirectory($out,"$out.zip")
    $archive=[IO.Compression.ZipFile]::OpenRead("$out.zip")
    try {
        $names=@($archive.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
        foreach($required in '.gitignore','LICENSE','CREDITS.md','FM27Access.sln','global.json','Prism/NOTICE','Prism/dynamic/release/bin/prism.dll'){
            if($required -notin $names){throw "Required publication file missing: $required"}
        }
        foreach($name in $names){
            if($name -match '(^|/)(\.git|\.upstream|\.tools|artifacts|obj|node_modules|TestResults|BepInEx|dotnet|Decompiled)/' -or
               ($name -match '(^|/)bin/' -and $name -ne 'Prism/dynamic/release/bin/prism.dll') -or
               $name -match '(^|/)\.env($|\.)|\.(exe|pdb|mdb|zip|7z|fm|fmf|log|tmp|bak|dmp|pfx|p12)$' -or
               ($name.EndsWith('.dll',[StringComparison]::OrdinalIgnoreCase) -and $name -ne 'Prism/dynamic/release/bin/prism.dll')){
                throw "Private/generated or unapproved binary in source ZIP: $name"
            }
        }
        Write-Output "PASS: source ZIP retains publication policy/licenses/Prism and excludes private/generated/game files ($($names.Count) entries)."
    } finally {$archive.Dispose()}
    Remove-SupersededPackages -PackageRoot ([IO.Path]::GetDirectoryName($out)) -Kind source -Keep $out
    Write-Output "Experimental source bundle: $out.zip"
} finally { Pop-Location }
