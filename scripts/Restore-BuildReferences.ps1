#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputPath,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$index=Get-Content -LiteralPath (Join-Path $InputPath 'reference-index.json') -Raw|ConvertFrom-Json
if($index.schema -ne 1 -or $index.file -ne 'build-references.zip' -or $index.sha256 -notmatch '^[0-9A-Fa-f]{64}$'){throw 'Invalid private input index.'}
$archive=Join-Path $InputPath $index.file
if((Get-FileHash -LiteralPath $archive).Hash -ne $index.sha256){throw 'Private reference archive changed.'}
if(Test-Path -LiteralPath $Destination){throw 'Reference extraction destination exists.'}
$root=[IO.Path]::GetFullPath($Destination).TrimEnd('\')+'\'
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$size=0
    foreach($entry in $zip.Entries){
        $name=$entry.FullName
        if($name -ne 'build-references.json' -and $name -notmatch '^BepInEx/interop/[A-Za-z0-9_][A-Za-z0-9_. -]*\.dll$'){throw 'Unlisted private input type.'}
        if($name.Contains('..') -or !$seen.Add($name) -or (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000){throw 'Unsafe private input entry.'}
        $size+=$entry.Length
        if($seen.Count -gt 513 -or $size -gt 2GB){throw 'Private input size limit exceeded.'}
        $target=[IO.Path]::GetFullPath((Join-Path $root $name))
        if(!$target.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Private input escaped root.'}
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target))|Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
    }
}finally{$zip.Dispose()}
Write-Output 'Private compiler input restored; no game executable, saves or generated interop will be included in player/source artifacts.'
