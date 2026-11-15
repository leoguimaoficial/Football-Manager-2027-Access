Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Get-ProductionLoggingText([string]$Text) {
    # Preserve comments and unrelated sections/keys. Refuse ambiguous duplicate settings.
    foreach($setting in @(
        @('Logging.Console','Enabled','false'),
        @('Logging.Disk','Enabled','true'),
        @('Logging.Disk','LogLevels','Fatal, Error, Warning'))){
        $section=[regex]::Escape($setting[0]);$key=[regex]::Escape($setting[1]);$value=$setting[2]
        $matches=[regex]::Matches($Text,"(?ms)^\[$section\][^\r\n]*(?:\r?\n|$).*?(?=^\[|\z)")
        if($matches.Count -gt 1){throw "Duplicate BepInEx section: $($setting[0])"}
        if($matches.Count -eq 0){$Text+="`r`n[$($setting[0])]`r`n$($setting[1]) = $value`r`n";continue}
        $block=$matches[0].Value
        $keys=[regex]::Matches($block,"(?m)^$key[^\S\r\n]*=[^\r\n]*")
        if($keys.Count -gt 1){throw "Duplicate BepInEx setting: $($setting[0]).$($setting[1])"}
        if($keys.Count -eq 1){$replacement=$block.Remove($keys[0].Index,$keys[0].Length).Insert($keys[0].Index,"$($setting[1]) = $value")}
        else{$replacement=$block.TrimEnd()+"`r`n$($setting[1]) = $value`r`n"}
        $Text=$Text.Remove($matches[0].Index,$block.Length).Insert($matches[0].Index,$replacement)
    }
    return $Text
}

function Set-ProductionLogging([string]$GamePath,[string]$ReceiptPath) {
    if(Test-Path -LiteralPath $ReceiptPath){throw 'Logging receipt already exists; preserved.'}
    $path=Join-Path $GamePath 'BepInEx/config/BepInEx.cfg'
    $existed=Test-Path -LiteralPath $path
    $original=[byte[]]@()
    if($existed){$original=[IO.File]::ReadAllBytes($path)}
    $text=if($existed){[IO.File]::ReadAllText($path)}else{''}
    $updated=Get-ProductionLoggingText $text
    # Preserve native line endings, including LF inside multiline comments and
    # CRLF around settings. Removing a setting's CR makes ConfigFile rewrite it
    # on first launch and invalidates an otherwise unchanged removal receipt.
    $encoding=[Text.UTF8Encoding]::new($false)
    $bytes=[byte[]]($encoding.GetPreamble()+$encoding.GetBytes($updated))
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    $receipt=@{schema=1;root=[IO.Path]::GetFullPath($GamePath);existed=$existed;original=[Convert]::ToBase64String($original);sha256=$hash;state='applying'}
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReceiptPath))),([IO.Path]::GetDirectoryName($path)) | Out-Null
    $receipt | ConvertTo-Json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    [IO.File]::WriteAllBytes($path,$bytes)
    $receipt.state='applied';$receipt | ConvertTo-Json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
}

function Restore-ProductionLogging([string]$ReceiptPath) {
    $receipt=Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
    if($receipt.schema -ne 1 -or $receipt.state -ne 'applied'){throw 'Logging receipt is not applied.'}
    $path=Join-Path $receipt.root 'BepInEx/config/BepInEx.cfg'
    if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $receipt.sha256){throw 'BepInEx configuration changed; preserve it and restore logging manually from the receipt.'}
    if($receipt.existed){[IO.File]::WriteAllBytes($path,[Convert]::FromBase64String($receipt.original))}
    else{Remove-Item -LiteralPath $path}
    $receipt.state='restored';$receipt | ConvertTo-Json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
}
Export-ModuleMember -Function Get-ProductionLoggingText,Set-ProductionLogging,Restore-ProductionLogging
