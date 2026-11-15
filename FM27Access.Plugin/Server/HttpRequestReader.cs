#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FM27Access.Plugin.Server;

// Preserve complete framing across persistent requests. Never dispatch a
// truncated command or interpret an unsupported framing as an empty body.
internal static class HttpRequestReader
{
    internal const int MaxHeaderBytes = 65536;
    internal const int MaxBodyBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal sealed class ProtocolException : IOException
    {
        internal int Status { get; }
        internal ProtocolException(string message, int status = 400) : base(message) => Status = status;
    }

    internal sealed class Request
    {
        internal string Method = "", Target = "", Body = "";
        internal string[] Lines = Array.Empty<string>();
        internal bool Persistent;
    }

    internal sealed class ConnectionReader
    {
        private readonly Stream stream;
        private readonly byte[] buffer = new byte[4096];
        private int offset, available;
        internal ConnectionReader(Stream stream) => this.stream = stream;
        internal bool HasBufferedData => offset < available;
        private int ReadByte()
        {
            if (offset == available) { available = stream.Read(buffer, 0, buffer.Length); offset = 0; }
            return offset == available ? -1 : buffer[offset++];
        }
        internal Request? Read()
        {
        var data = new List<byte>();
        int end = -1;
        while (end < 0)
        {
            int next = ReadByte();
            if (next < 0)
            {
                if (data.Count == 0) return null;
                throw new ProtocolException("Incomplete request headers.");
            }
            data.Add((byte)next);
            int n = data.Count;
            if (n >= 4 && data[n-4] == 13 && data[n-3] == 10 && data[n-2] == 13 && data[n-1] == 10) end = n-4;
            if (data.Count > MaxHeaderBytes)
                throw new ProtocolException("Request headers are too large.", 431);
        }

        byte[] bytes = data.ToArray();
        var lines = Encoding.ASCII.GetString(bytes, 0, end).Split(new[] { "\r\n" }, StringSplitOptions.None);
        var start = lines[0].Split(' ');
        if (start.Length != 3 || !Token(start[0]) || !start[1].StartsWith("/", StringComparison.Ordinal)
            || start[1].IndexOfAny(new[] { '\r', '\n', '\t', '\0' }) >= 0
            || (start[2] != "HTTP/1.1" && start[2] != "HTTP/1.0"))
            throw new ProtocolException("Invalid request line.");

        int? length = null;
        bool transferEncoding = false, close = false;
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0 || !Token(lines[i].Substring(0, colon))) throw new ProtocolException("Invalid request header.");
            string name = lines[i].Substring(0, colon), value = lines[i].Substring(colon + 1).Trim();
            foreach (char ch in value)
                if (ch < 32 && ch != '\t' || ch == 127) throw new ProtocolException("Invalid request header value.");
            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) transferEncoding = true;
            if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                foreach (string option in value.Split(','))
                    if (option.Trim().Equals("close", StringComparison.OrdinalIgnoreCase)) close = true;
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string part in value.Split(','))
            {
                string digits = part.Trim();
                if (digits.Length == 0) throw new ProtocolException("Invalid request content length.");
                long parsed = 0;
                foreach (char digit in digits)
                {
                    if (digit < '0' || digit > '9') throw new ProtocolException("Invalid request content length.");
                    parsed = parsed * 10 + digit - '0';
                    if (parsed > MaxBodyBytes) throw new ProtocolException("Request body is too large.", 413);
                }
                if (length.HasValue && length.Value != parsed) throw new ProtocolException("Conflicting request content lengths.");
                length = (int)parsed;
            }
        }
        // The companion sends known UTF-8 byte lengths. The original server did
        // not decode transfer codings; reject them instead of executing a prefix.
        if (transferEncoding) throw new ProtocolException(length.HasValue
            ? "Ambiguous request framing." : "Transfer coding is not supported.", length.HasValue ? 400 : 501);

        int bodyLength = length ?? 0;
        var body = new byte[bodyLength];
        int copied = Math.Min(bodyLength, available - offset);
        Array.Copy(buffer, offset, body, 0, copied);
        offset += copied;
        while (copied < bodyLength)
        {
            int count = stream.Read(body, copied, bodyLength - copied);
            if (count == 0) throw new ProtocolException("Incomplete request body.");
            copied += count;
        }
        string text;
        try { text = Utf8.GetString(body); }
        catch (DecoderFallbackException) { throw new ProtocolException("Invalid UTF-8 request body."); }
        return new Request { Method = start[0].ToUpperInvariant(), Target = start[1], Lines = lines, Body = text,
            Persistent = start[2] == "HTTP/1.1" && !close };
        }
    }

    // Compatibility helper for existing one-request callers/tests.
    internal static Request? Read(Stream stream) => new ConnectionReader(stream).Read();

    private static bool Token(string value)
    {
        if (value.Length == 0) return false;
        foreach (char ch in value)
            if (!(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z'
                || "!#$%&'*+-.^_`|~".IndexOf(ch) >= 0)) return false;
        return true;
    }
}
