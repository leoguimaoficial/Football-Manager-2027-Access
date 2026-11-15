# Only generated, explicitly named package outputs are eligible for removal.
function Remove-SupersededPackages {
    param([Parameter(Mandatory)][string]$PackageRoot,
          [Parameter(Mandatory)][ValidateSet('source','player')][string]$Kind,
          [Parameter(Mandatory)][string]$Keep)
    $root=[IO.Path]::GetFullPath($PackageRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $keepPath=[IO.Path]::GetFullPath($Keep)
    if([IO.Path]::GetDirectoryName($keepPath) -ne $root){throw 'Current package must be a direct child of the package root.'}
    if(-not(Test-Path -LiteralPath $keepPath -PathType Container) -or
       -not(Test-Path -LiteralPath ($keepPath+'.zip') -PathType Leaf)){throw 'Preserve old packages until both new outputs exist.'}
    $pattern=if($Kind -eq 'source'){'^source-\d{8}-\d{6}(?:\.zip)?$'}else{'^FM27Access-[0-9][0-9A-Za-z.+-]*-candidate-\d{8}-\d{6}(?:\.zip)?$'}
    if([IO.Path]::GetFileName($keepPath) -notmatch $pattern){throw 'Unrecognized current package name.'}
    $targets=@(Get-ChildItem -LiteralPath $root -Force | Where-Object {
        $_.Name -match $pattern -and $_.FullName -ne $keepPath -and $_.FullName -ne ($keepPath+'.zip')
    })
    # Validate every absolute target and its descendants before any recursive deletion.
    foreach($target in $targets){
        if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target.FullName)) -ne $root){throw 'Package removal escaped its root.'}
        $entries=@($target)
        if($target.PSIsContainer){$entries+=@(Get-ChildItem -LiteralPath $target.FullName -Recurse -Force)}
        if($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }){throw "Linked package preserved: $($target.Name)"}
    }
    foreach($target in $targets){Remove-Item -LiteralPath $target.FullName -Recurse -Force}
    Write-Output "Removed $($targets.Count) superseded $Kind package outputs; retained $([IO.Path]::GetFileName($keepPath)) and its ZIP."
}
