using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Foundation;
using System.Numerics;
namespace ClearTone;

public sealed partial class MainPage
{
    VideoPlaybackWorker? videoDecoder;readonly List<VideoPlaybackWorker> retiredVideoDecoders=[];CanvasControl? videoCanvas;
    readonly DispatcherTimer videoTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    int videoFrames;bool videoReady,videoFailed,videoHasFrame,videoSeekPending,videoRequestedPlaying;double videoLastFrameWall,videoPosition,videoWatchStart,videoOpenWall;int videoDownloadFailures;string videoLastDownloadFailure="",videoPlaybackState="None";
    Rect? videoMorphOrigin;bool videoMorphFromVideo;int videoMorphs;
    void InitializeVideoPlayback(){
        if(videoCanvas!=null||coverBox==null)return;
        videoCanvas=new CanvasControl{ClearColor=Colors.Transparent,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
        videoCanvas.Draw+=DrawVideo;
        videoCanvas.CreateResources+=(_,_)=>{videoHasFrame=false;};
        var host=new Grid();coverBox.Child=null;host.Children.Add(cover);host.Children.Add(videoCanvas);coverBox.Child=host;
        videoTimer.Tick+=(_,_)=>SyncVideo();
    }
    void CaptureVideoMorph(){
        videoMorphOrigin=coverBox is {ActualWidth:>0,ActualHeight:>0}?coverBox.TransformToVisual(Root).TransformBounds(new Rect(0,0,coverBox.ActualWidth,coverBox.ActualHeight)):null;
        videoMorphFromVideo=current?.IsVideo??false;
    }
    void SetVideoControls(bool video){
        if(recordStack==null)return;
        // Keep the shared transport but give a video its screen space. Restore all
        // dimensions before ordinary music layout, including compact-window sizing.
        title.FontSize=video?19:26;artist.FontSize=video?12:14;artist.Height=video?17:double.NaN;
        artist.TextWrapping=TextWrapping.NoWrap;artist.TextTrimming=TextTrimming.CharacterEllipsis;
        seek.Height=video?40:double.NaN;seek.MinHeight=40;
        ((FrameworkElement)recordStack.Children[1]).Height=video?26:double.NaN;
        ((FrameworkElement)recordStack.Children[4]).Height=video?14:double.NaN;
        var transport=(StackPanel)recordStack.Children[5];transport.Spacing=video?21:26;
        foreach(var child in transport.Children.OfType<Button>()){
            child.Width=child.Height=video?(ReferenceEquals(child,play)?38:32):(ReferenceEquals(child,play)?58:40);
            if(child.Content is FontIcon icon)icon.FontSize=video?17:19;
        }
        play.CornerRadius=new CornerRadius(video?19:29);
        var libraryRow=(Grid)recordStack.Children[6];libraryRow.Height=video?30:double.NaN;
        foreach(var button in libraryRow.Children.OfType<Button>())button.Width=button.Height=video?28:34;
        volume.Height=video?26:double.NaN;
        if(video){
            ((FrameworkElement)recordStack.Children[1]).Margin=new Thickness(0,9,0,0);
            seek.Margin=new Thickness(0,5,0,0);transport.Margin=new Thickness(0,3,0,0);
        }
    }
    void ApplyVideoLayout(){
        if(current?.IsVideo!=true||coverBox==null)return;
        // One centred media surface. Only the body changes; the focus/footer shell
        // remains exactly where it is for music, video and catalogue navigation.
        SetVideoControls(true);
        double widthLimit=Math.Clamp(ActualWidth-100,280,1080),controlsHeight=0;
        if(recordStack!=null)foreach(var child in recordStack.Children.Skip(1).OfType<FrameworkElement>()){
            child.Measure(new Size(widthLimit,double.PositiveInfinity));controlsHeight+=child.DesiredSize.Height;
        }
        double height=Math.Max(100,ActualHeight-header.Height-footer.Height-body.Margin.Top-body.Margin.Bottom-controlsHeight);
        double width=Math.Min(widthLimit,height*16/9);
        body.Width=left.Width=width;body.ColumnSpacing=0;
        body.ColumnDefinitions[0].Width=new GridLength(width);body.ColumnDefinitions[1].Width=new GridLength(0);
        right.Visibility=Visibility.Collapsed;left.RenderTransform=new ScaleTransform();
        coverBox.Width=width;coverBox.Height=width*9/16;title.MaxWidth=Math.Max(120,width-136);
        if(recordStack!=null){left.Height=double.NaN;recordStack.Measure(new Size(width,double.PositiveInfinity));left.Height=recordStack.DesiredSize.Height;}
    }
    void BeginVideoMorph(int version){
        if(version!=loadVersion||disposed)return;Layout();if(coverBox==null||videoMorphOrigin is not Rect before||videoMorphFromVideo==(current?.IsVideo??false)||!ShellMotionEnabled||page!="now")return;
        UpdateLayout();var targetSpace=coverBox.Parent as UIElement??Root;before=Root.TransformToVisual(targetSpace).TransformBounds(before);var after=coverBox.TransformToVisual(targetSpace).TransformBounds(new Rect(0,0,coverBox.ActualWidth,coverBox.ActualHeight));
        if(after.Width<=0||after.Height<=0)return;
        ElementCompositionPreview.SetIsTranslationEnabled(coverBox,true);
        var visual=ElementCompositionPreview.GetElementVisual(coverBox);var compositor=visual.Compositor;
        visual.StopAnimation("Scale");visual.StopAnimation("Translation");
        var scale=compositor.CreateVector3KeyFrameAnimation();scale.InsertKeyFrame(0,new Vector3((float)(before.Width/after.Width),(float)(before.Height/after.Height),1));scale.InsertKeyFrame(1,Vector3.One);scale.Duration=TimeSpan.FromMilliseconds(520);
        var move=compositor.CreateVector3KeyFrameAnimation();move.InsertKeyFrame(0,new Vector3((float)(before.X-after.X),(float)(before.Y-after.Y),0));move.InsertKeyFrame(1,Vector3.Zero);move.Duration=scale.Duration;
        visual.StartAnimation("Scale",scale);visual.StartAnimation("Translation",move);videoMorphs++;
        if(current?.IsVideo!=true){var lyricVisual=ElementCompositionPreview.GetElementVisual(right);var fade=compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(1,1);fade.Duration=TimeSpan.FromMilliseconds(450);lyricVisual.StartAnimation("Opacity",fade);}
    }
    void PrepareVideo(Track track,int version,int attempt=0){
        if(!track.IsVideo||string.IsNullOrEmpty(track.VideoUrl))return;
        InitializeVideoPlayback();retiredVideoDecoders.RemoveAll(d=>d.Finished);
        if(retiredVideoDecoders.Count>=3){FailVideo(new IOException("视频仍在退出，音频继续播放。"));return;}
        if(videoCanvas!=null){var visual=ElementCompositionPreview.GetElementVisual(videoCanvas);visual.StopAnimation("Opacity");visual.Opacity=0;videoCanvas.Visibility=Visibility.Visible;}
        var decoder=new VideoPlaybackWorker(track.VideoUrl,track.VideoAdaptive);videoDecoder=decoder;
        decoder.Failed+=error=>DispatcherQueue.TryEnqueue(()=>{
            if(disposed||!ReferenceEquals(videoDecoder,decoder)||loadVersion!=version)return;
            if(attempt==0&&(error.InnerException?.HResult==unchecked((int)0xC00D0035)||error.Message.Contains("NetworkError"))){_=RetryVideo(track,version,decoder);return;}
            Storage.Log(error);FailVideo(new IOException("视频画面暂不可用，音频继续播放。"));
        });
        videoOpenWall=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;decoder.Start();videoTimer.Start();SyncVideo();
    }
    async Task RetryVideo(Track track,int version,VideoPlaybackWorker failed){
        try{
            await Task.Delay(700,trackCancel.Token);
            if(disposed||version!=loadVersion||!ReferenceEquals(videoDecoder,failed))return;
            ResetVideo();PrepareVideo(track,version,1);
        }catch(OperationCanceledException){}catch(Exception error){if(!disposed&&version==loadVersion)FailVideo(error);}
    }
    void SyncVideo(bool force=false){
        var decoder=videoDecoder;if(disposed||decoder==null||videoFailed)return;
        bool visible=page=="now"&&(App.Main?.IsVisibleForAnimation??true)&&!LiveLocked;
        bool playing=trackReady&&audio.Playing&&visible;
        decoder.Update(visible&&trackReady,playing,audio.PresentationPosition,audio.Speed,force);
        var state=decoder.Snapshot;videoReady=state.Ready;videoPosition=state.Position;videoPlaybackState=state.Playback;videoSeekPending=state.Seeking;
        videoDownloadFailures=state.DownloadFailures;videoLastDownloadFailure=state.DownloadFailure;videoLastFrameWall=state.FrameWall;
        int prior=videoFrames;videoFrames=state.Frames;
        double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        if(!state.Ready&&now-videoOpenWall>25){FailVideo(new IOException("视频连接超时，音频继续播放。"));return;}
        if(playing&&(!videoRequestedPlaying||force))videoWatchStart=now;videoRequestedPlaying=playing;
        if(playing&&state.WorkerWall>0&&now-Math.Max(state.WorkerWall,videoWatchStart)>10){FailVideo(new IOException("视频网络定位超时，音频继续播放。"));return;}
        if(visible&&(prior!=videoFrames||force))videoCanvas?.Invalidate();
        if(playing||!state.Ready){if(!videoTimer.IsEnabled)videoTimer.Start();}else videoTimer.Stop();
    }
    void DrawVideo(CanvasControl sender,CanvasDrawEventArgs args){
        var decoder=videoDecoder;if(decoder==null||videoFailed)return;
        try{
            args.DrawingSession.Clear(Colors.Black);
            if(decoder.Draw(args.DrawingSession,sender.ActualWidth,sender.ActualHeight)&&!videoHasFrame){
                if(ShellMotionEnabled)FadeTrackElement(sender,1,260,true);else ElementCompositionPreview.GetElementVisual(sender).Opacity=1;
                videoHasFrame=true;
            }
        }catch(Exception error){DispatcherQueue.TryEnqueue(()=>{if(!disposed&&ReferenceEquals(videoDecoder,decoder))FailVideo(error);});}
    }
    async Task SnapshotVideoForTest(){
        if(!testControl||videoCanvas==null||videoDecoder==null||!videoHasFrame||!System.IO.File.Exists(Path.Combine(Storage.Data,".cleartone-test")))return;
        using var target=new CanvasRenderTarget(videoCanvas,640,360,96);
        using(var draw=target.CreateDrawingSession()){draw.Clear(Colors.Black);videoDecoder.Draw(draw,640,360);}
        await target.SaveAsync(Path.Combine(Storage.Data,"video-frame.png"),CanvasBitmapFileFormat.Png);
    }
    void FailVideo(Exception error){if(videoFailed)return;ResetVideo();videoFailed=true;ShowError(error);}
    void ResetVideo(){
        videoTimer.Stop();var previous=videoDecoder;videoDecoder=null;
        if(previous!=null){previous.ReleaseView();previous.Dispose();retiredVideoDecoders.Add(previous);}retiredVideoDecoders.RemoveAll(d=>d.Finished);
        videoReady=videoHasFrame=videoFailed=videoSeekPending=videoRequestedPlaying=false;videoFrames=videoDownloadFailures=0;videoPosition=videoLastFrameWall=videoWatchStart=videoOpenWall=0;videoLastDownloadFailure="";videoPlaybackState="None";
        if(videoCanvas!=null){videoCanvas.Visibility=Visibility.Collapsed;videoCanvas.Invalidate();}
    }
    void CloseVideo(){ResetVideo();videoCanvas?.RemoveFromVisualTree();videoCanvas=null;}
}
