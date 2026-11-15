[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$StopFile,
    [ValidateRange(1,3600)][int]$DurationSeconds=600,
    [ValidateRange(20,1000)][int]$SampleMilliseconds=50,
    [ValidateRange(1,65535)][int]$Port=8787
)
. "$PSScriptRoot/Common.ps1"
# Read-only OS socket observations. No packet payloads, listener/server,
# registry/network changes, process injection or administrative rights.
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
$output=[IO.Path]::GetFullPath($OutputPath)
if(-not $output.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must stay inside workspace artifacts.'}
if($StopFile){
    $StopFile=[IO.Path]::GetFullPath($StopFile)
    if(-not $StopFile.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Stop marker must stay inside workspace artifacts.'}
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$file=[IO.FileStream]::new($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
$writer=[IO.StreamWriter]::new($file)
$writer.AutoFlush=$true
$timer=[Diagnostics.Stopwatch]::StartNew()
$properties=[Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
$previous=@{}
$samples=0
try{
    $writer.WriteLine((@{kind='start';utc=[DateTime]::UtcNow.ToString('o');port=$Port;sampleMilliseconds=$SampleMilliseconds;durationSeconds=$DurationSeconds;payloadCaptured=$false} | ConvertTo-Json -Compress))
    while($timer.Elapsed.TotalSeconds -lt $DurationSeconds -and (-not $StopFile -or -not [IO.File]::Exists($StopFile))){
        $now=[DateTime]::UtcNow.ToString('o')
        $current=@{}
        foreach($connection in $properties.GetActiveTcpConnections()){
            $local=$connection.LocalEndPoint;$remote=$connection.RemoteEndPoint
            if(($local.Port -ne $Port -and $remote.Port -ne $Port) -or -not [Net.IPAddress]::IsLoopback($local.Address) -or -not [Net.IPAddress]::IsLoopback($remote.Address)){continue}
            $state=$connection.State.ToString()
            $key="$local>$remote|$state"
            $entry=@{local=$local.ToString();remote=$remote.ToString();localPort=$local.Port;remotePort=$remote.Port;state=$state}
            $current[$key]=$entry
            if(-not $previous.ContainsKey($key)){$writer.WriteLine((@{kind='appeared';utc=$now;elapsedMs=$timer.ElapsedMilliseconds;connection=$entry} | ConvertTo-Json -Compress))}
        }
        foreach($key in $previous.Keys){
            if(-not $current.ContainsKey($key)){$writer.WriteLine((@{kind='disappeared';utc=$now;elapsedMs=$timer.ElapsedMilliseconds;connection=$previous[$key]} | ConvertTo-Json -Compress))}
        }
        $previous=$current;$samples++
        Start-Sleep -Milliseconds $SampleMilliseconds
    }
}finally{
    $writer.WriteLine((@{kind='stop';utc=[DateTime]::UtcNow.ToString('o');elapsedMs=$timer.ElapsedMilliseconds;samples=$samples} | ConvertTo-Json -Compress))
    $writer.Dispose();$file.Dispose()
}
Write-Output "Loopback state observation completed: $output ($samples samples)."
