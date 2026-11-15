using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class JsonMapTests
{
    [Fact]
    public void EmptyMap_IsEmptyObject()
    {
        Assert.Equal("{}", JsonMap.Encode(new Dictionary<string, string>()));
    }

    [Fact]
    public void SingleEntry_ExactShape()
    {
        var map = new Dictionary<string, string> { ["e1234abcd"] = "Smith, rating 7.2" };
        Assert.Equal("{\"e1234abcd\":\"Smith, rating 7.2\"}", JsonMap.Encode(map));
    }

    [Fact]
    public void QuotesAndBackslashes_AreEscaped()
    {
        var map = new Dictionary<string, string> { ["k"] = "say \"hi\" \\ now" };
        Assert.Equal("{\"k\":\"say \\\"hi\\\" \\\\ now\"}", JsonMap.Encode(map));
    }

    [Fact]
    public void Newlines_BecomeSingleLineEscapes()
    {
        var map = new Dictionary<string, string> { ["k"] = "line1\nline2\r\ttab" };
        var json = JsonMap.Encode(map);
        Assert.Equal("{\"k\":\"line1\\nline2\\r\\ttab\"}", json);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void ControlChar_BecomesUnicodeEscape()
    {
        var map = new Dictionary<string, string> { ["k"] = "ab" };
        Assert.Equal("{\"k\":\"a\\u0001b\"}", JsonMap.Encode(map));
    }

    [Fact]
    public void MultipleEntries_ContainsBothPairs()
    {
        var map = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var json = JsonMap.Encode(map);
        Assert.Contains("\"a\":\"1\"", json);
        Assert.Contains("\"b\":\"2\"", json);
    }
}
