using System;
using System.Text;
using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// Reading a person's name out of the game's entity link. The contract: the name comes back only
/// with the record reference the link itself carries (so the caller can refuse a name for anyone
/// else), and anything that is not exactly one well-formed link yields nothing rather than a guess.
/// </summary>
public class EntityLinkNameTests
{
    // A real link the game returned for Arman Durmiši's Name (captured 2026-10-04); his person
    // reference's CombinedIndexAndType was 0x0101706c.
    private const string ArmanPayload =
        "BAAAACAxMTEBIgMgMDExHAAAAGRpdUfcYCcAZmVyZGxwAQFmdjFiAQMAWWNPTwEDAXRuaWgBGiYAAABDbGljayB0byB2aWV3IEFybWFuIER1cm1pxaFpJ3MgcHJvZmlsZQ==";

    private static string Link(string payload, string name) => "\x01" + payload + "\x02" + name + "\x01";

    [Fact]
    public void ReadsTheNameAndTheRecordItNames()
    {
        Assert.True(EntityLinkName.TryParse(Link(ArmanPayload, "Arman Durmiši"), out int reference, out string name));
        Assert.Equal(0x0101706c, reference);
        Assert.Equal("Arman Durmiši", name);
    }

    [Fact]
    public void AcceptsUnpaddedBase64()
    {
        Assert.True(EntityLinkName.TryParse(Link(ArmanPayload.TrimEnd('='), "Arman Durmiši"), out int reference, out _));
        Assert.Equal(0x0101706c, reference);
    }

    [Fact]
    public void RefusesTextThatIsNotALink()
    {
        Assert.False(EntityLinkName.TryParse("Arman Durmiši", out _, out _));
        Assert.False(EntityLinkName.TryParse("", out _, out _));
        Assert.False(EntityLinkName.TryParse(null, out _, out _));
    }

    [Fact]
    public void RefusesALinkWithNoRecordReference()
    {
        string payload = Convert.ToBase64String(Encoding.ASCII.GetBytes("no reference here"));
        Assert.False(EntityLinkName.TryParse(Link(payload, "Someone"), out _, out _));
    }

    [Fact]
    public void RefusesALinkNamingTwoRecords()
    {
        // Two references would make it ambiguous which person the name is for.
        var bytes = new byte[] { (byte)'f', (byte)'e', (byte)'r', (byte)'d', 1, 2, 3, 4, (byte)'f', (byte)'e', (byte)'r', (byte)'d', 5, 6, 7, 8 };
        Assert.False(EntityLinkName.TryParse(Link(Convert.ToBase64String(bytes), "Someone"), out _, out _));
    }

    [Fact]
    public void RefusesAnEmptyName()
        => Assert.False(EntityLinkName.TryParse(Link(ArmanPayload, "  "), out _, out _));

    [Fact]
    public void RefusesTextHoldingMoreThanOneLink()
        => Assert.False(EntityLinkName.TryParse(Link(ArmanPayload, "Arman Durmiši") + " and " + Link(ArmanPayload, "Again"), out _, out _));

    [Fact]
    public void RefusesAPayloadThatIsNotBase64()
        => Assert.False(EntityLinkName.TryParse(Link("not*base64!", "Someone"), out _, out _));
}
