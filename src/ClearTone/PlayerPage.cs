using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.UI;
using Windows.UI;
using System.Numerics;
using System.Text.Json;
using System.Net.Http.Json;
using Windows.Storage.Streams;
namespace ClearTone;

public sealed partial class MainPage
{
    readonly AppState state=Storage.Load();readonly MusicService service=new();readonly AudioEngine audio=new();readonly StudyClock study;
    readonly ListeningSync listening;string accountId="";
    readonly Image cover=new(){Stretch=Stretch.UniformToFill},background=new(){Stretch=Stretch.UniformToFill,Opacity=.95};
    readonly TextBlock title=Text("澄音",25),artist=Text("让音乐陪你专注",13,.7),clockText=Text("",12),positionText=Text("0:00",11,.6),durationText=Text("0:00",11,.6),notice=Text("",11,.45),quote=Text("把这一刻，留给正在做的事。",13,.8),quoteSource=Text("",11,.45),studyTime=Text("00:00:00",24),studyStatus=Text("本次专注",11,.6),readings=Text("—   ·   —   ·   —",12,.6);
    readonly Slider seek=new(){Minimum=0,Maximum=1,StepFrequency=.1},volume=new(){Minimum=0,Maximum=1,StepFrequency=.01,Width=85};
    readonly Button follow=Icon("\uE72A","回到当前歌词");
    readonly Button play=Icon("\uE768","播放"),like=Icon("\uEB51","喜欢"),studyButton=new(){Content="开始专注"},translation=Icon("\uF2B7","翻译"),roman=Icon("\uE8D2","罗马音");
    readonly CanvasControl lyricCanvas=new(){ClearColor=Colors.Transparent},spectrumCanvas=new(){Height=48,Width=340,ClearColor=Colors.Transparent};
    readonly Grid body=new(),footer=new(),left=new(),right=new(),header=new();Border? coverBox;StackPanel? recordStack;readonly DispatcherTimer uiTimer=new(){Interval=TimeSpan.FromMilliseconds(50)},saveTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    List<Lyric> lyrics=[];Track? current;JsonElement? timeline;int active=-1,loadVersion;bool ready,dragging,analyzing,disposed,accountLogged,analysisReady;float[] spectrum=new float[48];double scrollIndex=-1;DateTime lastAnalysis=DateTime.MinValue;
    readonly Dictionary<string,CanvasRenderTarget> lyricTextures=new();readonly List<float> lyricPositions=[],lyricHeights=[],mainHeights=[],translationHeights=[];double bpm,lastBeatTime,priorEnergy,energyMean;readonly Queue<double> intervals=new();bool beatHit;int ticks;CancellationTokenSource trackCancel=new();
    static TextBlock Text(string value,double size=14,double opacity=1)=>new(){Text=value,FontSize=size,Foreground=new SolidColorBrush(Colors.White),Opacity=opacity,TextWrapping=TextWrapping.Wrap};
    static void SetText(TextBlock block,string text){if(block.Text!=text)block.Text=text;}
    static Button Icon(string glyph,string help){var b=new Button{Content=new FontIcon{Glyph=glyph,FontSize=19},Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0),Width=40,Height=40};ToolTipService.SetToolTip(b,help);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,help);return b;}
    static StackPanel Row(params UIElement[] items){var s=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};foreach(var i in items)s.Children.Add(i);return s;}
    static StackPanel Column(params UIElement[] items){var s=new StackPanel{Spacing=8};foreach(var i in items)s.Children.Add(i);return s;}
    public MainPage(){
        InitializeComponent();study=new StudyClock(state);listening=new(Path.Combine(Storage.Data,"listening-outbox.json"),async r=>{var d=await service.Call("listening-report",r);return d.GetProperty("state").GetString()??"pending";});Build();
        Loaded+=async(_,_)=>{try{await service.Start();ready=true;await LoadAccount();await RefreshQuote();if(state.Queue.Count>0)await LoadTrack(Math.Clamp(state.Index,0,state.Queue.Count-1),false);else if(accountLogged&&(state.Daily||state.PlaylistId.Length>0)){var d=await service.Call(state.Daily?"daily":"playlist",new{id=state.PlaylistId});state.Queue=MusicService.Tracks(d);if(state.Queue.Count>0)await LoadTrack(0,false);}}catch(Exception e){ShowError(e);}uiTimer.Start();saveTimer.Start();};
        uiTimer.Tick+=(_,_)=>Tick();saveTimer.Tick+=(_,_)=>{if(App.Main?.IsVisibleForAnimation??true){SetText(clockText,DateTime.Now.ToString("HH:mm"));SetText(studyTime,StudyClock.Format(study.Seconds));SetText(studyStatus,study.Running?"正在专注":study.Resting?"休息中":"本次专注");string label=study.Running?"Ⅱ 休息一下":study.Resting?"▷ 继续专注":"▷ 开始专注";if(studyButton.Content as string!=label)studyButton.Content=label;}if(study.Running)study.Checkpoint();_=TestControl();};
        saveTimer.Tick+=(_,_)=>UpdateListening();
        audio.Ended+=()=>DispatcherQueue.TryEnqueue(()=>_=Next(1));SizeChanged+=(_,_)=>Layout();
    }
    void Build(){
        foreach(var key in new[]{"SliderTrackValueFill","SliderTrackValueFillPointerOver","SliderTrackValueFillPressed","SliderThumbBackground","SliderThumbBackgroundPointerOver","SliderThumbBackgroundPressed"})Root.Resources[key]=new SolidColorBrush(Color.FromArgb(255,224,219,236));
        Root.Background=new SolidColorBrush(Color.FromArgb(255,19,22,31));Root.Children.Add(background);Root.Children.Add(new Grid{Background=new SolidColorBrush(Color.FromArgb(28,8,10,18)),IsHitTestVisible=false});
        var layout=new Grid{Padding=new Thickness(0),RowSpacing=0};layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});Root.Children.Add(layout);
        header.Height=88;header.Margin=new Thickness(40,0,40,0);var settings=Icon("\uE713","设置");settings.Click+=async(_,_)=>await Settings();var full=Icon("\uE740","全屏");full.Click+=(_,_)=>App.Main?.ToggleFullscreen();
        var headerTools=Row(clockText,settings,full);headerTools.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(headerTools);layout.Children.Add(header);
        body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1.25,GridUnitType.Star)});body.ColumnSpacing=70;body.Margin=new Thickness(70,10,70,0);layout.Children.Add(body);Grid.SetRow(body,1);
        left.HorizontalAlignment=HorizontalAlignment.Center;left.VerticalAlignment=VerticalAlignment.Center;left.Width=360;
        coverBox=new Border{CornerRadius=new CornerRadius(12),Child=cover,Height=240,Width=240,Background=new SolidColorBrush(Color.FromArgb(80,120,120,130))};
        var collect=Icon("\uE710","收藏进歌单");collect.Click+=async(_,_)=>await Collect();like.Click+=async(_,_)=>await ToggleLike();var queue=Icon("\uE8FD","播放列表");queue.Click+=async(_,_)=>await Queue();
        var heading=new Grid();heading.Children.Add(title);title.MaxWidth=220;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.HorizontalAlignment=HorizontalAlignment.Left;var songButtons=Row(like,collect,queue);songButtons.Spacing=0;songButtons.HorizontalAlignment=HorizontalAlignment.Right;heading.Children.Add(songButtons);
        var times=new Grid();times.Children.Add(positionText);times.Children.Add(durationText);durationText.HorizontalAlignment=HorizontalAlignment.Right;
        var previous=Icon("\uE892","上一首");previous.Click+=async(_,_)=>await Next(-1);var next=Icon("\uE893","下一首");next.Click+=async(_,_)=>await Next(1);
        var shuffle=Icon("\uE8B1","随机播放");shuffle.Opacity=state.Shuffle?1:.5;shuffle.Click+=(_,_)=>{state.Shuffle=!state.Shuffle;shuffle.Opacity=state.Shuffle?1:.5;Storage.Save(state);};
        var repeat=Icon("\uE8EE","单曲循环");repeat.Opacity=state.RepeatOne?1:.5;repeat.Click+=(_,_)=>{state.RepeatOne=!state.RepeatOne;repeat.Opacity=state.RepeatOne?1:.5;Storage.Save(state);};
        play.Width=58;play.Height=58;play.CornerRadius=new CornerRadius(29);play.Background=new SolidColorBrush(Color.FromArgb(24,255,255,255));play.Click+=(_,_)=>{if(audio.Playing)audio.Pause();else audio.Play();UpdatePlay();};
        var transport=Row(shuffle,previous,play,next,repeat);transport.Spacing=26;transport.HorizontalAlignment=HorizontalAlignment.Center;
        var library=Icon("\uE8D6","我的网易云");library.Width=34;library.Height=34;library.Padding=new Thickness(0);library.Opacity=.72;library.HorizontalAlignment=HorizontalAlignment.Left;
        library.Content=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(12),BorderBrush=new SolidColorBrush(Colors.White),BorderThickness=new Thickness(1.3),Child=new TextBlock{Text="♪",FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        library.Click+=async(_,_)=>await Library();
        volume.Value=state.Volume;volume.ValueChanged+=(_,e)=>{state.Volume=e.NewValue;audio.Volume(e.NewValue);};var libraryRow=new Grid();libraryRow.Children.Add(library);var v=Row(Text("♪",14,.6),volume);v.HorizontalAlignment=HorizontalAlignment.Right;libraryRow.Children.Add(v);
        seek.AddHandler(PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>dragging=true),true);seek.AddHandler(PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>{if(dragging){audio.Seek(seek.Value);dragging=false;}}),true);seek.KeyUp+=(_,_)=>audio.Seek(seek.Value);
        recordStack=Column(coverBox,heading,artist,seek,times,transport,libraryRow,notice);recordStack.Spacing=7;left.Children.Add(recordStack);body.Children.Add(left);
        right.RowDefinitions.Add(new(){Height=new GridLength(42)});right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Grid.SetRow(lyricCanvas,1);right.Children.Add(lyricCanvas);right.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(right,1);body.Children.Add(right);follow.Visibility=Visibility.Collapsed;follow.Width=32;follow.Height=32;follow.Click+=(_,_)=>ReturnToLyrics();var tools=Row(follow,translation,roman);tools.VerticalAlignment=VerticalAlignment.Top;tools.HorizontalAlignment=HorizontalAlignment.Right;translation.Opacity=roman.Opacity=.45;translation.Width=roman.Width=32;translation.Height=roman.Height=32;right.Children.Add(tools);
        translation.Click+=(_,_)=>{state.Translation=!state.Translation;ClearLyricTextures();Storage.Save(state);lyricCanvas.Invalidate();};roman.Click+=(_,_)=>{state.Romanization=!state.Romanization;ClearLyricTextures();Storage.Save(state);lyricCanvas.Invalidate();};
        lyricCanvas.Draw+=DrawLyrics;SetupLyricInteraction();
        spectrumCanvas.Draw+=(_,e)=>{for(int i=0;i<48;i++){float h=audio.Playing?2+spectrum[i]*40:2;e.DrawingSession.FillRoundedRectangle(i*7,44-h,4,h,2,2,Color.FromArgb(105,249,248,245));}};
        footer.Height=126;footer.Padding=new Thickness(48,24,48,24);footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var refresh=Icon("\uE72C","更新一言");refresh.Width=22;refresh.Height=22;refresh.Padding=new Thickness(0);refresh.Opacity=.5;((FontIcon)refresh.Content).FontSize=12;refresh.Click+=async(_,_)=>await RefreshQuote();var qs=Column(quote,Row(quoteSource,refresh));qs.MaxWidth=400;qs.HorizontalAlignment=HorizontalAlignment.Left;qs.VerticalAlignment=VerticalAlignment.Bottom;footer.Children.Add(qs);
        var visual=Column(spectrumCanvas,readings);readings.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(visual,1);footer.Children.Add(visual);
        studyButton.Click+=(_,_)=>study.Toggle();studyButton.Height=34;studyButton.Padding=new Thickness(13,0,13,0);studyButton.FontSize=12;studyButton.CornerRadius=new CornerRadius(9);studyButton.Background=new SolidColorBrush(Color.FromArgb(18,255,255,255));studyButton.BorderThickness=new Thickness(0);studyButton.VerticalAlignment=VerticalAlignment.Center;
        var timer=Column(studyStatus,Row(studyTime,studyButton));timer.Spacing=3;((StackPanel)timer.Children[1]).Spacing=18;timer.HorizontalAlignment=HorizontalAlignment.Right;timer.VerticalAlignment=VerticalAlignment.Bottom;Grid.SetColumn(timer,2);footer.Children.Add(timer);Grid.SetRow(footer,2);layout.Children.Add(footer);
    }
    void Layout(){
        bool compact=ActualHeight<=840,narrow=ActualWidth<=1050;
        header.Height=compact?76:88;footer.Height=compact?112:126;footer.Padding=compact?new Thickness(40,20,40,20):new Thickness(48,24,48,24);
        studyTime.FontFamily=new FontFamily("Cascadia Mono");studyTime.FontSize=29;
        double workspace=Math.Min(compact?1080:1180,ActualWidth*(narrow?.88:compact?.86:.87));
        double gap=narrow?ActualWidth*.06:compact?ActualWidth*.09:Math.Clamp(ActualWidth*.08,60,140);
        double record=compact||narrow?330:420;
        if(ActualWidth<=820){workspace=Math.Max(500,ActualWidth-44);record=Math.Min(290,workspace*.43);gap=24;}
        body.Width=workspace;body.HorizontalAlignment=HorizontalAlignment.Center;body.Margin=new Thickness(0,compact?14:18,0,compact?18:30);body.ColumnSpacing=gap;
        body.ColumnDefinitions[0].Width=new GridLength(record);body.ColumnDefinitions[1].Width=new GridLength(1,GridUnitType.Star);
        left.Width=record;title.MaxWidth=Math.Max(120,record-130);left.RenderTransformOrigin=new Windows.Foundation.Point(.5,.5);left.RenderTransform=new ScaleTransform{ScaleX=.88,ScaleY=.88};
        right.Height=Math.Max(120,Math.Min(600,ActualHeight-(compact?188:214)-(compact?32:48)));
        if(coverBox!=null){double size=compact?Math.Max(100,Math.Min(330,ActualHeight-460)):record;coverBox.Height=coverBox.Width=size;}
        if(recordStack!=null){recordStack.Spacing=0;title.FontSize=26;artist.FontSize=14;
            recordStack.Children[1].SetValue(MarginProperty,new Thickness(0,compact?20:26,0,6));
            seek.Margin=new Thickness(0,compact?17:24,0,0);
            recordStack.Children[5].SetValue(MarginProperty,new Thickness(0,compact?8:14,0,compact?7:16));}
        bool instrumental=current!=null&&lyrics.Count==0;
        right.Visibility=instrumental?Visibility.Collapsed:Visibility.Visible;
        if(instrumental){body.Width=record;body.ColumnSpacing=0;body.ColumnDefinitions[1].Width=new GridLength(0);}
        Root.Clip=new RectangleGeometry{Rect=new Windows.Foundation.Rect(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight))};
        ClearLyricTextures();lyricCanvas.Invalidate();SetMotion();
    }
    void ShowError(Exception e){Storage.Log(e);notice.Text=e.Message;_=DismissNotice(e.Message);}
    async Task DismissNotice(string text){await Task.Delay(7000);if(notice.Text==text)notice.Text="";}
    bool? lastPlaying;
    void UpdatePlay(){bool playing=audio.Playing;if(lastPlaying==playing)return;lastPlaying=playing;((FontIcon)play.Content).Glyph=playing?"\uE769":"\uE768";if(!playing)spectrumCanvas.Invalidate();}
    async Task Next(int d){if(state.Queue.Count==0)return;int i=state.RepeatOne?state.Index:state.Shuffle?Random.Shared.Next(state.Queue.Count):(state.Index+d+state.Queue.Count)%state.Queue.Count;await LoadTrack(i,true);}
    async Task LoadTrack(int index,bool start){
        audio.Pause();EndListening();
        int version=++loadVersion;trackCancel.Cancel();trackCancel.Dispose();trackCancel=new();var token=trackCancel.Token;
        try{
            audio.Pause();state.Index=index;current=state.Queue[index];timeline=null;analysisReady=false;active=-1;scrollIndex=-1;lyricManualUntil=DateTime.MinValue;lyricPressed=false;intervals.Clear();bpm=0;lastAnalysis=DateTime.MinValue;title.Text=current.Name;ToolTipService.SetToolTip(title,current.Name);artist.Text=current.Artist;notice.Text="";lyrics=[];ClearLyricTextures();lyricCanvas.Invalidate();
            var t=await service.Refresh(current);if(version!=loadVersion)return;current=t;state.Queue[index]=t;Storage.Save(state);
            _=LoadCover(t.Cover,version);_=LoadLyrics(t,version,token);_=UpdateLike(version);
            await audio.Load(t.Url,state);if(version!=loadVersion)return;listening.Begin(accountId,t.NetEaseId,state.PlaylistId);seek.Maximum=Math.Max(1,audio.Duration);durationText.Text=ShortTime(audio.Duration);if(start)audio.Play();UpdatePlay();_=Analyze(version);
            if(t.NetEaseId.Length>0){var rights=await service.Call("track",new{id=t.NetEaseId},token);if(version==loadVersion&&rights.TryGetProperty("trial",out var trial)&&trial.GetBoolean())notice.Text="当前账号仅可试听";}
        }catch(OperationCanceledException){}catch(Exception e){if(version==loadVersion)ShowError(e);}
    }
    async Task LoadLyrics(Track t,int version,CancellationToken token){try{
        if(t.NetEaseId.Length>0){var d=await service.Call("lyrics",new{id=t.NetEaseId},token);if(version!=loadVersion)return;lyrics=Lyrics.Merge(d.GetProperty("lrc").GetString()??"",d.GetProperty("translation").GetString()??"",d.GetProperty("romanization").GetString()??"");}else lyrics=Lyrics.Parse(t.Lrc);
        active=-1;scrollIndex=-1;ClearLyricTextures();Layout();lyricCanvas.Invalidate();
    }catch(OperationCanceledException){}catch(Exception e){ShowError(e);}}
    async Task Analyze(int version){if(analyzing||current==null||current.NetEaseId.Length==0)return;analyzing=true;try{var d=await service.Call("analysis",new{id=current.NetEaseId,queue=audio.Playing,duration=current.FullDuration});if(version==loadVersion){if(d.TryGetProperty("timeline",out var t)&&t.ValueKind==JsonValueKind.Object)timeline=t.Clone();analysisReady=d.GetProperty("state").GetString() is "ready" or "disabled" or "unavailable";}lastAnalysis=DateTime.Now;}catch(Exception e){Storage.Log(e);}finally{analyzing=false;}}
    static string ShortTime(double s)=>$"{(int)s/60}:{(int)s%60:00}";
    void Tick(){
        if(disposed)return;bool visible=App.Main?.IsVisibleForAnimation??true;var interval=TimeSpan.FromMilliseconds(visible&&audio.Playing?50:500);if(uiTimer.Interval!=interval)uiTimer.Interval=interval;
        if(ready&&current!=null&&!analysisReady&&(DateTime.Now-lastAnalysis).TotalSeconds>2)_=Analyze(loadVersion);
        if(!visible){StopLyricAnimation();return;}if(!dragging){double value=Math.Min(seek.Maximum,audio.Position);if(seek.Value!=value)seek.Value=value;}SetText(positionText,ShortTime(audio.Position));UpdatePlay();
        if(audio.Playing){spectrum=audio.Spectrum();spectrumCanvas.Invalidate();}int next=lyrics.FindLastIndex(l=>l.Time<=audio.Position);if(next!=active){active=next;lyricCanvas.Invalidate();}
        if(++ticks%2==0)UpdateReadings();
        WakeLyricAnimation();
    }
    void UpdateReadings(){
        double t=audio.Position;string pitch="—",meter="—";double cachedBpm=0;beatHit=false;
        if(timeline is JsonElement data){
            if(data.TryGetProperty("pitch",out var ps)&&data.TryGetProperty("pitchStep",out var step)){int i=(int)(t/step.GetDouble());if(i>=0&&i<ps.GetArrayLength()&&ps[i].ValueKind==JsonValueKind.Number){double hz=ps[i].GetDouble();if(hz>0){int midi=(int)Math.Round(69+12*Math.Log2(hz/440));string[] names=["C","C♯","D","D♯","E","F","F♯","G","G♯","A","A♯","B"];pitch=names[(midi%12+12)%12]+(midi/12-1);}}}
            if(data.TryGetProperty("tempo",out var tempos))foreach(var seg in tempos.EnumerateArray())if(t>=seg.GetProperty("start").GetDouble()&&t<seg.GetProperty("end").GetDouble()){if(seg.TryGetProperty("bpm",out var value)&&value.ValueKind==JsonValueKind.Number)cachedBpm=value.GetDouble();break;}
            if(data.TryGetProperty("bars",out var bars))foreach(var seg in bars.EnumerateArray())if(t>=seg.GetProperty("start").GetDouble()&&t<seg.GetProperty("end").GetDouble()){if(seg.TryGetProperty("count",out var count)&&count.ValueKind==JsonValueKind.Number)meter=count.ToString()+"/"+(seg.TryGetProperty("denominator",out var den)&&den.ValueKind==JsonValueKind.Number?den.ToString():"?");break;}
            if(data.TryGetProperty("beats",out var beats)){int lo=0,hi=beats.GetArrayLength();while(lo<hi){int m=(lo+hi)/2;if(beats[m].GetDouble()<=t)lo=m+1;else hi=m;}beatHit=lo>0&&t-beats[lo-1].GetDouble()<.13;}
        }
        if(cachedBpm>0)bpm=cachedBpm;else if(audio.Playing){double energy=spectrum.Take(14).Average();energyMean=.92*energyMean+.08*energy;if(energy>energyMean*1.35&&energy>priorEnergy*1.12&&t-lastBeatTime>.28){double interval=t-lastBeatTime;if(interval<1.25&&interval>.28){intervals.Enqueue(interval);while(intervals.Count>24)intervals.Dequeue();if(intervals.Count>=4&&ticks%20<3)bpm=Math.Round(60/intervals.Order().ElementAt(intervals.Count/2));}lastBeatTime=t;beatHit=true;}priorEnergy=energy;}
        SetText(readings,$"{pitch}       {(beatHit?"●":"·")}  {(bpm>0?"≈ "+Math.Round(bpm)+" BPM":"—")}       {(meter=="—"?meter:"≈ "+meter)}");
    }
    void ClearLyricTextures(){foreach(var t in lyricTextures.Values)t.Dispose();lyricTextures.Clear();lyricPositions.Clear();lyricHeights.Clear();mainHeights.Clear();translationHeights.Clear();}
    void DrawLyrics(CanvasControl sender,CanvasDrawEventArgs args){long start=System.Diagnostics.Stopwatch.GetTimestamp();DrawLyricContent(sender,args.DrawingSession);if(testControl){lyricDrawTimes.Enqueue(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);while(lyricDrawTimes.Count>600)lyricDrawTimes.Dequeue();}}
    void DrawLyricContent(CanvasControl sender,CanvasDrawingSession ds){
        if(lyrics.Count==0){ds.DrawText(current==null?"让音乐陪你专注":"纯音乐，静静聆听",new Vector2(20,(float)sender.ActualHeight*.4f),Color.FromArgb(130,255,255,255),new CanvasTextFormat{FontSize=26,FontFamily="Segoe UI"});return;}
        float width=(float)Math.Max(180,sender.ActualWidth-24),center=(float)sender.ActualHeight*.35f;
        float font=(float)(ActualWidth<=820?22.5:ActualWidth<=1050?23.4:Math.Clamp(ActualWidth*.02205,22.5,34.2));
        using var mainFormat=new CanvasTextFormat{FontFamily="Segoe UI Variable",FontSize=font,FontWeight=new Windows.UI.Text.FontWeight{Weight=650},WordWrapping=CanvasWordWrapping.Wrap,LineSpacing=font*1.45f,LineSpacingBaseline=font*1.45f*.8f};
        using var translationFormat=new CanvasTextFormat{FontFamily="Segoe UI Variable",FontSize=font*.52f,FontWeight=new Windows.UI.Text.FontWeight{Weight=450},WordWrapping=CanvasWordWrapping.Wrap,LineSpacing=font*.52f*1.65f,LineSpacingBaseline=font*.52f*1.65f*.8f};
        using var romanFormat=new CanvasTextFormat{FontFamily="Segoe UI Variable",FontSize=font*.46f,FontWeight=new Windows.UI.Text.FontWeight{Weight=450},WordWrapping=CanvasWordWrapping.Wrap,LineSpacing=font*.46f*1.65f,LineSpacingBaseline=font*.46f*1.65f*.8f};
        if(lyricPositions.Count!=lyrics.Count){lyricPositions.Clear();lyricHeights.Clear();mainHeights.Clear();translationHeights.Clear();float y=0;
            foreach(var l in lyrics){using var measure=new CanvasTextLayout(sender,l.Text,mainFormat,width-16,2000);float h=(float)Math.Ceiling(measure.LayoutBounds.Height),th=0,rh=0;
                if(state.Translation&&l.Translation.Length>0){using var m=new CanvasTextLayout(sender,l.Translation,translationFormat,width-16,2000);th=(float)Math.Ceiling(m.LayoutBounds.Height)+7;}
                if(state.Romanization&&l.Romanization.Length>0){using var m=new CanvasTextLayout(sender,l.Romanization,romanFormat,width-16,2000);rh=(float)Math.Ceiling(m.LayoutBounds.Height)+7;}
                lyricPositions.Add(y);mainHeights.Add(h);translationHeights.Add(th);lyricHeights.Add(h+th+rh+34);y+=h+th+rh+34;}}
        int first=Math.Max(0,lyricPositions.FindLastIndex(p=>p<scrollIndex-center)-1);
        int last=Math.Min(lyrics.Count-1,lyricPositions.FindLastIndex(p=>p<scrollIndex-center+sender.ActualHeight)+1);
        for(int i=first;i<=last;i++){
            var l=lyrics[i];bool selected=i==active;bool browsing=lyricPressed||DateTime.Now<lyricManualUntil;int distance=browsing?0:Math.Abs(i-active);string key=$"{i}:{selected}:{Math.Min(distance,3)}:{(int)width}";
            if(!lyricTextures.TryGetValue(key,out var texture)){
                using var raw=new CanvasRenderTarget(sender,width+20,lyricHeights[i]+12,96);using(var r=raw.CreateDrawingSession()){
                    r.Clear(Colors.Transparent);r.DrawText(l.Text,new Windows.Foundation.Rect(8,6,width-16,mainHeights[i]+4),Colors.White,mainFormat);
                    float yText=6+mainHeights[i];
                    if(translationHeights[i]>0){r.DrawText(l.Translation,new Windows.Foundation.Rect(8,yText+7,width-16,translationHeights[i]),Color.FromArgb(220,255,255,255),translationFormat);yText+=translationHeights[i];}
                    if(state.Romanization&&l.Romanization.Length>0)r.DrawText(l.Romanization,new Windows.Foundation.Rect(8,yText+7,width-16,lyricHeights[i]-mainHeights[i]-translationHeights[i]),Color.FromArgb(220,255,255,255),romanFormat);}
                texture=new CanvasRenderTarget(sender,width+20,lyricHeights[i]+12,96);using(var r=texture.CreateDrawingSession()){r.Clear(Colors.Transparent);using var blur=new GaussianBlurEffect{Source=raw,BlurAmount=selected?0:Math.Min(4.5f,distance*1.5f),BorderMode=EffectBorderMode.Soft};r.DrawImage(blur);}lyricTextures[key]=texture;if(lyricTextures.Count>160){var old=lyricTextures.Keys.First(k=>k!=key);lyricTextures[old].Dispose();lyricTextures.Remove(old);}
            }
            float y=center+lyricPositions[i]-(float)scrollIndex;float edge=(float)Math.Clamp(Math.Min(y+lyricHeights[i],sender.ActualHeight-y)/100,0,1);ds.DrawImage(texture,new Vector2(0,y),texture.Bounds,(selected?1f:browsing?.65f:.55f)*edge);
        }
    }
    async Task LoadCover(string url,int version){try{
        using var client=new System.Net.Http.HttpClient();var bytes=url.StartsWith("http")?await client.GetByteArrayAsync(url):await File.ReadAllBytesAsync(url);if(version!=loadVersion)return;
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(bytes);await writer.StoreAsync();}stream.Seek(0);var bmp=new BitmapImage();await bmp.SetSourceAsync(stream);cover.Source=bmp;stream.Seek(0);
        using var image=await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(),stream);using var small=new CanvasRenderTarget(CanvasDevice.GetSharedDevice(),640,400,96);
        var pixels=image.GetPixelColors();var buckets=new Dictionary<int,(long r,long g,long b,int count)>();
        for(int i=0;i<pixels.Length;i+=29){var c=pixels[i];int max=Math.Max(c.R,Math.Max(c.G,c.B)),min=Math.Min(c.R,Math.Min(c.G,c.B));if(max-min<25||max<40||min>210)continue;int key=(c.R/32)*64+(c.G/32)*8+c.B/32;var b=buckets.GetValueOrDefault(key);buckets[key]=(b.r+c.R,b.g+c.G,b.b+c.B,b.count+1);}
        var colors=buckets.Values.OrderByDescending(v=>v.count).Take(3).Select(v=>Color.FromArgb(255,(byte)(v.r/v.count*.95),(byte)(v.g/v.count*.95),(byte)(v.b/v.count*.95))).ToList();
        if(colors.Count==0)colors.Add(Color.FromArgb(255,65,73,100));while(colors.Count<3)colors.Add(colors[0]);
        using(var draw=small.CreateDrawingSession()){
            draw.Clear(Color.FromArgb(255,18,21,31));
            for(int i=0;i<3;i++){var color=colors[i];using var brush=new CanvasRadialGradientBrush(small,color,Color.FromArgb(0,color.R,color.G,color.B)){Center=new Vector2(i==0?80:i==1?490:320,i==0?90:i==1?270:80),RadiusX=260,RadiusY=240};draw.FillRectangle(0,0,640,400,brush);}
        }
        using var target=new CanvasRenderTarget(CanvasDevice.GetSharedDevice(),640,400,96);using(var draw=target.CreateDrawingSession()){using var blur=new GaussianBlurEffect{Source=small,BlurAmount=65,BorderMode=EffectBorderMode.Hard};draw.DrawImage(blur);draw.FillRectangle(0,0,640,400,Color.FromArgb(18,10,12,22));}
        using var result=new InMemoryRandomAccessStream();await target.SaveAsync(result,CanvasBitmapFileFormat.Png);result.Seek(0);var bg=new BitmapImage();await bg.SetSourceAsync(result);if(version!=loadVersion)return;background.Source=bg;SetMotion();
    }catch(Exception e){Storage.Log(e);}}
    bool motionRunning;Vector2 motionSize;
    void SetMotion(){var v=ElementCompositionPreview.GetElementVisual(background);bool run=state.Motion&&(App.Main?.IsVisibleForAnimation??true);var size=new Vector2((float)ActualWidth,(float)ActualHeight);if(run==motionRunning&&size==motionSize)return;motionRunning=run;motionSize=size;v.StopAnimation("Offset");v.Scale=new Vector3(1.18f,1.18f,1);if(!run){v.Offset=new Vector3(-size.X*.06f,-size.Y*.06f,0);return;}var a=v.Compositor.CreateVector3KeyFrameAnimation();a.InsertKeyFrame(0,new Vector3(-size.X*.13f,-size.Y*.03f,0));a.InsertKeyFrame(.5f,new Vector3(-size.X*.015f,-size.Y*.14f,0));a.InsertKeyFrame(1,new Vector3(-size.X*.13f,-size.Y*.03f,0));a.Duration=TimeSpan.FromSeconds(24);a.IterationBehavior=Microsoft.UI.Composition.AnimationIterationBehavior.Forever;v.StartAnimation("Offset",a);}
    public void VisibilityChanged(bool visible){if(!visible&&state.Strict&&study.Running)study.Toggle();SetMotion();}
    void UpdateListening(){try{listening.Checkpoint(audio.PlayedSeconds);if(ready)_=listening.Pump(accountId);}catch(Exception e){Storage.Log(e);}}
    void EndListening(){try{listening.Checkpoint(audio.PlayedSeconds,true);listening.Finish();if(ready)_=listening.Pump(accountId);}catch(Exception e){Storage.Log(e);}}
    public async Task PrepareClose(){audio.Pause();EndListening();try{await listening.Pump(accountId).WaitAsync(TimeSpan.FromSeconds(5));}catch{} }
    public void Close(){CompositionTarget.Rendering-=AnimateLyrics;disposed=true;uiTimer.Stop();saveTimer.Stop();study.Checkpoint();audio.Dispose();service.Stop();ClearLyricTextures();}
    async Task RefreshQuote(){try{using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(8)};var d=await http.GetFromJsonAsync<JsonElement>("https://v1.hitokoto.cn/?max_length=38");quote.Text=d.GetProperty("hitokoto").GetString();quoteSource.Text="一言  "+d.GetProperty("from").GetString();}catch{quoteSource.Text="一言";}}
}
