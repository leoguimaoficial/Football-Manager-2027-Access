using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

public class MainThreadDispatcherTests
{
    private static int QueuedWorkCount()
    {
        var queue=typeof(MainThreadDispatcher).GetField("_queue",
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        return (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
    }

    [Fact]
    public async Task StoppingReleasesAlreadyQueuedCallerWithoutAnotherUnityFrame()
    {
        MainThreadDispatcher.Stopped=false;
        MainThreadDispatcher.Drain();
        bool executed=false;
        var caller=Task.Run(()=>MainThreadDispatcher.Invoke(()=>executed=true,10000));
        try
        {
            Assert.True(SpinWait.SpinUntil(()=>QueuedWorkCount()>0,2000),"Actual dispatcher work was enqueued");
            MainThreadDispatcher.Stopped=true;
            Assert.Same(caller,await Task.WhenAny(caller,Task.Delay(1000)));
            var error=await Assert.ThrowsAsync<InvalidOperationException>(async()=>await caller);
            Assert.Contains("mod has stopped",error.Message);
            Assert.Equal(0,QueuedWorkCount());
            Assert.False(executed);
        }
        finally
        {
            // A failing old implementation must still release its real worker.
            MainThreadDispatcher.Drain();
            try{await caller;}catch(InvalidOperationException){}
            MainThreadDispatcher.Stopped=false;
        }
    }

    [Fact]
    public async Task StopAndConcurrentProducersCannotRetainOrExecutePendingCommands()
    {
        MainThreadDispatcher.Stopped=false;
        MainThreadDispatcher.Drain();
        int executed=0;
        var callers=Enumerable.Range(0,16).Select(_=>Task.Run(()=>Record.Exception(()=>
            MainThreadDispatcher.Invoke(()=>Interlocked.Increment(ref executed),10000)))).ToArray();
        try
        {
            Assert.True(SpinWait.SpinUntil(()=>QueuedWorkCount()>0,2000));
            MainThreadDispatcher.Stopped=true;
            var errors=await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.All(errors,error=>Assert.Contains("mod has stopped",Assert.IsType<InvalidOperationException>(error).Message));
            Assert.Equal(0,QueuedWorkCount());
            Assert.Equal(0,executed);
        }
        finally
        {
            MainThreadDispatcher.Drain();
            await Task.WhenAll(callers);
            MainThreadDispatcher.Stopped=false;
        }
    }

    [Fact]
    public void StoppedDispatcherRejectsWorkEvenOnItsFormerMainThread()
    {
        MainThreadDispatcher.Drain();
        bool executed = false;
        MainThreadDispatcher.Stopped = true;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                MainThreadDispatcher.Invoke(() => executed = true));
            Assert.Contains("mod has stopped", error.Message);
            MainThreadDispatcher.Drain();
            Assert.False(executed);
        }
        finally { MainThreadDispatcher.Stopped = false; }
    }
}
