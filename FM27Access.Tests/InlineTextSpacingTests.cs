using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class InlineTextSpacingTests
{
    [Theory]
    [InlineData("Antevisão da BWSL: Duelo Aston Villa - Man City é cabeça de cartaz",new[]{"Antevisão da","BWSL",": Duelo","Aston Villa","-","Man City","é cabeça de cartaz"})]
    [InlineData("(Arsenal), Chelsea!",new[]{"(","Arsenal","),","Chelsea","!"})]
    [InlineData("北京对上海",new[]{"北京","对","上海"})]
    [InlineData("Arsenal - Arsenal: Arsenal",new[]{"Arsenal","-","Arsenal",":","Arsenal"})]
    [InlineData("12.5%",new[]{"12",".","5","%"})]
    public void ReconstructsExactNativeTextAcrossLinkBoundaries(string native,string[] captions)
    {
        Assert.True(InlineTextSpacing.TryRead(native,captions,out var separators));
        Assert.NotNull(separators);
        Assert.Equal(native,string.Concat(captions.Select((caption,index)=>separators[index]+caption)));
    }

    [Theory]
    [InlineData("Arsenal have 12 points",new[]{"Arsenal","points"})]
    [InlineData("Arsenal - Chelsea",new[]{"Chelsea","Arsenal"})]
    [InlineData("Arsenal extra",new[]{"Arsenal"})]
    [InlineData("Arsenal",new[]{"Arsenal",""})]
    public void IncompleteOrChangedFragmentsRequireCompleteSourceFallback(string native,string[] captions)
    {
        Assert.False(InlineTextSpacing.TryRead(native,captions,out var separators));
        Assert.Null(separators);
    }
}
