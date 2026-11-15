#nullable enable
using System;
using System.IO;
using System.Text;

namespace FM27Access.Plugin.Server;

// Exact response boundaries let the original raw-TCP server reuse its connection.
internal sealed class HttpResponseWriter
{
    private readonly Stream stream;
    private readonly bool persistent, headOnly;
    internal HttpResponseWriter(Stream stream, bool persistent, bool headOnly = false)
    { this.stream=stream; this.persistent=persistent; this.headOnly=headOnly; }

    internal void Write(string contentType,string body,string? extraHeader=null,int status=200)
        => WriteBytes(contentType+"; charset=utf-8",Encoding.UTF8.GetBytes(body),extraHeader,status);
    internal void WriteBinary(string contentType,byte[] body)
        => WriteBytes(contentType,body,"Cache-Control: max-age=86400",200);

    private void WriteBytes(string contentType,byte[] body,string? extraHeader,int status)
    {
        string reason=status switch {200=>"OK",400=>"Bad Request",403=>"Forbidden",404=>"Not Found",
            405=>"Method Not Allowed",413=>"Content Too Large",431=>"Request Header Fields Too Large",501=>"Not Implemented",_=>"Error"};
        string header=$"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\n"
            +(extraHeader==null?"":extraHeader+"\r\n")
            +$"Content-Length: {body.Length}\r\nConnection: {(persistent?"keep-alive":"close")}\r\n"
            +(persistent?"Keep-Alive: timeout=15, max=256\r\n":"")+"\r\n";
        byte[] bytes=Encoding.ASCII.GetBytes(header);stream.Write(bytes,0,bytes.Length);
        if(!headOnly)stream.Write(body,0,body.Length);
    }
}
