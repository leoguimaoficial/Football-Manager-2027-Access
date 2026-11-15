#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path,
      [Parameter(Mandatory)][string]$ExpectedCommit,
      [Parameter(Mandatory)][string]$ExpectedRunId,
      [Parameter(Mandatory)][string]$ExpectedVersion)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Path)
if($ExpectedCommit -notmatch '^[0-9a-f]{40}$' -or $ExpectedVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$'){throw 'Invalid expected identity.'}
$info=Get-Content -LiteralPath "$root/build-info.json" -Raw | ConvertFrom-Json
if($info.schema -ne 1 -or $info.commit -ne $ExpectedCommit -or $info.runId -ne $ExpectedRunId -or $info.version -ne $ExpectedVersion -or
   $info.configuration -ne 'Release' -or $info.developerTools -ne $false -or ($ExpectedRunId -ne 'local' -and $info.sourceDirty -ne $false)){throw 'Build provenance does not match selected run/commit/version.'}
$required=@('FM27Access-Installer.exe','FM27Access-Installer.json',"FM27Access-v$ExpectedVersion.zip", "FM27Access-v$ExpectedVersion-source.zip",'FM27Access-UserGuide.html','FM27Access-Manifest.json','build-info.json')
$sums=@{}
foreach($line in Get-Content -LiteralPath "$root/SHA256SUMS.txt"){
    if($line -notmatch '^([0-9a-f]{64})  ([A-Za-z0-9._+-]+)$'){throw 'Malformed checksum file.'}
    $name=$Matches[2];$hash=$Matches[1]
    if($sums.ContainsKey($name) -or $name -notin $required){throw 'Duplicate/unexpected asset checksum.'}
    $sums[$name]=$hash
    if(!(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf) -or (Get-FileHash -LiteralPath (Join-Path $root $name)).Hash -ne $hash){throw "Asset checksum mismatch: $name"}
}
if($sums.Count -ne $required.Count -or @(Get-ChildItem -LiteralPath $root -Recurse -File).Count -ne $required.Count+1){throw 'Incomplete or extra distribution assets.'}
$m=Get-Content -LiteralPath "$root/FM27Access-Manifest.json" -Raw | ConvertFrom-Json
if($m.schema -ne 1 -or $m.version -ne $ExpectedVersion -or $m.package -ne "FM27Access-v$ExpectedVersion.zip" -or
   $m.packageSha256 -ne $sums[$m.package] -or $m.packageSize -ne (Get-Item -LiteralPath (Join-Path $root $m.package)).Length -or
   $m.bepInExVersion -ne '6.0.0-be.793+dbc82f0' -or $m.files.Count -eq 0){throw 'Invalid distribution manifest.'}
$expected=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($file in $m.files){
    $p=$file.path
    if($p -match '(^|/)(\.|\.\.)(/|$)|[\\:]|^/|[. ]$|(^|/)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|/|$)' -or $file.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
       $file.size -lt 0 -or $file.size -gt 1GB -or $expected.ContainsKey($p)){throw 'Unsafe/duplicate file manifest.'}
    if($file.kind -eq 'mod'){
        if(!$p.StartsWith('BepInEx/plugins/FM27Access/',[StringComparison]::Ordinal) -or $p -match '\.(pdb|mdb|cfg)$|/(mcs\.dll|bookmarks\.json)$'){throw 'Protected or nonproduction mod payload.'}
    } elseif($file.kind -eq 'config') {if($p -ne 'BepInEx/config/BepInEx.cfg'){throw 'Unexpected configuration payload.'}}
    elseif($file.kind -ne 'loader' -or $p -notmatch '^(winhttp\.dll|doorstop_config\.ini|\.doorstop_version|BepInEx/core/.+|dotnet/.+)$'){throw 'Unexpected loader payload.'}
    $expected.Add($p,$file)
}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root $m.package))
try {
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $zip.Entries){
        if($entry.FullName.EndsWith('/')){continue}
        if(!$seen.Add($entry.FullName) -or !$expected.ContainsKey($entry.FullName) -or $entry.Length -ne $expected[$entry.FullName].size -or
            (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000){throw 'Unlisted/duplicate/linked ZIP entry.'}
        $stream=$entry.Open()
        try {$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
        if($hash -ne $expected[$entry.FullName].sha256){throw 'ZIP file integrity failure.'}
        if($entry.FullName -eq 'BepInEx/config/BepInEx.cfg'){
            $reader=[IO.StreamReader]::new($entry.Open())
            try {$configuration=$reader.ReadToEnd()}finally{$reader.Dispose()}
            Import-Module "$PSScriptRoot/../packaging/player/LoggingProfile.psm1" -Force
            if((Get-ProductionLoggingText $configuration) -cne $configuration){throw 'Manual ZIP does not have the production console-off logging profile.'}
        }
    }
    if($seen.Count -ne $expected.Count){throw 'Missing ZIP payload files.'}
}finally{$zip.Dispose()}
foreach($name in 'FM27Access.Host.dll','FM27Access.Plugin.dll','FM27Access.Navigation.dll','FM27Access.Speech.dll','native-build.json','required-api.json','LICENSE','THIRD-PARTY-LICENSES.txt','companion/FM27AccessCompanion.exe','prism/native/win-x64/prism.dll'){
    if(!$expected.ContainsKey('BepInEx/plugins/FM27Access/'+$name)){throw "Incomplete mod: $name"}
}
foreach($name in 'winhttp.dll','doorstop_config.ini','.doorstop_version','BepInEx/core/BepInEx.Unity.IL2CPP.dll','BepInEx/config/BepInEx.cfg'){if(!$expected.ContainsKey($name)){throw "Missing loader: $name"}}
$installer=Get-Content -LiteralPath "$root/FM27Access-Installer.json" -Raw | ConvertFrom-Json
if($installer.schema -ne 1 -or $installer.version -ne $info.installerVersion -or $installer.sha256 -ne $sums['FM27Access-Installer.exe'] -or
   $installer.size -ne (Get-Item -LiteralPath "$root/FM27Access-Installer.exe").Length -or
   $installer.url -ne "https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/download/v$ExpectedVersion/FM27Access-Installer.exe"){throw 'Invalid installer metadata.'}
$source=[IO.Compression.ZipFile]::OpenRead("$root/FM27Access-v$ExpectedVersion-source.zip")
try {
    $sourceInfo=$source.GetEntry('SOURCE-INFO.txt')
    if(!$sourceInfo){throw 'Source archive has no provenance.'}
    $reader=[IO.StreamReader]::new($sourceInfo.Open())
    try {$sourceText=$reader.ReadToEnd()}finally{$reader.Dispose()}
    if($sourceText -notmatch "(?m)^Commit: $ExpectedCommit`r?$" -or $sourceText -notmatch "(?m)^Version: $([regex]::Escape($ExpectedVersion))`r?$" -or
       ($ExpectedRunId -ne 'local' -and $sourceText -notmatch '(?m)^WorkingTreeDirty: false\r?$')){throw 'Source archive differs from the selected build.'}
    foreach($entry in $source.Entries){
        $n=$entry.FullName.Replace('\','/')
        if($n -match '(^|/)(\.git|\.tools|\.upstream|artifacts|obj|node_modules|BepInEx|dotnet|Decompiled)/|(^|/)\.env($|\.)|\.(exe|pdb|mdb|zip|7z|fm|fmf|log|tmp|bak|dmp|pfx|p12|pem|key)$' -or
            ($n -match '\.dll$' -and $n -ne 'Prism/dynamic/release/bin/prism.dll')){throw 'Private/generated/binary content in source archive.'}
    }
}finally{$source.Dispose()}
Write-Output "PASS: selected build $ExpectedRunId, commit $ExpectedCommit, version $ExpectedVersion; all assets and managed payload verified."
