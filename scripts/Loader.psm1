Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-GameClosed([string]$GamePath) {
    $root = [IO.Path]::GetFullPath($GamePath).TrimEnd('\') + '\'
    $packageRoot = $null
    $config = Join-Path $GamePath 'MicrosoftGame.config'
    if (Test-Path -LiteralPath $config) {
        [xml]$identity = Get-Content -LiteralPath $config
        $package = Get-AppxPackage -Name $identity.Game.Identity.Name -ErrorAction SilentlyContinue
        if ($package) { $packageRoot = $package.InstallLocation.TrimEnd('\') + '\' }
    }
    foreach ($process in Get-Process -Name fm,gamelaunchhelper -ErrorAction SilentlyContinue) {
        if (-not $process.Path -or $process.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
            ($packageRoot -and $process.Path.StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase))) {
            throw 'Close Football Manager before installing or removing files.'
        }
    }
}

function Get-SafeTarget([string]$Root, [string]$Relative) {
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath([IO.Path]::Combine($prefix, $Relative))
    if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped the installation root.' }
    return $target
}

function Copy-LoaderRuntimePayload([string]$Source, [string]$Destination,
    [ValidateSet('winhttp.dll','version.dll')][string]$ProxyName='version.dll') {
    $sourceRoot=[IO.Path]::GetFullPath($Source).TrimEnd('\')+'\'
    $destinationRoot=[IO.Path]::GetFullPath($Destination).TrimEnd('\')+'\'
    if($destinationRoot.StartsWith($sourceRoot,[StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($destinationRoot,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Loader staging must be separate from its source.'
    }
    if(Test-Path -LiteralPath $Destination){throw 'Loader staging destination already exists; preserved.'}
    $files=@(Get-ChildItem -LiteralPath $Source -Recurse -File)
    if((Test-Path -LiteralPath (Join-Path $Source 'winhttp.dll')) -and (Test-Path -LiteralPath (Join-Path $Source 'version.dll'))){throw 'Loader source contains conflicting proxy names.'}
    $copied=0;$excluded=0
    foreach($file in $files){
        $relative=[IO.Path]::GetRelativePath($sourceRoot,$file.FullName)
        $normalized=$relative.Replace('\','/')
        # Keep the pinned Doorstop bytes; install just the store's supported name.
        if($normalized -in 'winhttp.dll','version.dll'){$relative=$ProxyName}
        # Loader bundles used for development can retain example/diagnostic
        # mods. Plugins and patchers have independent ownership/deployment;
        # restoring the loader must never silently reinstall either subtree.
        if($normalized.StartsWith('BepInEx/plugins/',[StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith('BepInEx/patchers/',[StringComparison]::OrdinalIgnoreCase)){
            $excluded++;continue
        }
        $target=Get-SafeTarget $Destination $relative
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::Copy($file.FullName,$target,$false)
        $copied++
    }
    if($copied -eq 0){throw 'Loader source contains no runtime files.'}
    return [pscustomobject]@{Path=[IO.Path]::GetFullPath($Destination);Copied=$copied;ExcludedMods=$excluded}
}

function Install-Loader([string]$GamePath, [string]$Payload, [string]$ReceiptPath) {
    Assert-GameClosed $GamePath
    if (Test-Path -LiteralPath $ReceiptPath) { throw 'An installation receipt already exists; remove its owned files before reinstalling.' }
    if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'fm.exe'))) { throw 'fm.exe not found.' }
    $files = @(Get-ChildItem -LiteralPath $Payload -Recurse -File)
    if ($files.Count -eq 0) { throw 'Empty payload.' }
    if((Test-Path -LiteralPath (Join-Path $Payload 'winhttp.dll')) -and (Test-Path -LiteralPath (Join-Path $Payload 'version.dll'))){throw 'Payload contains conflicting Doorstop proxies.'}
    foreach($proxy in 'winhttp.dll','version.dll'){
        $other=if($proxy -eq 'winhttp.dll'){'version.dll'}else{'winhttp.dll'}
        if((Test-Path -LiteralPath (Join-Path $Payload $proxy)) -and (Test-Path -LiteralPath (Join-Path $GamePath $other))){throw "Conflicting loader proxy preserved: $other. Remove its owned installation through its receipt first."}
    }
    $entries = @($files | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($Payload), $_.FullName)
        $target = Get-SafeTarget $GamePath $relative
        if (Test-Path -LiteralPath $target) { throw "Collision: $relative. Existing files will not be overwritten." }
        @{ relative=$relative; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; source=$_.FullName }
    })
    $receipt = @{ schema=1; root=[IO.Path]::GetFullPath($GamePath); createdUtc=[DateTime]::UtcNow.ToString('o'); state='installing'; files=@() }
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReceiptPath))) | Out-Null
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    foreach ($entry in $entries) {
        $target = Get-SafeTarget $GamePath $entry.relative
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        # Record intent before copying so interrupted installations remain removable.
        $receipt.files += @{relative=$entry.relative; sha256=$entry.sha256}
        $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
        [IO.File]::Copy($entry.source, $target, $false)
    }
    $receipt.state = 'installed'
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    Write-Output "Installation recorded; receipt: $ReceiptPath"
}

function Uninstall-Loader([string]$ReceiptPath) {
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
    if ($receipt.schema -ne 1) { throw 'Unknown receipt version.' }
    if ($receipt.state -eq 'removed') { throw 'This receipt was already removed; use the current installation receipt.' }
    Assert-GameClosed $receipt.root
    # Validate the entire operation before removing anything.
    $targets = @($receipt.files | ForEach-Object {
        $target = Get-SafeTarget $receipt.root $_.relative
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $_.sha256) { throw "Modified file preserved: $target" }
            $target
        }
    })
    foreach ($target in $targets) { Remove-Item -LiteralPath $target }
    $receipt.state = 'removed'
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    Write-Output 'Owned files removed; generated files and directories preserved.'
}
function Get-LoaderProxy([string]$GamePath) {
    $found=@('version.dll','winhttp.dll' | Where-Object {Test-Path -LiteralPath (Join-Path $GamePath $_) -PathType Leaf})
    if($found.Count -ne 1){throw 'Expected exactly one supported Doorstop proxy (version.dll or legacy winhttp.dll).'}
    return $found[0]
}
Export-ModuleMember -Function Install-Loader,Uninstall-Loader,Assert-GameClosed,Get-SafeTarget,Copy-LoaderRuntimePayload,Get-LoaderProxy
