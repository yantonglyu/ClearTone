using Windows.Media;
using Windows.Storage.Streams;
namespace ClearTone;

public sealed partial class MainPage
{
    SystemMediaTransportControls? systemMedia;
    string systemMediaTrack="";
    bool systemMediaBusy;
    MediaPlaybackStatus? systemMediaStatus;
    double systemMediaPosition=-1,systemMediaDuration=-1;
    string systemMediaTestResult="not-run";
    async Task TestSystemMediaSession(){
        if(!testControl||!File.Exists(Path.Combine(Storage.Data,".cleartone-test")))return;
        var manager=await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions=manager.GetSessions();
        foreach(var session in sessions){
            if(!session.SourceAppUserModelId.Contains("ClearTone",StringComparison.OrdinalIgnoreCase))continue;
            var metadata=await session.TryGetMediaPropertiesAsync();if(metadata.Title!=current?.Name)continue;
            bool played=await session.TryPlayAsync();await Task.Delay(350);bool wasPlaying=audio.Playing;
            bool paused=await session.TryPauseAsync();await Task.Delay(350);
            systemMediaTestResult=played&&paused&&wasPlaying&&!audio.Playing?"passed":"failed";return;
        }
        systemMediaTestResult="session-not-found";
    }
    void InitializeSystemMedia(){
        try{
            systemMedia=SystemMediaTransportControlsInterop.GetForWindow(WinRT.Interop.WindowNative.GetWindowHandle(App.Main));
            systemMedia.IsEnabled=true;systemMedia.IsPlayEnabled=true;systemMedia.IsPauseEnabled=true;
            systemMedia.IsNextEnabled=true;systemMedia.IsPreviousEnabled=true;
            systemMedia.ButtonPressed+=SystemMediaButton;systemMedia.PlaybackPositionChangeRequested+=SystemMediaSeek;
            UpdateSystemMedia();
        }catch(Exception e){Storage.Log(e);systemMedia=null;}
    }
    void SystemMediaButton(SystemMediaTransportControls sender,SystemMediaTransportControlsButtonPressedEventArgs args){
        DispatcherQueue.TryEnqueue(async()=>{
            if(disposed||systemMediaBusy)return;systemMediaBusy=true;
            try{switch(args.Button){
                case SystemMediaTransportControlsButton.Play:PlayAudio();break;
                case SystemMediaTransportControlsButton.Pause:audio.Pause();break;
                case SystemMediaTransportControlsButton.Next:await Next(1);break;
                case SystemMediaTransportControlsButton.Previous:await Next(-1);break;
            }UpdatePlay();UpdateSystemMedia();}catch(Exception e){ShowError(e);}finally{systemMediaBusy=false;}
        });
    }
    void SystemMediaSeek(SystemMediaTransportControls sender,PlaybackPositionChangeRequestedEventArgs args){
        DispatcherQueue.TryEnqueue(()=>{if(disposed)return;SeekAudio(args.RequestedPlaybackPosition.TotalSeconds);UpdateSystemMedia();});
    }
    void UpdateSystemMedia(){
        if(systemMedia==null||disposed)return;
        try{
            systemMedia.IsPlayEnabled=trackReady;
            var status=current==null?MediaPlaybackStatus.Closed:trackLoading?MediaPlaybackStatus.Changing:!trackReady?MediaPlaybackStatus.Stopped:audio.Playing?MediaPlaybackStatus.Playing:MediaPlaybackStatus.Paused;
            if(systemMediaStatus!=status){systemMedia.PlaybackStatus=status;systemMediaStatus=status;}
            string track=current?.Key??"";
            if(systemMediaTrack!=track){
                systemMediaTrack=track;var display=systemMedia.DisplayUpdater;display.ClearAll();display.Type=MediaPlaybackType.Music;
                display.MusicProperties.Title=current?.Name??"澄音";display.MusicProperties.Artist=current?.Artist??"";
                if(Uri.TryCreate(current?.Cover,UriKind.Absolute,out var coverUri)&&coverUri.Scheme=="https")display.Thumbnail=RandomAccessStreamReference.CreateFromUri(coverUri);
                display.Update();systemMediaPosition=-1;systemMediaDuration=-1;
            }
            double position=audio.Position,duration=audio.Duration;
            if(Math.Abs(position-systemMediaPosition)>=.9||duration!=systemMediaDuration){
                systemMediaPosition=position;systemMediaDuration=duration;var end=TimeSpan.FromSeconds(Math.Max(0,duration));
                systemMedia.UpdateTimelineProperties(new(){StartTime=TimeSpan.Zero,MinSeekTime=TimeSpan.Zero,EndTime=end,MaxSeekTime=end,Position=TimeSpan.FromSeconds(Math.Clamp(position,0,Math.Max(0,duration)))});
            }
            if(systemMedia.PlaybackRate!=audio.Speed)systemMedia.PlaybackRate=audio.Speed;
        }catch(Exception e){Storage.Log(e);CloseSystemMedia();}
    }
    void CloseSystemMedia(){
        if(systemMedia==null)return;
        try{systemMedia.ButtonPressed-=SystemMediaButton;systemMedia.PlaybackPositionChangeRequested-=SystemMediaSeek;systemMedia.PlaybackStatus=MediaPlaybackStatus.Closed;systemMedia.IsEnabled=false;}catch{}systemMedia=null;
    }
}


