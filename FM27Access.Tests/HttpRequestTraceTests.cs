using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

public class HttpRequestTraceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CapacityMustBePositive(int capacity)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>new HttpRequestTrace(capacity));

    [Fact]
    public void LateCompletionDoesNotReinsertAnEvictedRequest()
    {
        var trace=new HttpRequestTrace(2);
        var old=trace.Begin(40001);var second=trace.Begin(40002);var third=trace.Begin(40003);
        trace.Mark(old,"failed","GET /body","IOException");
        Assert.Equal(new[]{second.Id,third.Id},trace.Snapshot().Select(e=>e.Id));
    }

    [Fact]
    public void SnapshotIsIndependentOfConcurrentPhaseChanges()
    {
        var trace=new HttpRequestTrace();var request=trace.Begin(40001);
        var before=trace.Snapshot();trace.Mark(request,"handling","GET /body");
        Assert.Equal("reading",Assert.Single(before).Phase);
        before[0].Route="modified caller copy";
        Assert.Equal("GET /body",Assert.Single(trace.Snapshot()).Route);
    }

    [Fact]
    public void FailureKeepsConnectionIdentityAndElapsedTime()
    {
        var trace=new HttpRequestTrace();var request=trace.Begin(40001);
        trace.Mark(request,"handling","GET /body");trace.Mark(request,"failed",errorType:"IOException");
        var snapshot=Assert.Single(trace.Snapshot());
        Assert.Equal(request.Id,snapshot.Id);Assert.Equal(40001,snapshot.PeerPort);
        Assert.Equal("GET /body",snapshot.Route);Assert.Equal("failed",snapshot.Phase);
        Assert.Equal("IOException",snapshot.ErrorType);Assert.True(snapshot.ElapsedMs>=0);
        Assert.Equal(DateTimeKind.Utc,snapshot.StartedUtc.Kind);
    }

    [Fact]
    public void ConcurrentRequestsRemainBoundedWithUniqueOrderedIds()
    {
        var trace=new HttpRequestTrace(32);
        Parallel.For(0,500,i=>{var request=trace.Begin(40000+i);trace.Mark(request,"response written","GET /health");});
        var entries=trace.Snapshot();Assert.Equal(32,entries.Length);
        Assert.Equal(Enumerable.Range(469,32).Select(i=>(long)i),entries.Select(e=>e.Id));
        Assert.All(entries,e=>Assert.Equal("response written",e.Phase));
    }
}
