using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;
using System.Text.Json;
using Windows.Storage.Streams;
namespace ClearTone;
public sealed partial class MainPage
{
    bool liked,likeBusy;MusicSheet? openDialog;
    readonly DispatcherTimer settingsSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    bool settingsSaveHooked;
    void SavePreferencesSoon(){if(!settingsSaveHooked){settingsSaveHooked=true;settingsSaveTimer.Tick+=(_,_)=>{settingsSaveTimer.Stop();Storage.Save(state);};}settingsSaveTimer.Stop();settingsSaveTimer.Start();}
    async Task Dialog(string name,UIElement content){
        if(openDialog!=null)return;
        var d=CreateSheet(name,content);
        openDialog=d;try{await d.ShowAsync();}finally{if(openDialog==d)openDialog=null;}
    }
    async Task LoadAccount(){var d=await service.Call("status");accountLogged=d.GetProperty("loggedIn").GetBoolean();var next=accountLogged?d.GetProperty("profile").GetProperty("id").ToString():"";if(next!=accountId){EndListening();accountId=next;listeningBaseline=audio.PlayedSeconds;if(current?.NetEaseId.Length>0)listening.Begin(accountId,current.NetEaseId,state.PlaylistId);}if(accountLogged)_=listening.Pump(accountId);}
    async Task UpdateLike(int version){try{if(current==null||current.NetEaseId.Length==0){like.IsEnabled=false;return;}like.IsEnabled=true;var d=await service.Call("liked",new{id=current.NetEaseId});if(version!=loadVersion)return;liked=d.GetProperty("liked").GetBoolean();like.Foreground=new SolidColorBrush(liked?Windows.UI.Color.FromArgb(255,238,158,172):Colors.White);}catch(Exception e){Storage.Log(e);}}
    async Task ToggleLike(){if(likeBusy||current?.NetEaseId.Length==0||current==null)return;likeBusy=true;like.IsEnabled=false;string id=current.NetEaseId;try{await service.Call("like",new{id,liked=!liked});await UpdateLike(loadVersion);}catch(Exception e){ShowError(e);}finally{likeBusy=false;like.IsEnabled=true;}}
    async Task Collect(){
        if(current==null||current.NetEaseId.Length==0){notice.Text="喜欢和收藏适用于网易云歌曲";return;}string id=current.NetEaseId;
        var list=new ListView{Height=320,ItemTemplate=PlaylistTemplate(),SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true};var msg=Text("",12,.6);var more=new Button{Content="加载更多"};int offset=0;
        async Task Load(){try{var data=await service.Call("playlists",new{offset});foreach(var item in data.GetProperty("items").EnumerateArray())if(item.GetProperty("owned").GetBoolean())list.Items.Add(ReadPlaylist(item));offset+=data.GetProperty("items").GetArrayLength();more.Visibility=data.GetProperty("more").GetBoolean()?Visibility.Visible:Visibility.Collapsed;if(list.Items.Count==0)msg.Text="暂无自己创建的歌单";}catch(Exception e){msg.Text=e.Message;}}
        list.ItemClick+=async(_,e)=>{list.IsEnabled=false;try{var item=(PlaylistItem)e.ClickedItem;await service.Call("collect",new{id,playlistId=item.Id});msg.Text="已收藏";}catch(Exception ex){msg.Text=ex.Message;}finally{list.IsEnabled=true;}};
        more.Click+=async(_,_)=>await Load();await Load();await Dialog("收藏 · "+current.Name,Column(list,more,msg));
    }
    public record PlaylistItem(string Id,string Name,string Cover="",string Subtitle=""){public override string ToString()=>Name;}
    async Task Queue(){
        var list=SongList(state.Queue);list.Height=400;list.SelectedIndex=state.Index;
        await Dialog("播放列表",list);
    }
    async Task Library(){
        if(openDialog!=null)return;
        var content=new StackPanel{Spacing=12,MaxWidth=500};var message=Text("",12,.65);
        var d=CreateSheet("音乐库",content);openDialog=d;
        using var cancel=new CancellationTokenSource();var token=cancel.Token;
        async Task ShowTracks(PlaylistItem item,bool daily=false){
            try{message.Text="正在读取歌曲…";var data=await service.Call(daily?"daily":"playlist",new{id=item.Id},token);var tracks=MusicService.Tracks(data);RememberCovers(tracks);content.Children.Clear();
                var back=new Button{Content="‹ 歌单"};back.Click+=async(_,_)=>await ShowPlaylists(false);
                var all=new Button{Content="播放全部"};ToolTipService.SetToolTip(all,"替换当前播放列表");all.Click+=async(_,_)=>{if(tracks.Count==0)return;state.Queue=tracks;state.PlaylistId=item.Id;state.Daily=daily;d.Hide();await LoadTrack(0,true);};
                var search=SearchInput("搜索歌曲或歌手");var list=SongList(tracks,text=>message.Text=text);list.Height=320;
                search.TextChanged+=(_,_)=>list.ItemsSource=tracks.Where(t=>(t.Name+" "+t.Artist).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
                content.Children.Add(Row(back,Primary(all)));content.Children.Add(AlbumHeading(item.Cover,item.Name,tracks.Count+" 首歌曲"));content.Children.Add(search);content.Children.Add(list);message.Text="";content.Children.Add(message);
            }catch(OperationCanceledException){}catch(Exception e){message.Text=e.Message;}
        }
        async Task ShowPlaylists(bool recommended){
            content.Children.Clear();var mine=new Button{Content="我的歌单"};var rec=new Button{Content="为你推荐"};mine.Click+=async(_,_)=>await ShowPlaylists(false);rec.Click+=async(_,_)=>await ShowPlaylists(true);var bili=new Button{Content="B 站音频"};bili.Click+=(_,_)=>{d.Hide();_=BiliLibrary();};content.Children.Add(Row(mine,rec,bili));
            if(recommended){var daily=new Button{Content="每日推荐"};daily.Click+=async(_,_)=>await ShowTracks(new("","每日推荐"),true);content.Children.Add(daily);}
            var list=new ListView{Height=320,ItemTemplate=PlaylistTemplate(),SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true};content.Children.Add(list);list.ItemClick+=async(_,e)=>await ShowTracks((PlaylistItem)e.ClickedItem);
            var more=new Button{Content="加载更多"};int offset=0;
            async Task Load(){try{var data=await service.Call(recommended?"recommendations":"playlists",new{offset},token);foreach(var item in data.GetProperty("items").EnumerateArray())list.Items.Add(ReadPlaylist(item));offset+=data.GetProperty("items").GetArrayLength();more.Visibility=data.GetProperty("more").GetBoolean()?Visibility.Visible:Visibility.Collapsed;message.Text="";}catch(OperationCanceledException){}catch(Exception e){message.Text=e.Message;}}
            more.Click+=async(_,_)=>await Load();content.Children.Add(more);var logout=new Button{Content="退出登录"};logout.Click+=async(_,_)=>{try{EndListening();await listening.Pump(accountId);await service.Call("logout");accountLogged=false;accountId="";d.Hide();}catch(Exception e){message.Text=e.Message;}};content.Children.Add(logout);content.Children.Add(message);await Load();
        }
        async Task Login(){try{
            content.Children.Clear();var bili=new Button{Content="B 站音频"};bili.Click+=(_,_)=>{d.Hide();_=BiliLibrary();};content.Children.Add(bili);content.Children.Add(message);message.Text="正在生成二维码…";var qr=await service.Call("qr",null,token);var image=new Image{Width=220,Height=220};var data=qr.GetProperty("image").GetString()!;using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(Convert.FromBase64String(data[(data.IndexOf(',')+1)..]));await writer.StoreAsync();}stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);image.Source=bitmap;content.Children.Insert(0,image);message.Text="使用网易云音乐 App 扫码";
            while(!token.IsCancellationRequested){await Task.Delay(2000,token);var status=await service.Call("check",null,token);int code=status.GetProperty("code").GetInt32();if(code==803){await LoadAccount();await ShowPlaylists(false);break;}if(code==800){message.Text="二维码已过期";var retry=new Button{Content="重新扫码"};retry.Click+=async(_,_)=>await Login();content.Children.Add(retry);break;}if(code==802)message.Text="请在手机上确认";}
        }catch(OperationCanceledException){}catch(Exception e){message.Text=e.Message;}}
        content.Children.Add(message);message.Text="正在读取…";d.Closed+=(_,_)=>cancel.Cancel();
        try{var showing=d.ShowAsync();await LoadAccount();if(!token.IsCancellationRequested){if(accountLogged)await ShowPlaylists(false);else _=Login();}await showing;}catch(OperationCanceledException){}catch(Exception e){ShowError(e);}finally{cancel.Cancel();if(openDialog==d)openDialog=null;}
    }
    async Task Settings(){
        var layout=new Grid{Width=Math.Min(500,Math.Max(280,ActualWidth-106)),HorizontalAlignment=HorizontalAlignment.Left,ColumnSpacing=24};layout.ColumnDefinitions.Add(new(){Width=new(88)});layout.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var nav=new StackPanel{Spacing=8};var detail=new StackPanel{Spacing=16,MinHeight=350};layout.Children.Add(nav);layout.Children.Add(detail);Grid.SetColumn(detail,1);
        var tabs=new List<Button>();
        void Heading(string title,string? description=null){detail.Children.Add(Text(title,20));if(description!=null)detail.Children.Add(Text(description,11,.58));}
        void Section(int index){
            detail.Children.Clear();for(int i=0;i<tabs.Count;i++){tabs[i].Background=new SolidColorBrush(Windows.UI.Color.FromArgb(i==index?(byte)42:(byte)0,231,169,186));tabs[i].Opacity=i==index?1:.65;}
            if(index==0){
                Heading("声音","选择输出设备，调整常用音色。");
                var devices=AudioEngine.GetOutputDevices();int selected=devices.ToList().FindIndex(d=>d.Id==state.OutputDeviceId);var outputMessage=Text("",11,.6);
                var device=Choice("输出设备",devices.Select(d=>d.Name).ToArray(),Math.Max(0,selected),i=>{try{audio.SelectOutput(devices[i].Id,state.ExclusiveOutput);state.OutputDeviceId=devices[i].Id;Storage.Save(state);}catch(Exception e){ShowError(e);}Section(0);});device.MaxWidth=340;device.HorizontalAlignment=HorizontalAlignment.Stretch;detail.Children.Add(device);
                var mode=Choice("输出方式",["日常 · WASAPI 共享","WASAPI 独占"],state.ExclusiveOutput?1:0,i=>{try{audio.SelectOutput(state.OutputDeviceId,i==1);state.ExclusiveOutput=i==1;Storage.Save(state);}catch(Exception e){ShowError(e);}Section(0);});detail.Children.Add(mode);
                detail.Children.Add(Text(state.ExclusiveOutput?"独占设备由澄音单独使用。处理链可在下方查看。":"共享输出允许其他应用同时发声。",11,.52));
                detail.Children.Add(AudioFormatSettings());
                detail.Children.Add(AudioProcessingSettings());
                detail.Children.Add(SettingSwitch("均衡器",state.Eq,value=>{state.Eq=value;audio.Eq(state.Eq,state.Gains);Storage.Save(state);}));
                string[] bands=["60 Hz","250 Hz","1 kHz","4 kHz","12 kHz"];
                var equalizer=new Grid{ColumnSpacing=10};
                for(int i=0;i<5;i++){int band=i;equalizer.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});var slider=new Slider{Minimum=-12,Maximum=12,StepFrequency=1,Value=state.Gains.Length>i?state.Gains[i]:0,Height=112,Orientation=Orientation.Vertical,HorizontalAlignment=HorizontalAlignment.Center};ToolTipService.SetToolTip(slider,bands[i]);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,bands[i]);slider.ValueChanged+=(_,e)=>{state.Gains[band]=e.NewValue;audio.Eq(state.Eq,state.Gains);SavePreferencesSoon();};var label=Text(bands[i],10,.65);label.HorizontalAlignment=HorizontalAlignment.Center;var column=Column(slider,label);equalizer.Children.Add(column);Grid.SetColumn(column,i);}detail.Children.Add(equalizer);
                detail.Children.Add(ActionButton("重置音色",()=>{state.Gains=new double[5];audio.Eq(state.Eq,state.Gains);Storage.Save(state);Section(0);}));
            }else if(index==1){
                Heading("外观与歌词","柔和、透明，让音乐留在画面中心。");
                detail.Children.Add(SettingSwitch("流动背景",state.Motion,value=>{state.Motion=value;SetMotion();Storage.Save(state);}));
                detail.Children.Add(SettingSwitch("显示翻译",state.Translation,value=>{state.Translation=value;ClearLyricTextures();lyricCanvas.Invalidate();Storage.Save(state);}));
                detail.Children.Add(Text("播放页注音",12,.7));detail.Children.Add(Choice("播放页注音",["关闭","平假名","罗马音"],PlaybackReadingMode,i=>{state.LyricReadingMode=i;state.Romanization=i==2;UpdateReadingButton();ClearLyricTextures();lyricCanvas.Invalidate();Storage.Save(state);if(i>0)_=LoadPlaybackReadings();}));
                detail.Children.Add(Text("也可以用歌词右上角的「あ」按钮依次切换。",11,.55));
                detail.Children.Add(Text("Noto Sans SC · 开源思源黑体\n页面动效遵循 Windows 动画设置。",11,.55));
            }else if(index==2){
                Heading("专注与记录");detail.Children.Add(Text(StudyClock.Format(state.TotalSeconds),27));detail.Children.Add(Text("累计学习时间 · 休息不计入",11,.55));
                detail.Children.Add(SettingSwitch("切到其他窗口时休息",state.Strict,value=>{state.Strict=value;Storage.Save(state);}));
                detail.Children.Add(ActionButton("结束本次专注",()=>{FinishStudy();Section(2);}));
                detail.Children.Add(Row(ActionButton("导出记录",ExportStudy),AsyncButton("导入记录",ImportStudy)));
                detail.Children.Add(Text("导入时保留较大的累计值，重复导入不会叠加时间。",11,.55));
            }else if(index==3){
                Heading("音乐与分析");detail.Children.Add(AsyncButton(accountLogged?"打开我的网易云":"登录网易云",async()=>{openDialog?.Hide();await Library();}));detail.Children.Add(AsyncButton("打开 B 站链接",async()=>{openDialog?.Hide();await BiliLibrary();}));
                detail.Children.Add(Text("人声音高与节拍",14));detail.Children.Add(Text("后台分段分析并缓存。拍号、音高与 BPM 是估计结果，混响、合唱和变拍可能产生误差。",12,.6));
                detail.Children.Add(AsyncButton("重新尝试当前歌曲分析",async()=>{analysisReady=false;await Analyze(loadVersion);notice.Text="后台分析状态已刷新";}));
            }
        }
        string[] names=["声音","外观","专注","音乐"];
        for(int i=0;i<names.Length;i++){int tab=i;var b=ActionButton(names[i],()=>Section(tab));b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Left;tabs.Add(b);nav.Children.Add(b);}Section(0);await Dialog("设置",layout);
    }
    void ExportStudy(){try{study.Checkpoint();var file=Path.Combine(Storage.Data,"学习记录-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");File.WriteAllText(file,JsonSerializer.Serialize(new{format="cleartone-study-v1",totalSeconds=state.TotalSeconds},Storage.Json));notice.Text="已导出到 "+file;}catch(Exception e){ShowError(e);}}
    async Task ImportStudy(){try{var picker=new Windows.Storage.Pickers.FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(App.Main));picker.FileTypeFilter.Add(".json");var file=await picker.PickSingleFileAsync();if(file==null)return;using var data=JsonDocument.Parse(await Windows.Storage.FileIO.ReadTextAsync(file));var r=data.RootElement;if(r.GetProperty("format").GetString()!="cleartone-study-v1")throw new InvalidDataException("不支持的记录格式");double total=r.GetProperty("totalSeconds").GetDouble();if(!double.IsFinite(total)||total<0)throw new InvalidDataException("学习时间无效");FinishStudy();state.TotalSeconds=Math.Max(state.TotalSeconds,total);Storage.Save(state);notice.Text="已合并累计时间";}catch(Exception e){ShowError(e);}}
}

