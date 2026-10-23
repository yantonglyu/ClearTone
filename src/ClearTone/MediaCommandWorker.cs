using System.Collections.Concurrent;

namespace ClearTone;

// Native decoders can wait inside synchronous network/COM calls. No UI caller
// ever joins this background thread, including during shutdown or cancellation.
internal sealed class MediaCommandWorker : IDisposable
{
    sealed record Command(Action Run,Action Cancel,string? Key,long Version);
    readonly ConcurrentQueue<Command> queue=new();
    readonly ConcurrentDictionary<string,long> versions=new();
    readonly AutoResetEvent wake=new(false);
    readonly Action? tick,cleanup;readonly Thread thread;
    public int Interval {get;set;}
    int stopped;public bool Finished {get;private set;}
    public MediaCommandWorker(string name,Action? tick=null,Action? cleanup=null,int interval=30){
        this.tick=tick;this.cleanup=cleanup;Interval=interval;
        thread=new Thread(Run){IsBackground=true,Name=name};thread.SetApartmentState(ApartmentState.MTA);thread.Start();
    }
    public Task Post(Action action,string? key=null){
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(Volatile.Read(ref stopped)!=0){completion.SetCanceled();return completion.Task;}
        long version=key==null?0:versions.AddOrUpdate(key,1,(_,n)=>n+1);
        queue.Enqueue(new(()=>{try{action();completion.TrySetResult();}catch(Exception e){completion.TrySetException(e);}},()=>completion.TrySetCanceled(),key,version));
        if(Volatile.Read(ref stopped)!=0)completion.TrySetCanceled();wake.Set();return completion.Task;
    }
    void Run(){
        try{
            while(Volatile.Read(ref stopped)==0){
                while(Volatile.Read(ref stopped)==0&&queue.TryDequeue(out var command)){
                    if(command.Key!=null&&versions[command.Key]!=command.Version)command.Cancel();else command.Run();
                }
                if(Volatile.Read(ref stopped)==0)try{tick?.Invoke();}catch(Exception e){Storage.Log(e);}
                wake.WaitOne(Math.Clamp(Interval,10,1000));
            }
        }finally{
            while(queue.TryDequeue(out var pending))pending.Cancel();
            try{cleanup?.Invoke();}catch(Exception e){Storage.Log(e);}Finished=true;
            // Dispose does not close the event while a producer may still post.
        }
    }
    public void Dispose(){if(Interlocked.Exchange(ref stopped,1)==0)wake.Set();}
    public void Wake()=>wake.Set();
}
