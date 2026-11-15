using FM27Access.Navigation;
using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class ComposedShapeTests
{
    [Fact]
    public void FinalNativeShapeCannotProveAnEarlierPartialBodyContainsItsNewControls()
    {
        var before=ScreenShape.Compose(new[]{("staff",3),("footer",0)});
        var ready=ScreenShape.Compose(new[]{("staff",3),("footer",6)});
        Assert.NotEqual(before,ready);
        // Native layout finished during the across-frame table harvest. The
        // captured body is still partial, even though its receipt has the final
        // native signature. Neither forced nor changed-shape recovery may use
        // that receipt to conclude the new controls reached the companion.
        var partial=new ComposedShape(ready,100,true);
        Assert.False(partial.CanSuppress(ready,800,15000));
    }

    [Theory]
    [InlineData("ready",false,"ready",15000,true)]
    [InlineData("ready",false,"ready",15001,false)]
    [InlineData("old",false,"ready",1,false)]
    [InlineData("ready",true,"ready",1,false)]
    [InlineData("old",true,"ready",1,false)]
    public void OnlyFreshCompleteMatchingBodySuppressesRedundantRecovery(
        string served,bool partial,string current,long age,bool expected)
    {
        Assert.Equal(expected,new ComposedShape(served,100,partial).CanSuppress(current,100+age,15000));
    }
}
