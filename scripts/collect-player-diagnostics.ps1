#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GamePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputPath){throw 'Diagnostics destination already exists; preserved.'}
New-Item -ItemType Directory -Path $OutputPath | Out-Null
$files=@('BepInEx/LogOutput.log','BepInEx/ErrorLog.log','BepInEx/plugins/FM27Access/server.log','BepInEx/plugins/FM27Access/server.log.bak')
$sources=@($files | ForEach-Object {Join-Path $GamePath $_})
$sources+=@(Join-Path $env:LOCALAPPDATA 'FM27Access/companion.log'),@(Join-Path $env:LOCALAPPDATA 'FM27Access/companion.log.bak')
foreach($source in $sources){
    if(!(Test-Path -LiteralPath $source -PathType Leaf)){continue}
    # Bounded tail only. Do not read a large game's full history into memory.
    $stream=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        $offset=[Math]::Max(0,$stream.Length-1MB)
        $null=$stream.Seek($offset,[IO.SeekOrigin]::Begin)
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
        try {if($offset -gt 0){$null=$reader.ReadLine()};$text=$reader.ReadToEnd()}
        finally {$reader.Dispose()}
    } finally {$stream.Dispose()}
    $text=[regex]::Replace($text,'(?im)(Cookie|Set-Cookie|Authorization)\s*:[^\r\n]*','$1: <redacted>')
    $text=[regex]::Replace($text,'(?i)(fm27session\s*=\s*)[^\s;"''<>]+','$1<redacted>')
    $text=[regex]::Replace($text,'(?i)C:\\Users\\[^\\\r\n]+','C:\Users\<user>')
    [IO.File]::WriteAllText((Join-Path $OutputPath ([IO.Path]::GetFileName($source))),$text)
}
$fingerprints=@()
foreach($relative in 'GameAssembly.dll','fm_Data/il2cpp_data/Metadata/global-metadata.dat','BepInEx/core/BepInEx.Unity.IL2CPP.dll'){
    $path=Join-Path $GamePath $relative
    $fingerprints+=@{file=$relative;present=(Test-Path -LiteralPath $path -PathType Leaf);sha256=if(Test-Path -LiteralPath $path -PathType Leaf){(Get-FileHash -LiteralPath $path).Hash}else{$null}}
}
@{createdUtc=[DateTime]::UtcNow.ToString('o');fingerprints=$fingerprints;scope='Local bounded framework/mod log tails only; no Player.log, saves, DOM or speech exports.'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputPath 'diagnostics.json') -Encoding utf8
Write-Output "Local diagnostics: $OutputPath. Review before sharing; nothing was uploaded."
