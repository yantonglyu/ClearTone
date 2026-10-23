using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;

namespace ClearTone;

internal sealed class VideoPlaybackWorker : IDisposable
{
    internal sealed record State(bool Ready=false,double Position=0,string Playback="None",int Frames=0,double FrameWall=0,bool Seeking=false,int DownloadFailures=0,string DownloadFailure="",double WorkerWall=0,double Target=0,double Rate=1);
    sealed record Desired(bool Visible=false,bool Playing=false,double Position=0,double Rate=1,int Seek=0);
    readonly MediaCommandWorker worker;readonly CancellationTokenSource cancel=new();readonly bool adaptive;readonly string url;
    sealed record Pixels(IBuffer Buffer,int Width,int Height,int Sequence=0);
    readonly object frameGate=new();Pixels? front,back;CanvasDevice? decodeDevice;CanvasRenderTarget? decodeSurface;
    CanvasBitmap? viewBitmap;int viewSequence;
    MediaPlayer? player;MediaSource? source;AdaptiveMediaSource? adaptiveSource;
    Desired desired=new();State state=new();int pending,frames,lastSeek,seekFrames,downloadFailures;string downloadFailure="";
    volatile bool stopped,ready;bool playRequested,seeking;double lastSync,seekPosition,frameWall;
    public event Action<Exception>? Failed;
    public State Snapshot=>Volatile.Read(ref state);
    public bool Finished=>worker.Finished;
    static double Now=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
    public VideoPlaybackWorker(string url,bool adaptive){
        this.adaptive=adaptive;this.url=url;
        worker=new("ClearTone video",Tick,Cleanup,33);
    }
    public void Start()=>_=Observe(worker.Post(()=>Open(url)));
    async Task Observe(Task task){try{await task.ConfigureAwait(false);}catch(OperationCanceledException){}catch(Exception error){if(!stopped)Failed?.Invoke(error);}}
    void Open(string url){
        if(stopped)return;
        player=new MediaPlayer{AutoPlay=false,Volume=0,IsMuted=true,IsVideoFrameServerEnabled=true};player.CommandManager.IsEnabled=false;
        player.VideoFrameAvailable+=(_,_)=>Interlocked.Exchange(ref pending,1);
        player.MediaOpened+=(_,_)=>{ready=true;};
        player.MediaFailed+=(_,e)=>{if(!stopped)Failed?.Invoke(new IOException($"视频解码暂不可用 ({e.Error}, 0x{e.ExtendedErrorCode?.HResult:X8})",e.ExtendedErrorCode));};
        if(adaptive){
            var result=AdaptiveMediaSource.CreateFromUriAsync(new Uri(url)).AsTask(cancel.Token).GetAwaiter().GetResult();
            if(stopped){result.MediaSource?.Dispose();return;}
            if(result.Status!=AdaptiveMediaSourceCreationStatus.Success)throw new IOException("视频分段暂不可用："+result.Status);
            adaptiveSource=result.MediaSource;
            adaptiveSource.DownloadFailed+=(_,e)=>{Interlocked.Increment(ref downloadFailures);downloadFailure=$"{e.ResourceType}: HTTP {e.HttpResponseMessage?.StatusCode}, 0x{e.ExtendedError?.HResult:X8}";};
            source=MediaSource.CreateFromAdaptiveMediaSource(adaptiveSource);
        }else source=MediaSource.CreateFromUri(new Uri(url));
        if(!stopped)player.Source=source;
    }
    public void Update(bool visible,bool playing,double position,double rate,bool force){
        var before=Volatile.Read(ref desired);Volatile.Write(ref desired,new(visible,playing,position,rate,before.Seek+(force?1:0)));
        if(force||before.Visible!=visible||before.Playing!=playing||before.Rate!=rate)worker.Wake();
    }
    void Tick(){
        if(stopped||!ready||player==null)return;
        try{
            var target=Volatile.Read(ref desired);
            worker.Interval=target.Visible&&target.Playing?33:500;
            if(!target.Playing&&!playRequested&&lastSeek==target.Seek&&Volatile.Read(ref pending)==0&&Snapshot.Ready)return;
            var session=player.PlaybackSession;double wall=Now;
            bool playing=target.Visible&&target.Playing,started=playing&&!playRequested;
            if(!playing&&playRequested){player.Pause();playRequested=false;}
            double position=session.Position.TotalSeconds,drift=target.Position-position;
            if(seeking&&frames-seekFrames>=3&&wall-lastSync>.25&&position>seekPosition+.10)seeking=false;
            bool flowing=!seeking&&frames>0&&wall-frameWall<.3;
            bool force=lastSeek!=target.Seek;
            bool hard=target.Visible&&(force||started||playing&&session.PlaybackState!=MediaPlaybackState.Buffering&&flowing&&Math.Abs(drift)>2.5&&wall-lastSync>5);
            if(hard){
                lastSeek=target.Seek;seeking=true;seekFrames=frames;seekPosition=target.Position;Interlocked.Exchange(ref pending,0);
                session.Position=TimeSpan.FromSeconds(Math.Clamp(target.Position,0,Math.Max(0,session.NaturalDuration.TotalSeconds-.03)));lastSync=wall;
            }
            double correction=1;
            double rate=target.Rate*correction;if(Math.Abs(session.PlaybackRate-rate)>.01)session.PlaybackRate=rate;
            if(playing&&(started||force)){playRequested=true;player.Play();}
            if(target.Visible&&Interlocked.Exchange(ref pending,0)!=0){
                int width=(int)session.NaturalVideoWidth,height=(int)session.NaturalVideoHeight;
                if(width>0&&height>0){
                    decodeDevice??=new CanvasDevice();decodeSurface??=new CanvasRenderTarget(decodeDevice,width,height,96);
                    back??=new(new Windows.Storage.Streams.Buffer((uint)(width*height*4)){Length=(uint)(width*height*4)},width,height);
                    // This synchronous GPU/decoder call can wait after a distant
                    // HTTP seek. It must never execute in CanvasControl.Draw.
                    player.CopyFrameToVideoSurface(decodeSurface);decodeSurface.GetPixelBytes(back.Buffer);
                    back=back with{Sequence=frames+1};lock(frameGate){(front,back)=(back,front);}
                    frames++;frameWall=Now;
                }
            }
            Volatile.Write(ref state,new(true,session.Position.TotalSeconds,session.PlaybackState.ToString(),frames,frameWall,seeking,Volatile.Read(ref downloadFailures),downloadFailure,Now,target.Position,session.PlaybackRate));
        }catch(Exception error){if(!stopped){ready=false;Failed?.Invoke(error);}}
    }
    public bool Draw(CanvasDrawingSession drawing,double width,double height){
        // Never wait for a worker/native call from the UI. The front frame is
        // already complete; the decoder only writes into the separate back frame.
        if(!Monitor.TryEnter(frameGate))return false;
        try{
            if(front==null)return false;
            if(viewBitmap==null)viewBitmap=CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(),front.Buffer,front.Width,front.Height,DirectXPixelFormat.B8G8R8A8UIntNormalized);
            else if(viewSequence!=front.Sequence)viewBitmap.SetPixelBytes(front.Buffer);
            viewSequence=front.Sequence;
            double scale=Math.Min(width/front.Width,height/front.Height),w=front.Width*scale,h=front.Height*scale;
            drawing.DrawImage(viewBitmap,new Rect((width-w)/2,(height-h)/2,w,h),viewBitmap.Bounds);return true;
        }finally{Monitor.Exit(frameGate);}
    }
    public void Dispose(){if(stopped)return;stopped=true;_=cancel.CancelAsync();worker.Dispose();}
    // Called only by the UI. No D3D resource is shared with the decoder thread.
    public void ReleaseView(){viewBitmap?.Dispose();viewBitmap=null;}
    void Cleanup(){
        try{player?.Pause();if(player!=null)player.Source=null;player?.Dispose();}catch(Exception error){Storage.Log(error);}
        try{source?.Dispose();adaptiveSource?.Dispose();}catch(Exception error){Storage.Log(error);}
        decodeSurface?.Dispose();decodeDevice?.Dispose();lock(frameGate){front=back=null;}cancel.Dispose();
    }
}
