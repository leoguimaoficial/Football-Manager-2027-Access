using System.Text;
using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public sealed class HttpResponseWriterTests
{
    [Fact]
    public void Utf8LengthSeparatesTwoResponsesWithoutConnectionClosure()
    {
        using var stream=new MemoryStream();
        var writer=new HttpResponseWriter(stream,true);
        const string text="Escalação 日本語";
        writer.Write("text/plain",text,"X-FM-Token: example");writer.Write("text/plain","second");
        byte[] bytes=stream.ToArray();string all=Encoding.UTF8.GetString(bytes);
        Assert.Contains("Connection: keep-alive\r\n",all);Assert.Contains("Content-Length: "+Encoding.UTF8.GetByteCount(text)+"\r\n",all);
        Assert.Contains(text+"HTTP/1.1 200 OK\r\n",all);Assert.EndsWith("second",all);
    }

    [Theory]
    [InlineData(200,"OK")]
    [InlineData(403,"Forbidden")]
    [InlineData(404,"Not Found")]
    public void HeadHasTheSameLengthButNoBodyToCorruptTheNextResponse(int status,string reason)
    {
        using var stream=new MemoryStream();new HttpResponseWriter(stream,true,true).Write("text/plain","hidden",null,status);
        new HttpResponseWriter(stream,false).Write("text/plain","next");string text=Encoding.UTF8.GetString(stream.ToArray());
        Assert.StartsWith("HTTP/1.1 "+status+" "+reason,text);Assert.Contains("Content-Length: 6\r\n",text);
        Assert.DoesNotContain("hidden",text);Assert.Contains("\r\n\r\nHTTP/1.1 200 OK\r\n",text);Assert.Contains("Connection: close",text);
    }

    [Fact]
    public void BinaryResponsesRetainExactBytesAndCaching()
    {
        using var stream=new MemoryStream();byte[] body={0,255,13,10,128};new HttpResponseWriter(stream,true).WriteBinary("audio/wav",body);
        byte[] all=stream.ToArray();Assert.Equal(body,all[^body.Length..]);string header=Encoding.ASCII.GetString(all[..^body.Length]);
        Assert.Contains("Content-Length: 5\r\n",header);Assert.Contains("Cache-Control: max-age=86400",header);Assert.DoesNotContain("charset",header);
    }
}
