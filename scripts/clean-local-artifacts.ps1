[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts'))
$prefix=$artifactRoot.TrimEnd('\')+'\'
$receiptPath=(Get-Content -LiteralPath (Join-Path $artifactRoot 'install/active-mod.txt') -Raw).Trim()
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if($receipt.state -ne 'installed'){throw 'An active installed receipt is required before removing staging copies.'}
$receiptName=[IO.Path]::GetFileNameWithoutExtension($receiptPath)
if($receiptName -notmatch '^mod-([0-9a-f]{32})$'){throw 'Active development receipt identity is required.'}
$activePayload=Join-Path $artifactRoot ('payload-'+$Matches[1])
$sources=@($receipt.files | Where-Object {$_.source} | ForEach-Object {[IO.Path]::GetFullPath($_.source)})
$targets=@(Get-ChildItem -LiteralPath $artifactRoot -Directory | Where-Object {
    $_.Name -match '^(?:payload-|dev-payload-)[0-9a-f]{32}$' -or $_.Name -in 'initial-payload','native-cache-baseline-debug-payload'
} | Where-Object {
    $candidatePrefix=$_.FullName.TrimEnd('\')+'\'
    $_.FullName -ne $activePayload -and -not @($sources | Where-Object {$_.StartsWith($candidatePrefix,[StringComparison]::OrdinalIgnoreCase)}).Count
})
$backups=Join-Path $artifactRoot 'reload-backups'
if(Test-Path -LiteralPath $backups){
    $targets+=@(Get-ChildItem -LiteralPath $backups -File | Where-Object Name -Match '^[0-9a-fA-F]{64}\.dll$' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip 1)
}
foreach($target in $targets){
    $resolved=[IO.Path]::GetFullPath($target.FullName)
    if(-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target escaped artifacts.'}
    $entries=@($target)
    if($target.PSIsContainer){$entries+=@(Get-ChildItem -LiteralPath $resolved -Recurse -Force)}
    if($entries | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw "Linked artifact preserved: $resolved"}
}
$bytes=0L
foreach($target in $targets){
    $files=if($target.PSIsContainer){@(Get-ChildItem -LiteralPath $target.FullName -Recurse -File)}else{@($target)}
    $bytes+=($files | Measure-Object Length -Sum).Sum
    Remove-Item -LiteralPath $target.FullName -Recurse -Force
}
$report=[ordered]@{at=[DateTime]::UtcNow.ToString('o');removed=@($targets.FullName);bytesRemoved=$bytes;activeReceipt=$receiptPath;preserved='Installed mod, active staging sources, latest reload backup, receipts, diagnostics, dependencies and all saves'}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactRoot 'local-cleanup-latest.json') -Encoding utf8
Write-Output "Removed $($targets.Count) obsolete staging/backup outputs ($([Math]::Round($bytes/1GB,2)) GiB). Active files and saves preserved."
