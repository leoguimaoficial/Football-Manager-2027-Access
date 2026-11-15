using FM27Access.Navigation;

namespace FM27Access.Tests;

public class IdRegistryTests
{
    [Fact]
    public void SameKey_DifferentRegistries_SameId()
    {
        // Determinism across renders: a fresh registry must produce the same id for the
        // same key, so focus/values restore by id between rebuilds.
        var a = new IdRegistry();
        var b = new IdRegistry();
        Assert.Equal(a.Allocate("/Root:0/Card:3"), b.Allocate("/Root:0/Card:3"));
    }

    [Fact]
    public void DifferentKeys_DifferentIds()
    {
        var reg = new IdRegistry();
        Assert.NotEqual(reg.Allocate("/Root:0/Card:0"), reg.Allocate("/Root:0/Card:1"));
    }

    [Fact]
    public void RepeatedKey_GetsNumericSuffix()
    {
        var reg = new IdRegistry();
        var first = reg.Allocate("dup");
        var second = reg.Allocate("dup");
        var third = reg.Allocate("dup");
        Assert.Equal(first + "-2", second);
        Assert.Equal(first + "-3", third);
    }

    [Fact]
    public void Clear_ResetsCollisionPool()
    {
        var reg = new IdRegistry();
        var first = reg.Allocate("dup");
        reg.Clear();
        // After a clear the same key is unoccupied again, so no suffix.
        Assert.Equal(first, reg.Allocate("dup"));
    }

    [Fact]
    public void IdFormat_IsEPrefixedHex()
    {
        var reg = new IdRegistry();
        var id = reg.Allocate("anything");
        Assert.StartsWith("e", id);
        Assert.Equal(9, id.Length); // 'e' + 8 hex digits
    }
}
