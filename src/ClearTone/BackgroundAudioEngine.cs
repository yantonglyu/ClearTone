namespace ClearTone;

// The UI reads immutable snapshots. All decoder, WASAPI and stream operations
// belong to a serial worker, so a slow HTTP seek cannot hold the window hostage.
public sealed class BackgroundAudioEngine : IDisposable
{
    sealed record Snapshot(bool Playing=false,double Duration=0,double Position=0,double Presentation=0,double Heard=0,double Speed=1,int Rate=0,string Device="",string RequestedDevice="",bool Exclusive=false,bool RequestedExclusive=false,string Notice="",string Output="尚未播放",string Format="尚未播放",string Processing="尚未播放",int OutputRate=0,float[]? Bars=null);
    sealed class Session {public readonly AudioEngine Engine=new();public MediaCommandWorker Worker=null!;public int Seek,Generation;public bool Ready;public double HeardOffset,RequestedSpeed=1;public string Source="";public AppState Settings=new();}
    readonly object sessionsGate=new();readonly List<Session> retired=[];
    Session? active;Snapshot snapshot=new();AudioProcessingOptions processing=new();
    volatile bool disposed,desiredPlaying;int generation;
    readonly TimeSpan seekTimeout;static readonly float[] emptySpectrum=new float[48];
    public BackgroundAudioEngine():this(TimeSpan.FromSeconds(8)){}
    internal BackgroundAudioEngine(TimeSpan seekTimeout)=>this.seekTimeout=seekTimeout;
    public event Action? Ended;public event Action<Exception>? Failed;
    public bool Seeking {get;private set;}
    public bool Playing=>!Seeking&&desiredPlaying&&Volatile.Read(ref snapshot).Playing;
    public double Duration=>Volatile.Read(ref snapshot).Duration;
    public double Position=>Volatile.Read(ref snapshot).Position;
    public double PresentationPosition=>Volatile.Read(ref snapshot).Presentation;
    public double PlayedSeconds=>Volatile.Read(ref snapshot).Heard;
    public double Speed=>Volatile.Read(ref snapshot).Speed;
    public int SampleRate=>Volatile.Read(ref snapshot).Rate;
    public string ActiveOutputDeviceId=>Volatile.Read(ref snapshot).Device;
    public string OutputDeviceId=>Volatile.Read(ref snapshot).RequestedDevice;
    public bool ActiveExclusiveOutput=>Volatile.Read(ref snapshot).Exclusive;
    public bool ExclusiveOutput=>Volatile.Read(ref snapshot).RequestedExclusive;
    public string OutputNotice=>Volatile.Read(ref snapshot).Notice;
    public string OutputDescription=>Volatile.Read(ref snapshot).Output;
    public string FormatDescription=>Volatile.Read(ref snapshot).Format;
    public string ProcessingDescription=>Volatile.Read(ref snapshot).Processing;
    public int OutputSampleRate=>Volatile.Read(ref snapshot).OutputRate;
    public float[] Spectrum()=>Volatile.Read(ref snapshot).Bars??emptySpectrum;
    bool Current(Session session)=>!disposed&&ReferenceEquals(Volatile.Read(ref active),session);
    void Publish(Session session){
        if(!Current(session)||!session.Ready)return;var e=session.Engine;
        var next=new Snapshot(e.Playing,e.Duration,e.Position,e.PresentationPosition,e.PlayedSeconds+session.HeardOffset,e.Speed,e.SampleRate,e.ActiveOutputDeviceId,e.OutputDeviceId,e.ActiveExclusiveOutput,e.ExclusiveOutput,e.OutputNotice,e.OutputDescription,e.FormatDescription,e.ProcessingDescription,e.OutputSampleRate,e.Playing?e.Spectrum():emptySpectrum);
        lock(sessionsGate)if(Current(session)){Volatile.Write(ref snapshot,next);session.Worker.Interval=next.Playing?50:500;}
    }
    Session CreateSession(string url,AppState settings,int version,double heardOffset=0,Session? expected=null,int expectedSeek=0){
        lock(sessionsGate){
            if(disposed||version!=generation||expected!=null&&(!ReferenceEquals(active,expected)||expected.Seek!=expectedSeek))throw new OperationCanceledException();
            retired.RemoveAll(s=>s.Worker.Finished);
            // Native APIs cannot be forcibly killed safely. Bound abandoned
            // decoders instead of starting unbounded threads on repeated clicks.
            if(retired.Count>=3)throw new IOException("网络音频仍在退出，请稍后重试。窗口和专注计时可以继续使用。");
            var previous=active;if(previous!=null){active=null;previous.Worker.Dispose();retired.Add(previous);}
            var next=new Session{HeardOffset=heardOffset,Source=url,Settings=settings,Generation=version};active=next;
            next.Engine.CanContinue=()=>Current(next);
            next.Engine.Ended+=()=>{if(Current(next))Observe(next.Worker.Post(()=>{if(Current(next)){desiredPlaying=false;Publish(next);Ended?.Invoke();}}),next);};
            next.Worker=new("ClearTone audio",()=>{if(desiredPlaying||Volatile.Read(ref snapshot).Playing)Publish(next);},()=>next.Engine.Dispose(),500);
            return next;
        }
    }
    public async Task Load(string url,AppState state){
        ObjectDisposedException.ThrowIf(disposed,this);int version=Interlocked.Increment(ref generation);
        desiredPlaying=false;Seeking=false;var settings=CopyState(state);
        var session=CreateSession(url,settings,version);Volatile.Write(ref snapshot,new());
        try{await session.Worker.Post(()=>{session.Engine.ConfigureProcessing(processing);session.Engine.Load(url,settings).GetAwaiter().GetResult();if(!Current(session)||version!=generation)return;session.Ready=true;Publish(session);}).WaitAsync(TimeSpan.FromSeconds(45));}
        catch{session.Worker.Dispose();throw;}
    }
    static AppState CopyState(AppState state)=>new(){Volume=state.Volume,Eq=state.Eq,Gains=state.Gains.ToArray(),OutputDeviceId=state.OutputDeviceId,ExclusiveOutput=state.ExclusiveOutput};
    void Queue(Action<AudioEngine> command,string? key=null){
        var session=active;if(session==null||disposed)return;
        Observe(session.Worker.Post(()=>{if(!Current(session))return;command(session.Engine);Publish(session);},key),session);
    }
    async void Observe(Task pending,Session session){try{await pending.ConfigureAwait(false);}catch(OperationCanceledException){}catch(Exception error){if(Current(session))Failed?.Invoke(error);}}
    public void Play(){desiredPlaying=true;Queue(e=>{if(desiredPlaying)e.Play();},"transport");}
    public void Pause(){desiredPlaying=false;Queue(e=>e.Pause(),"transport");}
    public Task PauseAsync(){desiredPlaying=false;var session=active;return session==null?Task.CompletedTask:session.Worker.Post(()=>{session.Engine.Pause();Publish(session);},"transport");}
    public async Task Seek(double seconds){
        var session=active;if(session==null||!session.Ready||disposed||!double.IsFinite(seconds))return;
        int request=Interlocked.Increment(ref session.Seek);double target=Math.Clamp(seconds,0,Duration);Seeking=true;
        var before=Volatile.Read(ref snapshot);Volatile.Write(ref snapshot,before with{Position=target,Presentation=target});
        try{
            await session.Worker.Post(()=>{
                if(!Current(session)||request!=session.Seek)return;
                session.Engine.Seek(target,()=>Current(session)&&request==session.Seek&&desiredPlaying);
                if(Current(session)&&request==session.Seek){Seeking=false;Publish(session);}
            },"seek").WaitAsync(seekTimeout).ConfigureAwait(false);
        }catch(OperationCanceledException){}
        catch(TimeoutException){
            if(!Current(session)||request!=session.Seek)return;
            // Abandon only this decoder. Reopen on a fresh serial worker, retain
            // genuine heard seconds, and never count the skipped media distance.
            Failed?.Invoke(new IOException("网络定位较慢，正在重新连接音频…"));
            await RecoverSeek(session,request,target).ConfigureAwait(false);
        }catch(Exception e){if(Current(session)&&request==session.Seek){Seeking=false;Failed?.Invoke(e);}}
    }
    async Task RecoverSeek(Session stalled,int request,double target){
        if(!Current(stalled)||request!=stalled.Seek)return;
        double heard=PlayedSeconds;Session replacement;
        var settings=CopyState(stalled.Settings);string url=stalled.Source;
        try{replacement=CreateSession(url,settings,stalled.Generation,heard,stalled,request);}catch(OperationCanceledException){return;}catch(Exception error){if(Current(stalled)){Seeking=false;desiredPlaying=false;Failed?.Invoke(error);}return;}
        replacement.RequestedSpeed=stalled.RequestedSpeed;
        try{
            await replacement.Worker.Post(()=>{
                replacement.Engine.ConfigureProcessing(processing);replacement.Engine.Load(url,settings).GetAwaiter().GetResult();
                if(!Current(replacement))return;replacement.Ready=true;
                replacement.Engine.Volume(settings.Volume);replacement.Engine.SetSpeed(replacement.RequestedSpeed);
                replacement.Engine.Seek(target,()=>Current(replacement)&&desiredPlaying);
                if(Current(replacement)){Seeking=false;Publish(replacement);}
            }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        }catch(Exception error){if(Current(replacement)){desiredPlaying=false;Seeking=false;replacement.Worker.Dispose();Failed?.Invoke(new IOException("网络音频暂时无法定位，可重新选歌。",error));}}
    }
    public void SetSpeed(double speed){if(active is {} session)session.RequestedSpeed=speed;Queue(e=>e.SetSpeed(speed),"speed");}
    public void Volume(double value){if(active is {} session)session.Settings.Volume=value;Queue(e=>e.Volume(value),"volume");}
    public void Eq(bool enabled,double[] gains){if(active is {} session){session.Settings.Eq=enabled;session.Settings.Gains=gains.ToArray();}Queue(e=>e.Eq(enabled,gains),"eq");}
    public void ConfigureProcessing(AudioProcessingOptions options){processing=options;Queue(e=>e.ConfigureProcessing(options),"processing");}
    public async Task SelectOutput(string? id,bool exclusive){
        var session=active;if(session==null)return;
        await session.Worker.Post(()=>{if(Current(session)){session.Engine.SelectOutput(id,exclusive);Publish(session);}}).WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        if(Current(session)){session.Settings.OutputDeviceId=id??"";session.Settings.ExclusiveOutput=exclusive;}
    }
    public void Dispose(){lock(sessionsGate){if(disposed)return;disposed=true;desiredPlaying=false;Interlocked.Increment(ref generation);var previous=active;active=null;previous?.Worker.Dispose();foreach(var session in retired)session.Worker.Dispose();}}
}
