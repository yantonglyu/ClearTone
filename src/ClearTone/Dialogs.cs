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
    async Task Dialog(string name,UIElement content){
        if(openDialog!=null)return;
        var d=CreateSheet(name,content);
        openDialog=d;try{await d.ShowAsync();}finally{if(openDialog==d)openDialog=null;}
    }
    async Task LoadAccount(){var d=await service.Call("status");accountLogged=d.GetProperty("loggedIn").GetBoolean();var next=accountLogged?d.GetProperty("profile").GetProperty("id").ToString():"";if(next!=accountId){EndListening();accountId=next;}if(accountLogged)_=listening.Pump(accountId);}
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
        var list=new ListView{ItemsSource=state.Queue,Height=400,ItemTemplate=TrackTemplate(),IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single,SelectedIndex=state.Index};
        list.ItemClick+=async(_,e)=>{int i=state.Queue.IndexOf((Track)e.ClickedItem);openDialog?.Hide();await LoadTrack(i,true);};
        await Dialog("播放列表",list);
    }
    async Task Library(){
        if(openDialog!=null)return;
        var content=new StackPanel{Spacing=12,Width=500};var message=Text("",12,.65);
        var d=CreateSheet("音乐库",content);openDialog=d;
        using var cancel=new CancellationTokenSource();var token=cancel.Token;
        async Task ShowTracks(PlaylistItem item,bool daily=false){
            try{message.Text="正在读取歌曲…";var data=await service.Call(daily?"daily":"playlist",new{id=item.Id},token);var tracks=MusicService.Tracks(data);RememberCovers(tracks);content.Children.Clear();
                var back=new Button{Content="‹ 歌单"};back.Click+=async(_,_)=>await ShowPlaylists(false);
                var all=new Button{Content="播放全部"};ToolTipService.SetToolTip(all,"替换当前播放列表");all.Click+=async(_,_)=>{if(tracks.Count==0)return;state.Queue=tracks;state.PlaylistId=item.Id;state.Daily=daily;d.Hide();await LoadTrack(0,true);};
                var search=new TextBox{PlaceholderText="搜索歌曲或歌手",Background=new SolidColorBrush(Windows.UI.Color.FromArgb(16,255,255,255)),BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(9)};var list=new ListView{Height=320,ItemTemplate=TrackTemplate(),SelectionMode=ListViewSelectionMode.Multiple,ItemsSource=tracks,IsItemClickEnabled=false};
                search.TextChanged+=(_,_)=>list.ItemsSource=tracks.Where(t=>(t.Name+" "+t.Artist).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
                var add=new Button{Content="加入播放列表"};add.Click+=(_,_)=>{var existing=state.Queue.Select(t=>t.NetEaseId.Length>0?t.NetEaseId:t.Url).ToHashSet();int added=0;foreach(var t in list.SelectedItems.Cast<Track>()){string key=t.NetEaseId.Length>0?t.NetEaseId:t.Url;if(existing.Add(key)){state.Queue.Add(t);added++;}}Storage.Save(state);message.Text=added>0?$"已加入 {added} 首，当前播放继续":"所选歌曲已在队列中";};
                list.DoubleTapped+=async(_,_)=>{if(list.SelectedItem is not Track t)return;int i=state.Queue.FindIndex(q=>q.NetEaseId==t.NetEaseId);if(i<0){state.Queue.Add(t);i=state.Queue.Count-1;}d.Hide();await LoadTrack(i,true);};
                content.Children.Add(Row(back,all));content.Children.Add(AlbumHeading(item.Cover,item.Name,tracks.Count+" 首歌曲"));content.Children.Add(search);content.Children.Add(list);content.Children.Add(add);message.Text="双击歌曲可直接播放";content.Children.Add(message);
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
            while(!token.IsCancellationRequested){await Task.Delay(2000,token);var status=await service.Call("check",null,token);int code=status.GetProperty("code").GetInt32();if(code==803){accountLogged=true;await ShowPlaylists(false);break;}if(code==800){message.Text="二维码已过期";var retry=new Button{Content="重新扫码"};retry.Click+=async(_,_)=>await Login();content.Children.Add(retry);break;}if(code==802)message.Text="请在手机上确认";}
        }catch(OperationCanceledException){}catch(Exception e){message.Text=e.Message;}}
        content.Children.Add(message);message.Text="正在读取…";d.Closed+=(_,_)=>cancel.Cancel();
        try{var showing=d.ShowAsync();await LoadAccount();if(!token.IsCancellationRequested){if(accountLogged)await ShowPlaylists(false);else _=Login();}await showing;}catch(OperationCanceledException){}catch(Exception e){ShowError(e);}finally{cancel.Cancel();if(openDialog==d)openDialog=null;}
    }
    async Task Settings(){
        var body=new StackPanel{Width=500,Spacing=16};
        var motion=SettingSwitch("流动背景",state.Motion,value=>{state.Motion=value;SetMotion();Storage.Save(state);});
        var strict=SettingSwitch("切到其他窗口时休息",state.Strict,value=>{state.Strict=value;Storage.Save(state);});
        var eq=SettingSwitch("均衡器",state.Eq,value=>{state.Eq=value;audio.Eq(state.Eq,state.Gains);Storage.Save(state);});
        var music=new Button{Content="音乐库  ›"};music.Click+=(_,_)=>{openDialog?.Hide();_=Library();};body.Children.Add(music);body.Children.Add(Text("声音",18));body.Children.Add(Text("解码音源 "+(audio.SampleRate>0?(audio.SampleRate/1000.0)+" kHz":"未知"),12,.6));body.Children.Add(eq);
        string[] bands=["60 Hz","250 Hz","1 kHz","4 kHz","12 kHz"];
        for(int i=0;i<5;i++){int band=i;var slider=new Slider{Minimum=-12,Maximum=12,StepFrequency=1,Value=state.Gains.Length>i?state.Gains[i]:0,Width=270};var label=Text(bands[i],12,.65);label.Width=65;slider.ValueChanged+=(_,e)=>{state.Gains[band]=e.NewValue;audio.Eq(state.Eq,state.Gains);Storage.Save(state);};body.Children.Add(Row(label,slider));}
        var reset=new Button{Content="重置 EQ"};reset.Click+=(_,_)=>{state.Gains=new double[5];audio.Eq(state.Eq,state.Gains);Storage.Save(state);openDialog?.Hide();};body.Children.Add(reset);
        var retry=new Button{Content="重新尝试后台分析"};retry.Click+=async(_,_)=>{analysisReady=false;await Analyze(loadVersion);notice.Text="后台分析状态已刷新";};body.Children.Add(retry);
        body.Children.Add(motion);body.Children.Add(strict);body.Children.Add(Text("累计学习  "+StudyClock.Format(state.TotalSeconds),16));
        var finish=new Button{Content="结束本次专注"};finish.Click+=(_,_)=>FinishStudy();
        var export=new Button{Content="导出记录"};export.Click+=(_,_)=>{try{study.Checkpoint();var file=Path.Combine(Storage.Data,"学习记录-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");File.WriteAllText(file,JsonSerializer.Serialize(new{format="cleartone-study-v1",totalSeconds=state.TotalSeconds},Storage.Json));notice.Text="已导出到 "+file;}catch(Exception e){ShowError(e);}};
        var import=new Button{Content="导入记录"};import.Click+=async(_,_)=>{try{var picker=new Windows.Storage.Pickers.FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(App.Main));picker.FileTypeFilter.Add(".json");var file=await picker.PickSingleFileAsync();if(file==null)return;var data=JsonDocument.Parse(await Windows.Storage.FileIO.ReadTextAsync(file));double total;var r=data.RootElement;if(r.GetProperty("format").GetString()=="cleartone-study-v1")total=r.GetProperty("totalSeconds").GetDouble();else throw new InvalidDataException("不支持的记录格式");if(!double.IsFinite(total)||total<0)throw new InvalidDataException("学习时间无效");FinishStudy();state.TotalSeconds=Math.Max(state.TotalSeconds,total);Storage.Save(state);notice.Text="已合并累计时间（保留较大值，不重复相加）";}catch(Exception e){ShowError(e);}};
        body.Children.Add(Row(finish,export,import));await Dialog("设置",body);
    }
}
