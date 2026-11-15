using System.Text;
using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class HttpRequestReaderTests
{
    private static HttpRequestReader.Request? Read(string frame, int fragment = 4096)
        => HttpRequestReader.Read(new FragmentedStream(Encoding.UTF8.GetBytes(frame), fragment));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(4096)]
    public void FragmentedHeadersAndMultibyteBodyRemainOneCompleteRequest(int fragment)
    {
        const string body = "Escalação: ação — 日本語";
        var request = Read($"POST /eval?mode=read HTTP/1.1\r\nHost: 127.0.0.1\r\nCookie: private\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}", fragment)!;
        Assert.Equal("POST", request.Method);
        Assert.Equal("/eval?mode=read", request.Target);
        Assert.Equal(body, request.Body);
        Assert.Contains("Cookie: private", request.Lines);
    }

    [Fact]
    public void EmptyPreconnectionDoesNotBecomeAnEntryPageRequest() => Assert.Null(Read(""));

    [Theory]
    [InlineData("POST /eval HTTP/1.1\r\nContent-Length: 4\r\n\r\nabc")]
    [InlineData("POST /eval HTTP/1.1\r\nContent-Length: 4\r\n\r\n")]
    [InlineData("GET /activate?id=abc HTTP/1.1\r\nCookie: private")]
    [InlineData("GET / HTTP/1.1\r\n\r")]
    public void EndOfStreamNeverReturnsAPartialCommand(string frame)
        => Assert.Equal(400, Assert.Throws<HttpRequestReader.ProtocolException>(() => Read(frame, 1)).Status);

    [Theory]
    [InlineData("Content-Length: -1", 400)]
    [InlineData("Content-Length: +1", 400)]
    [InlineData("Content-Length: ", 400)]
    [InlineData("Content-Length: 0x1", 400)]
    [InlineData("Content-Length: 1\r\nContent-Length: 2", 400)]
    [InlineData("Content-Length: 1, 2", 400)]
    [InlineData("Content-Length: 9999999999999999999999999", 413)]
    [InlineData("Content-Length: 4194305", 413)]
    [InlineData("Content-Length: 0\r\nTransfer-Encoding: chunked", 400)]
    [InlineData("Transfer-Encoding: chunked", 501)]
    [InlineData("Transfer-Encoding: unknown", 501)]
    public void InvalidOrUnsupportedFramingCannotReachADispatcher(string headers, int status)
        => Assert.Equal(status, Assert.Throws<HttpRequestReader.ProtocolException>(() => Read($"POST /eval HTTP/1.1\r\n{headers}\r\n\r\nx")).Status);

    [Theory]
    [InlineData("Content-Length: 3\r\nContent-Length: 3")]
    [InlineData("content-length: 003, 3")]
    public void IdenticalRepeatedLengthsHaveOneMeaning(string headers)
        => Assert.Equal("abc", Read($"POST /eval HTTP/1.1\r\n{headers}\r\n\r\nabc")!.Body);

    [Theory]
    [InlineData("GET / HTTP/9.9\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\n folded: value\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost : value\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: bad\nvalue\r\n\r\n")]
    public void MalformedStartAndHeaderLinesAreNotTolerated(string frame)
        => Assert.Equal(400, Assert.Throws<HttpRequestReader.ProtocolException>(() => Read(frame)).Status);

    [Fact]
    public void OversizedHeaderIsRejectedBeforeAnyBodyAllocation()
        => Assert.Equal(431, Assert.Throws<HttpRequestReader.ProtocolException>(() => Read("GET / HTTP/1.1\r\nX-Long: " + new string('a', HttpRequestReader.MaxHeaderBytes) + "\r\n\r\n")).Status);

    [Fact]
    public void ATruncatedUtf8CodePointIsNotSilentlyReplaced()
    {
        var bytes = Encoding.ASCII.GetBytes("POST /settext HTTP/1.1\r\nContent-Length: 1\r\n\r\n").Concat(new byte[] { 0xc3 }).ToArray();
        Assert.Equal(400, Assert.Throws<HttpRequestReader.ProtocolException>(() => HttpRequestReader.Read(new MemoryStream(bytes))).Status);
    }

    [Fact]
    public void FollowingBytesCannotBecomePartOfThisCommandsBody()
        => Assert.Equal("abc", Read("POST /eval HTTP/1.1\r\nContent-Length: 3\r\n\r\nabcGET /activate?id=other HTTP/1.1\r\n\r\n")!.Body);

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void PersistentReaderRetainsFollowingHeadersAndUtf8BodyBoundaries(int fragment)
    {
        const string body="ação 日本語";
        string frames="GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n"
            +$"POST /settext?id=owned HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}"
            +"GET /version HTTP/1.1\r\nConnection: close\r\n\r\n";
        var reader=new HttpRequestReader.ConnectionReader(new FragmentedStream(Encoding.UTF8.GetBytes(frames),fragment));
        Assert.Equal("/health",reader.Read()!.Target);
        var command=reader.Read()!;Assert.Equal("POST",command.Method);Assert.Equal(body,command.Body);Assert.True(command.Persistent);
        var last=reader.Read()!;Assert.Equal("/version",last.Target);Assert.False(last.Persistent);Assert.Null(reader.Read());
    }

    [Theory]
    [InlineData("HTTP/1.1","",true)]
    [InlineData("HTTP/1.1","Connection: keep-alive\r\n",true)]
    [InlineData("HTTP/1.1","Connection: Keep-Alive, CLOSE\r\n",false)]
    [InlineData("HTTP/1.1","Connection: keep-alive\r\nConnection: close\r\n",false)]
    [InlineData("HTTP/1.0","Connection: keep-alive\r\n",false)]
    public void PersistenceRequiresHttp11WithoutAnyCloseOption(string version,string header,bool expected)
        => Assert.Equal(expected,Read($"GET /health {version}\r\n{header}\r\n")!.Persistent);

    [Fact]
    public void MalformedSecondRequestCannotBeDispatchedAsAnEmptyCommand()
    {
        string frame="GET /health HTTP/1.1\r\n\r\nPOST /activate?id=owned HTTP/1.1\r\nContent-Length: 10\r\n\r\nshort";
        var reader=new HttpRequestReader.ConnectionReader(new MemoryStream(Encoding.UTF8.GetBytes(frame)));
        Assert.Equal("/health",reader.Read()!.Target);
        Assert.Throws<HttpRequestReader.ProtocolException>(()=>reader.Read());
    }

    private sealed class FragmentedStream(byte[] bytes, int fragment) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, fragment));
    }
}
