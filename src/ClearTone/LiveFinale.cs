using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;

namespace ClearTone;

// Local, pre-aligned media only. No network access or account data in the profile.
public sealed record LiveFinaleProfile {
    public bool Enabled {get;init;}
    public string File {get;init;}="";
    public double SongStart {get;init;}
    public double FadeIn {get;init;}=5;
    public double FadeOut {get;init;}=3;
    public double PerformanceEnd {get;init;}=0;
    public double QuietSeconds {get;init;}=3;
    public double LiveGain {get;init;}=1;
    public double StudioGain {get;init;}=0;
    public bool Valid=>double.IsFinite(PerformanceEnd)&&PerformanceEnd>=0&&SongStart>0&&double.IsFinite(SongStart)&&FadeIn is >=1 and <=30&&FadeOut is >=1 and <=10&&QuietSeconds is >=0 and <=10&&LiveGain is >=0 and <=1&&StudioGain is >=0 and <=1;
}

public sealed partial class MainPage {
    LiveFinaleProfile? liveProfile;
    MediaPlayer? livePlayer;MediaSource? liveSource;CanvasControl? liveCanvas;CanvasRenderTarget? liveFrame;Popup? livePopup;Grid? liveSurface;
    int liveFramePending;bool liveHasFrame;double liveVisualOpacity,liveStudioGain,liveAudioGain,liveEntrance;int liveFrames;
    readonly DispatcherTimer liveTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    bool liveReady,liveStarted,liveStarting,liveSongEnded,liveVideoEnded,liveCompleting,liveFailed,liveRequested;
    int liveVersion;double liveEndWall=-1,liveLastPosition,liveProgressWall,liveStudioPosition,liveStudioProgressWall;
    bool LiveLocked=>liveStarted;
    void InitializeLiveFinale(){
        // Block input without switching controls to their visibly dimmed disabled style.
        PreviewKeyDown+=(_,e)=>{if(LiveLocked)e.Handled=true;};
        PreviewKeyUp+=(_,e)=>{if(LiveLocked)e.Handled=true;};
        try{var path=Path.Combine(Storage.Data,"easter-eggs","live-finale.json");if(System.IO.File.Exists(path)){
            var p=JsonSerializer.Deserialize<LiveFinaleProfile>(System.IO.File.ReadAllText(path));
            if(p is {Enabled:true,Valid:true}&&Path.IsPathFullyQualified(p.File)&&System.IO.File.Exists(p.File))liveProfile=p;
        }}catch(Exception e){Storage.Log(e);}
        liveTimer.Tick+=(_,_)=>TickLiveFinale();
    }
    bool IsLiveTrack=>liveProfile!=null&&current!=null&&eggProfiles.Any(p=>p.Resolve(current,lyrics,audio.Duration)!=null);
    void ConsiderLiveFinale(){
        if(!IsLiveTrack||!trackReady||liveFailed||liveRequested||audio.Speed!=1||practiceLoop)return;
        var p=liveProfile!;
        // Prepare shortly before the transition, without locking UI or changing volume.
        if(audio.PresentationPosition>=p.SongStart-12&&audio.PresentationPosition<audio.Duration-3){liveRequested=true;_=PrepareLiveFinale();}
    }
    async Task PrepareLiveFinale(){
        int version=loadVersion;liveVersion=version;
        var player=new MediaPlayer{AutoPlay=false,Volume=0,IsVideoFrameServerEnabled=true};livePlayer=player;
        player.CommandManager.IsEnabled=false;
        player.VideoFrameAvailable+=(_,_)=>{if(ReferenceEquals(livePlayer,player))Interlocked.Exchange(ref liveFramePending,1);};
        player.MediaOpened+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{if(livePlayer==player&&loadVersion==version){liveReady=true;liveTimer.Start();}});
        player.MediaEnded+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{if(livePlayer==player){liveVideoEnded=true;TickLiveFinale();}});
        player.MediaFailed+=(_,args)=>DispatcherQueue.TryEnqueue(()=>{if(livePlayer==player)FailLiveFinale(new IOException("Local video: "+args.ErrorMessage));});
        try{
            // WASAPI endpoint IDs are not WinRT device-interface IDs. Resolve the
            // actually active output (including fallback), rather than the saved ID.
            string endpoint=audio.ActiveOutputDeviceId;
            if(endpoint.Length>0){
                var devices=await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioRenderSelector());
                if(disposed||version!=loadVersion||livePlayer!=player)return;
                var device=devices.FirstOrDefault(d=>d.Id.Equals(endpoint,StringComparison.OrdinalIgnoreCase)||d.Id.Contains(endpoint,StringComparison.OrdinalIgnoreCase));
                if(device==null)throw new IOException("现场视频的输出设备暂不可用，请重新选择音频输出。");
                player.AudioDevice=device;
            }
            if(disposed||version!=loadVersion||livePlayer!=player)return;
            liveSource=MediaSource.CreateFromUri(new Uri(liveProfile!.File));player.Source=liveSource;
            liveProgressWall=eggWatch.Elapsed.TotalSeconds;liveTimer.Start();
        }catch(Exception e){if(livePlayer==player)FailLiveFinale(e);}
    }
    void TickLiveFinale(){
        if(disposed||livePlayer==null||liveProfile==null)return;
        try{
            if(liveVersion!=loadVersion){ResetLiveFinale();return;}
            double wall=eggWatch.Elapsed.TotalSeconds;
            if(!liveReady){if(wall-liveProgressWall>12)FailLiveFinale(new TimeoutException("Local video preparation timed out"));return;}
            if(!liveStarted){
                if(!audio.Playing||audio.PresentationPosition<liveProfile.SongStart)return;
                double offset=audio.PresentationPosition-liveProfile.SongStart;
                if(livePlayer.PlaybackSession.NaturalDuration.TotalSeconds-offset<1){FailLiveFinale(new IOException("Local clip does not cover the current position"));return;}
                // Video and studio audio must share the same output during the blend.
                if(audio.ActiveExclusiveOutput){if(!liveStarting)_=ShareLiveOutput();return;}
                openDialog?.Hide();StopPractice();
                liveCanvas=new CanvasControl{ClearColor=Microsoft.UI.Colors.Transparent,IsHitTestVisible=false};
                liveCanvas.Draw+=DrawLiveFrame;
                liveCanvas.CreateResources+=(_,_)=>{liveFrame?.Dispose();liveFrame=null;liveHasFrame=false;Interlocked.Exchange(ref liveFramePending,1);};
                liveSurface=new Grid{Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};liveSurface.Children.Add(liveCanvas);
                livePopup=new Popup{XamlRoot=XamlRoot,Child=liveSurface,IsLightDismissEnabled=false,ShouldConstrainToRootBounds=true};
                liveStarted=true;Root.IsHitTestVisible=false;
                livePlayer.PlaybackSession.Position=TimeSpan.FromSeconds(Math.Max(0,offset));livePlayer.Play();liveProgressWall=liveStudioProgressWall=wall;
            }
            liveSurface!.Width=XamlRoot.Size.Width;liveSurface.Height=XamlRoot.Size.Height;livePopup!.IsOpen=true;
            double position=livePlayer.PlaybackSession.Position.TotalSeconds;
            if(Math.Abs(position-liveLastPosition)>.02){liveLastPosition=position;liveProgressWall=wall;}
            if(!liveVideoEnded&&wall-liveProgressWall>15){FailLiveFinale(new TimeoutException("Local video stopped advancing"));return;}
            if(Math.Abs(audio.PresentationPosition-liveStudioPosition)>.02){liveStudioPosition=audio.PresentationPosition;liveStudioProgressWall=wall;}
            if(!liveSongEnded&&wall-liveStudioProgressWall>5){FailLiveFinale(new IOException("Studio audio stopped advancing"));return;}
            // The full-size image is present from frame one. Only its opacity changes,
            // linearly across the entire bridge; there is no scale or centre reveal.
            // Device presentation clocks may reset at EOF. Completed entrance must not
            // collapse to zero (or restore studio gain) while the exit is still running.
            liveEntrance=Math.Max(liveEntrance,Math.Clamp((audio.PresentationPosition-liveProfile.SongStart)/liveProfile.FadeIn,0,1));
            double fade=liveEntrance;
            // A true handover: the studio stream reaches silence as live reaches full gain.
            liveStudioGain=1-fade;liveAudioGain=liveProfile.LiveGain*fade;
            audio.Volume(state.Volume*liveStudioGain);
            
            // A padded ambience tail remains audible after the performance ends.
            bool performanceEnded=liveVideoEnded||(liveProfile.PerformanceEnd>0&&position>=liveProfile.PerformanceEnd);
            if(liveSongEnded&&performanceEnded&&liveEndWall<0)liveEndWall=wall;
            double exit=liveEndWall<0?1:1-LiveEase((wall-liveEndWall)/liveProfile.FadeOut);
            liveVisualOpacity=fade*exit;liveAudioGain*=exit;livePlayer.Volume=state.Volume*liveAudioGain;liveCanvas?.Invalidate();
            if(liveEndWall>=0&&wall-liveEndWall>=liveProfile.FadeOut+liveProfile.QuietSeconds&&!liveCompleting){
                liveCompleting=true;ResetLiveFinale();_=Next(1);
            }
        }catch(Exception e){FailLiveFinale(e);}
    }
    async Task ShareLiveOutput(){
        liveStarting=true;int version=liveVersion;var player=livePlayer;
        try{await audio.SelectOutput(state.OutputDeviceId,false).WaitAsync(TimeSpan.FromSeconds(8));}
        catch(Exception error){if(!disposed&&liveVersion==version&&livePlayer==player)FailLiveFinale(error);}
        finally{if(liveVersion==version&&livePlayer==player)liveStarting=false;}
    }
    void DrawLiveFrame(CanvasControl sender,CanvasDrawEventArgs args){
        if(livePlayer==null||!liveStarted)return;
        try{
            if(Interlocked.Exchange(ref liveFramePending,0)!=0&&!liveVideoEnded){
                int width=(int)livePlayer.PlaybackSession.NaturalVideoWidth,height=(int)livePlayer.PlaybackSession.NaturalVideoHeight;
                if(width>0&&height>0){
                    liveFrame??=new CanvasRenderTarget(sender,width,height,96);
                    livePlayer.CopyFrameToVideoSurface(liveFrame);liveHasFrame=true;liveFrames++;
                }
            }
            // Retain the last decoded GPU frame after EOF. Hardware video surfaces may
            // clear at EOF and do not reliably honor opacity inherited from their parent.
            DrawLiveImage(args.DrawingSession,sender.ActualWidth,sender.ActualHeight);
        }catch(Exception e){DispatcherQueue.TryEnqueue(()=>{if(!disposed&&liveStarted)FailLiveFinale(e);});}
    }
    void DrawLiveImage(CanvasDrawingSession session,double w,double h){
        if(!liveHasFrame||liveFrame==null||liveVisualOpacity<=0||w<=0||h<=0)return;
        double fw=liveFrame.Size.Width,fh=liveFrame.Size.Height,scale=Math.Max(w/fw,h/fh),sw=w/scale,sh=h/scale;
        session.DrawImage(liveFrame,new Rect(0,0,w,h),new Rect((fw-sw)/2,(fh-sh)/2,sw,sh),(float)liveVisualOpacity);
    }
    async Task SnapshotLiveForTest(){
        if(!testControl||liveCanvas==null||!System.IO.File.Exists(Path.Combine(Storage.Data,".cleartone-test")))return;
        using var target=new CanvasRenderTarget(liveCanvas,640,360,96);
        using(var ds=target.CreateDrawingSession()){ds.Clear(Microsoft.UI.Colors.Transparent);DrawLiveImage(ds,640,360);}
        await target.SaveAsync(Path.Combine(Storage.Data,"live-frame.png"),CanvasBitmapFileFormat.Png);
    }
    static double LiveEase(double value){double t=Math.Clamp(value,0,1);return t*t*(3-2*t);}
    bool HoldLiveEnding(){if(!LiveLocked)return false;liveSongEnded=true;TickLiveFinale();return true;}
    void FailLiveFinale(Exception e){bool advance=liveSongEnded;Storage.Log(e);ResetLiveFinale();liveFailed=true;
        if(!disposed&&audio.ExclusiveOutput!=state.ExclusiveOutput)_=RestoreLiveOutput();
        if(advance&&!disposed)_=Next(1);
    }
    async Task RestoreLiveOutput(){try{await audio.SelectOutput(state.OutputDeviceId,state.ExclusiveOutput).WaitAsync(TimeSpan.FromSeconds(8));}catch(Exception restore){Storage.Log(restore);}}
    void ResetLiveFinale(){
        liveTimer.Stop();if(livePopup!=null){livePopup.IsOpen=false;livePopup.Child=null;}
        var player=livePlayer;livePlayer=null;
        try{player?.Pause();if(player!=null)player.Source=null;liveSource?.Dispose();player?.Dispose();}catch(Exception e){Storage.Log(e);}
        liveFrame?.Dispose();liveFrame=null;liveCanvas?.RemoveFromVisualTree();liveCanvas=null;liveHasFrame=false;liveVisualOpacity=0;liveFramePending=0;
        liveSource=null;livePopup=null;liveSurface=null;Root.IsHitTestVisible=true;
        audio.Volume(state.Volume);
        liveReady=liveStarted=liveStarting=liveSongEnded=liveVideoEnded=liveCompleting=liveFailed=liveRequested=false;
        liveEndWall=-1;liveLastPosition=0;liveEntrance=0;
    }
}




