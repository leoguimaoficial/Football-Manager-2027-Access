[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(2,200)][int]$Pairs=20,
    [ValidateRange(0,15000)][int]$ReuseDelayMilliseconds=0,
    [ValidateRange(0,10000)][int]$RandomizedConnections=0
)
. "$PSScriptRoot/Common.ps1"
if(-not $IsWindows){throw 'This diagnostic uses the Windows socket RTO API.'}
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
$destination=[IO.Path]::GetFullPath($OutputPath)
if(-not $destination.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must stay inside workspace artifacts.'}
if([IO.File]::Exists($destination)){throw 'Use a new output filename; existing evidence is preserved.'}
# Read-only production /health requests, not native business commands. Change
# only each disposable diagnostic socket's own SYN policy; never OS settings.
# Alternate native defaults and Chromium's documented loopback fast-fail policy.
if(-not ('FM27LoopbackReuseProbe' -as [type])){ Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
public static class FM27LoopbackReuseProbe {
    public sealed class Observation {
        public int Pair, Port, ErrorCode, ResponseBytes;
        public string Policy, BeforeState, Stage, Error, Utc, ReuseUtc;
        public long ConnectMs, PriorCloseAgeMs;
        public bool RtoApplied, Http200, RandomizationApplied;
    }
    static Socket Open(int port, bool fastFail, Observation row, bool randomize=false) {
        var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
        try {
            socket.ReceiveTimeout=2000; socket.SendTimeout=2000;
            if(randomize) {
                // Official Windows SDK ws2def.h SO_RANDOMIZE_PORT=0x3005.
                // This changes only this disposable socket, not the OS or app.
                socket.SetSocketOption(SocketOptionLevel.Socket,(SocketOptionName)0x3005,1);
                row.RandomizationApplied=true;
            }
            if(fastFail) {
                // mstcpip.h: _WSAIOW(IOC_VENDOR,17), ushort RTT=0 (default),
                // uchar MaxSynRetransmissions=0xfe (none), native struct size4.
                socket.IOControl(unchecked((int)0x98000011),new byte[]{0,0,254,0},null);
                row.RtoApplied=true;
            }
            row.Stage="bind";
            if(port>=0)socket.Bind(new IPEndPoint(IPAddress.Loopback,port));
            row.Stage="connect";
            var timer=Stopwatch.StartNew();
            try {
                // Use connect(), like Chromium. BeginConnect/ConnectEx adds
                // wildcard binding and SO_REUSE_UNICASTPORT inside .NET,
                // confounding an otherwise unbound-port comparison.
                socket.Blocking=false;
                try {socket.Connect(new IPEndPoint(IPAddress.Loopback,8787));}
                catch(SocketException e) when(e.SocketErrorCode==SocketError.WouldBlock) {
                    var writable=new List<Socket>{socket};var errors=new List<Socket>{socket};
                    Socket.Select(null,writable,errors,5000000);
                    if(writable.Count==0&&errors.Count==0)throw new TimeoutException("Diagnostic connect exceeded5s; no HTTP was sent.");
                    int error=(int)socket.GetSocketOption(SocketOptionLevel.Socket,SocketOptionName.Error);
                    if(error!=0)throw new SocketException(error);
                }
                socket.Blocking=true;
            } finally {
                row.ConnectMs=timer.ElapsedMilliseconds;
                if(socket.LocalEndPoint is IPEndPoint local)row.Port=local.Port;
            }
            row.Port=((IPEndPoint)socket.LocalEndPoint).Port;
            return socket;
        } catch {socket.Dispose();throw;}
    }
    static void Health(Socket socket,Observation row) {
        row.Stage="health";
        var bytes=Encoding.ASCII.GetBytes("GET /health HTTP/1.1\r\nHost: 127.0.0.1:8787\r\nConnection: close\r\n\r\n");
        for(int sent=0;sent<bytes.Length;)sent+=socket.Send(bytes,sent,bytes.Length-sent,SocketFlags.None);
        var buffer=new byte[4096]; var response=new StringBuilder();
        int read;
        while((read=socket.Receive(buffer))>0) {
            row.ResponseBytes+=read;
            if(row.ResponseBytes>65536)throw new InvalidOperationException("Unexpected health response size.");
            response.Append(Encoding.ASCII.GetString(buffer,0,read));
        }
        row.Http200=response.ToString().StartsWith("HTTP/1.1 200 ",StringComparison.Ordinal);
        if(!row.Http200)throw new InvalidOperationException("Health response was not200.");
        row.Stage="complete";
    }
    public static List<Observation> Run(int pairs,int delayMs) {
        var rows=new List<Observation>();
        for(int i=0;i<pairs;i++) {
            var row=new Observation{Pair=i,Policy=i%2==0?"NoSynRetransmissions":"NativeDefaults",Utc=DateTime.UtcNow.ToString("o"),BeforeState="NotObserved"};
            try {
                row.Stage="warmup";
                var first=new Observation();
                using(var initial=Open(0,false,first))Health(initial,first);
                row.Port=first.Port;
                // Diagnostic pacing only: model the observed ten-second tuple
                // reuse gap. It never controls game actions or UI readiness.
                if(delayMs>0)System.Threading.Thread.Sleep(delayMs);
                row.ReuseUtc=DateTime.UtcNow.ToString("o");
                foreach(var connection in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
                    if(connection.LocalEndPoint.Port==8787&&connection.RemoteEndPoint.Port==row.Port&&IPAddress.IsLoopback(connection.LocalEndPoint.Address))
                        row.BeforeState=connection.State.ToString();
                using(var reused=Open(row.Port,i%2==0,row))Health(reused,row);
            } catch(Exception e) {
                row.Error=e.GetType().Name+": "+e.Message;
                if(e is SocketException se)row.ErrorCode=se.NativeErrorCode;
            }
            rows.Add(row);
        }
        return rows;
    }
    public static List<Observation> RunRandomized(int count) {
        var rows=new List<Observation>();
        var closed=new Dictionary<int,long>();
        var timer=Stopwatch.StartNew(); int failures=0;
        for(int i=0;i<count;i++) {
            // Leave ample free ephemeral ports and bound pathological runs.
            if(timer.Elapsed.TotalSeconds>90 || failures>=5 ||
                IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length>4000)break;
            bool randomize=i%2!=0;
            var row=new Observation{Pair=i,Policy=randomize?"RandomizedNoSynRetransmissions":"OrdinaryNoSynRetransmissions",Utc=DateTime.UtcNow.ToString("o"),PriorCloseAgeMs=-1};
            try {
                // Deliberately unbound: Chromium applies randomization before
                // connect; the earlier explicit-bind probe bypassed that path.
                using(var socket=Open(-1,true,row,randomize))Health(socket,row);
            } catch(Exception e) {
                row.Error=e.GetType().Name+": "+e.Message;
                if(e is SocketException se)row.ErrorCode=se.NativeErrorCode;
                failures++;
            }
            if(row.Port>0) {
                if(closed.TryGetValue(row.Port,out long previous))row.PriorCloseAgeMs=timer.ElapsedMilliseconds-previous;
                if(row.Http200)closed[row.Port]=timer.ElapsedMilliseconds;
            }
            rows.Add(row);
        }
        return rows;
    }
}
'@
}
$report=@{
    utc=[DateTime]::UtcNow.ToString('o'); target='127.0.0.1:8787/health';
    nativeCommandsSubmitted=$false; systemSettingsChanged=$false;
    reuseDelayMilliseconds=$ReuseDelayMilliseconds;
    requestedRandomizedConnections=$RandomizedConnections;
    connectMethod='Nonblocking connect()/select, no ConnectEx wildcard/reuse-unicast setup';
    observations=if($RandomizedConnections){@([FM27LoopbackReuseProbe]::RunRandomized($RandomizedConnections))}else{@([FM27LoopbackReuseProbe]::Run($Pairs,$ReuseDelayMilliseconds))}
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$stream=[IO.FileStream]::new($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$writer=[IO.StreamWriter]::new($stream);$writer.Write(($report|ConvertTo-Json -Depth 5));$writer.Flush()}finally{if($writer){$writer.Dispose()}else{$stream.Dispose()}}
$report.observations | Group-Object Policy | ForEach-Object {
    $failures=@($_.Group | Where-Object Error)
    $unit=if($RandomizedConnections){'unbound health connections'}else{'explicit probe pairs'}
    Write-Output "$($_.Name): $($_.Count) $unit; $($failures.Count) failures. Evidence: $destination"
}
