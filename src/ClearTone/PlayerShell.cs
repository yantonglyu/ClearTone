using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage
{
    readonly Grid workspace=new();readonly StackPanel navigation=new(){Orientation=Orientation.Horizontal,Spacing=4};
    readonly Dictionary<string,Button> navigationButtons=[];
    readonly MusicShelf shelf=MusicShelf.Load();
    readonly TextBlock miniTitle=Text("选择一首歌",13,.8);readonly Button miniPlay=Icon("\uE768","播放或暂停");
    StackPanel? miniPlayer;Button? miniSong,collapsePlayer;readonly Image miniCover=new(){Width=42,Height=42,Stretch=Stretch.UniformToFill};readonly Slider miniSeek=new(){Minimum=0,Maximum=1,StepFrequency=.1,Height=20};bool miniSeeking;string miniCoverSource="",lastHomePage="discover";string page="now";CancellationTokenSource pageCancel=new();
    List<Track>? queueUndo;string queueUndoPlaylist="";bool queueUndoDaily;
    static Button ActionButton(string label,Action action){var b=new Button{FontFamily=UiFont,Content=label,CornerRadius=new(10),MinHeight=36,FontSize=13,Padding=new(14,8,14,8),VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Color.FromArgb(18,255,255,255)),BorderThickness=new(0)};b.Click+=(_,_)=>action();return b;}
    Button AsyncButton(string label,Func<Task> action){var b=ActionButton(label,()=>{});b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(Exception e){ShowError(e);}finally{b.IsEnabled=true;}};return b;}
    void BuildShell(Grid layout){
        navigation.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(navigation);
        foreach(var item in new[]{("discover","\uE80F","主页"),("library","\uE8F1","音乐库"),("practice","\uE720","跟唱")}){
            string key=item.Item1;var b=ActionButton(item.Item3,()=>_=Navigate(key));b.Content=Row(new FontIcon{Glyph=item.Item2,FontSize=15},Text(item.Item3,12,.9));ToolTipService.SetToolTip(b,item.Item3);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,item.Item3);navigationButtons[key]=b;navigation.Children.Add(b);
        }
        collapsePlayer=Icon("\uE70D","收起播放页 · 可向下拖动");collapsePlayer.Click+=(_,_)=>{if(!SuppressShellClick)_=Navigate(lastHomePage);};collapsePlayer.HorizontalAlignment=HorizontalAlignment.Left;collapsePlayer.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(collapsePlayer);HookShellDrag(collapsePlayer,false);
        workspace.Margin=new(60,12,60,20);workspace.MaxWidth=1200;workspace.HorizontalAlignment=HorizontalAlignment.Stretch;workspace.Visibility=Visibility.Collapsed;Grid.SetRow(workspace,1);layout.Children.Add(workspace);
        var prev=Icon("\uE892","上一首");prev.Click+=async(_,_)=>await Next(-1);var next=Icon("\uE893","下一首");next.Click+=async(_,_)=>await Next(1);
        miniPlay.Click+=(_,_)=>TogglePlayback();miniTitle.MaxWidth=190;miniTitle.TextWrapping=TextWrapping.NoWrap;miniTitle.TextTrimming=TextTrimming.CharacterEllipsis;
        miniPlayer=Row(prev,miniPlay,next);miniPlayer.Visibility=Visibility.Collapsed;miniPlayer.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(miniPlayer,1);footer.Children.Add(miniPlayer);
        miniSong=ActionButton("展开播放页",()=>{if(!SuppressShellClick)_=Navigate("now");});miniSong.Content=Row(new Border{CornerRadius=new(7),Child=miniCover},miniTitle);miniSong.Padding=new(0);miniSong.Background=new SolidColorBrush(Color.FromArgb(0,0,0,0));miniSong.HorizontalAlignment=HorizontalAlignment.Left;miniSong.VerticalAlignment=VerticalAlignment.Center;ToolTipService.SetToolTip(miniSong,"展开播放页 · 可向上拖动");Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(miniSong,"展开播放页");footer.Children.Add(miniSong);HookShellDrag(miniSong,true);
        footer.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});footer.RowDefinitions.Add(new(){Height=GridLength.Auto});footer.Children.Add(miniSeek);Grid.SetRow(miniSeek,1);Grid.SetColumnSpan(miniSeek,3);
        miniSeek.AddHandler(PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>miniSeeking=true),true);miniSeek.AddHandler(PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>{if(miniSeeking){SeekAudio(miniSeek.Value);miniSeeking=false;}}),true);miniSeek.KeyUp+=(_,_)=>SeekAudio(miniSeek.Value);
        _=Navigate("discover");
    }
    void PlayAudio(){if(trackReady&&!disposed)audio.Play();}
    void SeekAudio(double seconds){if(trackReady&&!disposed){eggClock.Seek();audio.Seek(seconds);}}
    void TogglePlayback(){if(audio.Playing)audio.Pause();else PlayAudio();UpdatePlay();}
    void PaintNavigation(){foreach(var pair in navigationButtons){pair.Value.Background=new SolidColorBrush(pair.Key==page?Color.FromArgb(48,231,169,186):Color.FromArgb(0,255,255,255));pair.Value.Opacity=pair.Key==page?1:.65;}}
    async Task NavigateCore(string target){
        if(target is not ("now" or "discover" or "library" or "practice"))return;
        bool returning=page=="now"&&target==lastHomePage&&workspace.Children.Count>0&&(target!="practice"||ReferenceEquals(practiceLines?.ItemsSource,lyrics));
        if(page=="practice"&&target!="practice")StopPractice();
        // Expanding the player keeps the home view alive, including its query and scroll position.
        if(target!="now"&&!returning){pageCancel.Cancel();pageCancel.Dispose();pageCancel=new();}
        page=target;if(target!="now")lastHomePage=target;PaintNavigation();
        navigation.Visibility=page=="now"?Visibility.Collapsed:Visibility.Visible;
        if(collapsePlayer!=null)collapsePlayer.Visibility=page=="now"?Visibility.Visible:Visibility.Collapsed;
        body.Visibility=page=="now"?Visibility.Visible:Visibility.Collapsed;workspace.Visibility=page=="now"?Visibility.Collapsed:Visibility.Visible;
        if(miniSong!=null)miniSong.Visibility=page=="now"?Visibility.Collapsed:Visibility.Visible;miniSeek.Visibility=page=="now"?Visibility.Collapsed:Visibility.Visible;
        if(miniPlayer!=null)miniPlayer.Visibility=page=="now"?Visibility.Collapsed:Visibility.Visible;
        if(visualPanel!=null)visualPanel.Visibility=page=="now"?Visibility.Visible:Visibility.Collapsed;
        if(quotePanel!=null)quotePanel.Visibility=page=="now"?Visibility.Visible:Visibility.Collapsed;
        StopLyricAnimation();
        if(page=="now"){WakeLyricAnimation();return;}
        if(returning){if(page=="practice")PaintPractice(true);return;}
        workspace.Children.Clear();
        if(page=="practice")_=BuildPractice();else BuildCatalog(page=="discover");
    }
    bool? miniPlaying;
    void UpdateShell(){
        SetText(miniTitle,current?.Name??"选择一首歌");
        if(current?.Cover!=miniCoverSource){miniCoverSource=current?.Cover??"";miniCover.Source=Uri.TryCreate(miniCoverSource,UriKind.Absolute,out var uri)?new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri){DecodePixelWidth=96}:null;}
        if(page!="now"&&!miniSeeking){miniSeek.Maximum=Math.Max(1,audio.Duration);if(Math.Abs(miniSeek.Value-audio.Position)>.05)miniSeek.Value=Math.Min(miniSeek.Maximum,audio.Position);}if(miniPlaying!=audio.Playing){miniPlaying=audio.Playing;((FontIcon)miniPlay.Content).Glyph=audio.Playing?"\uE769":"\uE768";}

    }
    async Task PlaySelection(Track track){
        int i=state.Queue.FindIndex(t=>t.Key==track.Key);if(i<0){state.Queue.Add(track);i=state.Queue.Count-1;}
        await LoadTrack(i,true);
    }
    void AppendSelection(IEnumerable<Track> tracks){int added=0;foreach(var track in tracks.ToArray()){state.Queue.Add(track);added++;}Storage.Save(state);notice.Text=$"已加入 {added} 首";}
    async Task ReplaceQueue(List<Track> tracks,string playlist="",bool daily=false){
        if(tracks.Count==0)return;queueUndo=state.Queue.ToList();queueUndoPlaylist=state.PlaylistId;queueUndoDaily=state.Daily;
        state.Queue=tracks.ToList();state.PlaylistId=playlist;state.Daily=daily;await LoadTrack(0,true);
    }
    async Task UndoQueue(){if(queueUndo==null)return;var old=queueUndo;queueUndo=null;string key=current?.Key??"";state.Queue=old;state.PlaylistId=queueUndoPlaylist;state.Daily=queueUndoDaily;int i=old.FindIndex(t=>t.Key==key);if(i>=0){state.Index=i;Storage.Save(state);}else if(old.Count>0)await LoadTrack(0,false);else{audio.Pause();EndListening();state.Index=0;Storage.Save(state);} }
    async Task ImportLocal(){
        try{var picker=new Windows.Storage.Pickers.FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(App.Main));foreach(var ext in new[]{".flac",".wav",".mp3",".m4a",".aac",".wma",".dsf",".dff"})picker.FileTypeFilter.Add(ext);
            var files=await picker.PickMultipleFilesAsync();var tracks=new List<Track>();
            foreach(var file in files){
                var track=new Track{Name=file.DisplayName,Artist="本地音乐",Url=file.Path};
                try{var props=await file.Properties.GetMusicPropertiesAsync();if(!string.IsNullOrWhiteSpace(props.Title))track.Name=props.Title;if(!string.IsNullOrWhiteSpace(props.Artist))track.Artist=props.Artist;track.FullDuration=props.Duration.TotalSeconds;}catch when(DsdDecoder.IsDsd(file.Path)){}
                string lrc=Path.ChangeExtension(file.Path,".lrc");track.Lrc=File.Exists(lrc)?await File.ReadAllTextAsync(lrc):"";tracks.Add(track);
            }
            shelf.Add(tracks);if(page=="library")BuildCatalog(false);
        }catch(Exception e){ShowError(e);}
    }
    async Task SaveMixed(List<Track> tracks){
        if(tracks.Count==0)return;var name=new TextBox{PlaceholderText="歌单名称",Text="我的混合歌单",MaxLength=80};
        var save=ActionButton("保存",()=>{if(string.IsNullOrWhiteSpace(name.Text))return;shelf.Playlists.Add(new(){Name=name.Text.Trim(),Tracks=tracks.ToList()});shelf.Add(tracks);openDialog?.Hide();if(page=="library")BuildCatalog(false);});
        await Dialog("保存为本地歌单",Column(name,save));
    }
    static RubyPanel Wrap(params UIElement[] items){var panel=new RubyPanel();foreach(var item in items){if(item is FrameworkElement e)e.Margin=new Thickness(0,0,8,6);panel.Children.Add(item);}return panel;}
    Grid CatalogFrame(string heading,out Grid actions,out TextBlock message,out Grid content){
        var grid=new Grid{RowSpacing=16};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var label=Text(heading,26);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;grid.Children.Add(label);actions=new Grid{RowSpacing=10};actions.RowDefinitions.Add(new(){Height=GridLength.Auto});actions.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.Children.Add(actions);Grid.SetRow(actions,1);content=new Grid();grid.Children.Add(content);Grid.SetRow(content,2);message=Text("",11,.55);grid.Children.Add(message);Grid.SetRow(message,3);return grid;
    }
    string catalogSource="netease",catalogKind="songs";
    void BuildCatalog(bool discover){
        homeBuilds++;searchVersion++;
        workspace.Children.Clear();var token=pageCancel.Token;var frame=CatalogFrame(discover?"发现喜欢的声音":"音乐库",out var actions,out var message,out var content);workspace.Children.Add(frame);
        var searchSurface=CatalogSearch(content,message,token);
        if(discover)actions.Children.Add(searchSurface);
        else{
            // The library's list leads: share one title/search row instead of stacking three toolbars.
            var heading=(FrameworkElement)frame.Children[0];frame.Children.Remove(heading);
            var headingRow=new Grid{ColumnSpacing=24};headingRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});headingRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            heading.VerticalAlignment=VerticalAlignment.Center;headingRow.Children.Add(heading);headingRow.Children.Add(searchSurface);Grid.SetColumn(searchSurface,1);frame.Children.Insert(0,headingRow);actions.RowSpacing=0;
        }
        var account=AsyncButton(accountLogged?"我的网易云":"登录网易云",async()=>{await Library();await LoadAccount();if(!token.IsCancellationRequested)BuildCatalog(discover);});
        var secondary=new Grid();actions.Children.Add(secondary);Grid.SetRow(secondary,discover?1:0);
        if(discover){
            secondary.Children.Add(Row(account,AsyncButton("打开 B 站链接",BiliLibrary)));
            var daily=AsyncButton("今日推荐",async()=>{try{message.Text="正在读取…";var d=await service.Call("daily",null,token);if(!token.IsCancellationRequested)ShowCatalogTracks(MusicService.Tracks(d),"每日推荐",content,message,token,daily:true);}catch(Exception e){if(!token.IsCancellationRequested)message.Text=e.Message;}});
            var recommendations=AsyncButton("推荐歌单",async()=>{try{message.Text="正在读取…";var d=await service.Call("recommendations",null,token);if(token.IsCancellationRequested)return;var list=new ListView{ItemsSource=d.GetProperty("items").EnumerateArray().Select(ReadPlaylist).ToList(),ItemTemplate=PlaylistTemplate(),SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true};list.ItemClick+=async(_,e)=>{var item=(PlaylistItem)e.ClickedItem;try{var songs=await service.Call("playlist",new{id=item.Id},token);if(!token.IsCancellationRequested)ShowCatalogTracks(MusicService.Tracks(songs),item.Name,content,message,token,item.Id);}catch(Exception ex){message.Text=ex.Message;}};content.Children.Clear();content.Children.Add(list);message.Text="为你推荐";}catch(Exception e){if(!token.IsCancellationRequested)message.Text=e.Message;}});
            var recent=AsyncButton("继续听",()=>{ShowCatalogTracks(shelf.History,"最近播放",content,message,token);return Task.CompletedTask;});
            daily.HorizontalAlignment=HorizontalAlignment.Stretch;daily.HorizontalContentAlignment=HorizontalAlignment.Stretch;daily.Padding=new(26);daily.CornerRadius=new(18);daily.Background=new SolidColorBrush(Color.FromArgb(38,237,187,204));
            var hero=new Grid{MinHeight=132};var caption=Text("今日推荐",24);caption.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;hero.Children.Add(Column(Text(DateTime.Now.ToString("MM / dd"),11,.55),caption,Text("为今天，留一点心动的旋律。",12,.65)));var spark=new FontIcon{Glyph="\uE787",FontSize=38,Opacity=.45,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};hero.Children.Add(spark);daily.Content=hero;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(daily,"今日推荐");
            void Entry(Button b,string glyph,string heading,string description){b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Padding=new(18,15,18,15);b.Background=new SolidColorBrush(Color.FromArgb(12,255,255,255));var row=new Grid{ColumnSpacing=16};row.ColumnDefinitions.Add(new(){Width=new(28)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.Children.Add(new FontIcon{Glyph=glyph,FontSize=23,Opacity=.7});var labels=Column(Text(heading,15),Text(description,11,.55));row.Children.Add(labels);Grid.SetColumn(labels,1);b.Content=row;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,heading);}
            Entry(recommendations,"\uE8F1","推荐歌单","收集新的喜欢");Entry(recent,"\uE81C","继续听","回到熟悉的声音");
            var group=new Grid{ColumnSpacing=18,VerticalAlignment=VerticalAlignment.Top};group.ColumnDefinitions.Add(new(){Width=new(1.35,GridUnitType.Star)});group.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});group.Children.Add(daily);var side=Column(recommendations,recent);side.Spacing=14;group.Children.Add(side);Grid.SetColumn(side,1);content.Children.Add(group);
            message.Text=accountLogged?"日推与歌单来自你的网易云账号":"登录网易云查看日推，也可以搜索 B 站或导入本地音乐";
        }else{
            BuildLibraryNavigation(secondary,content,message,token,account);
        }
    }
    sealed record SearchPage(string Query,string Source,string Kind,string ArtistId=""){
        public object Request(int offset)=>ArtistId.Length>0?new{id=ArtistId,source=Source,offset}:(object)new{query=Query,source=Source,kind=Kind,offset};
        public string Route=>ArtistId.Length>0?"artist-tracks":"search";
    }
    public sealed record CatalogArtist(string Id,string Name,string Cover,string Source,string Description){public string Subtitle=>Description;}
    async Task SearchCatalog(string query,Grid content,TextBlock message,CancellationToken token){
        query=query.Trim();if(query.Length==0)return;int version=++searchVersion;var request=new SearchPage(query,catalogSource,catalogKind);message.Text="正在搜索…";
        if(catalogSource=="local"){
            var local=shelf.Tracks.Concat(shelf.History).Where(t=>t.NetEaseId.Length==0&&t.Bvid.Length==0).DistinctBy(t=>t.Key).Where(t=>(catalogKind=="artists"?t.Artist:t.Name+" "+t.Artist).Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();ShowCatalogTracks(local,"本地 · "+query,content,message,token);return;
        }
        try{var d=await service.Call(request.Route,request.Request(0),token);if(token.IsCancellationRequested||version!=searchVersion)return;
            if(request.Kind=="artists")ShowCatalogArtists(d,request,content,message,token);
            else ShowCatalogTracks(MusicService.Tracks(d),"搜索 · "+query,content,message,token,search:request,more:d.GetProperty("more").GetBoolean(),nextOffset:d.GetProperty("nextOffset").GetInt32());
        }catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested&&version==searchVersion){content.Children.Clear();content.Children.Add(Text(e.Message,14,.7));message.Text="可换一个来源或关键词重试";}}
    }
    void ShowCatalogArtists(System.Text.Json.JsonElement data,SearchPage request,Grid content,TextBlock message,CancellationToken token){
        content.Children.Clear();var artists=System.Text.Json.JsonSerializer.Deserialize<List<CatalogArtist>>(data.GetProperty("artists"),Storage.Json)??[];
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var artistRows=new System.Collections.ObjectModel.ObservableCollection<CatalogArtist>(artists);var list=new ListView{ItemsSource=artistRows,ItemTemplate=PlaylistTemplate(),SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true};grid.Children.Add(list);
        list.ItemClick+=async(_,e)=>{int version=++searchVersion;var a=(CatalogArtist)e.ClickedItem;message.Text="正在读取 "+a.Name;try{var next=new SearchPage(a.Name,a.Source,"songs",a.Id);var d=await service.Call(next.Route,next.Request(0),token);if(!token.IsCancellationRequested&&version==searchVersion)ShowCatalogTracks(MusicService.Tracks(d),a.Name,content,message,token,search:next,more:d.GetProperty("more").GetBoolean(),nextOffset:d.GetProperty("nextOffset").GetInt32());}catch(Exception ex){message.Text=ex.Message;}};
        int offset=data.GetProperty("nextOffset").GetInt32();Button? more=null;more=AsyncButton("更多作者",async()=>{var d=await service.Call(request.Route,request.Request(offset),token);if(token.IsCancellationRequested||!content.Children.Contains(grid))return;var extra=System.Text.Json.JsonSerializer.Deserialize<List<CatalogArtist>>(d.GetProperty("artists"),Storage.Json)??[];artists.AddRange(extra);foreach(var item in extra)artistRows.Add(item);offset=d.GetProperty("nextOffset").GetInt32();more!.Visibility=d.GetProperty("more").GetBoolean()?Visibility.Visible:Visibility.Collapsed;});more.Visibility=data.GetProperty("more").GetBoolean()?Visibility.Visible:Visibility.Collapsed;Grid.SetRow(more,1);grid.Children.Add(more);content.Children.Add(grid);message.Text=$"{request.Query} · {artists.Count} 位作者";
    }
    int searchVersion,homeBuilds;ListView? catalogList;
    void ShowCatalogTracks(List<Track> tracks,string heading,Grid content,TextBlock message,CancellationToken token,string playlist="",bool daily=false,SearchPage? search=null,bool more=false,int nextOffset=0){
        content.Children.Clear();var grid=new Grid{RowSpacing=10};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var visibleTracks=new System.Collections.ObjectModel.ObservableCollection<Track>(tracks);var list=catalogList=SongList(visibleTracks,text=>message.Text=text);
        var playAll=Primary(AsyncButton("播放全部",()=>ReplaceQueue(tracks,playlist,daily)));playAll.IsEnabled=tracks.Count>0;
        var moreActions=MoreActions(ActionButton("全部加入队列",()=>AppendSelection(tracks)),AsyncButton("存为本地歌单",()=>SaveMixed(tracks)),AsyncButton("撤销换队列",UndoQueue));
        var buttons=new Grid{ColumnSpacing=20};buttons.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});buttons.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var caption=Text(heading,15,.85);caption.HorizontalAlignment=HorizontalAlignment.Left;caption.TextTrimming=TextTrimming.CharacterEllipsis;caption.TextWrapping=TextWrapping.NoWrap;caption.VerticalAlignment=VerticalAlignment.Center;buttons.Children.Add(caption);var rightTools=Row(playAll,moreActions);rightTools.HorizontalAlignment=HorizontalAlignment.Right;buttons.Children.Add(rightTools);Grid.SetColumn(rightTools,1);
        grid.Children.Add(buttons);grid.Children.Add(list);Grid.SetRow(list,1);
        if(tracks.Count==0){var empty=LibraryEmpty("这里还很安静","搜索一首喜欢的歌，或从右上角导入本地音乐。");grid.Children.Add(empty);Grid.SetRow(empty,1);}
        if(more&&search!=null){int offset=nextOffset;Button? load=null;load=AsyncButton("更多结果",async()=>{try{var d=await service.Call(search.Route,search.Request(offset),token);if(token.IsCancellationRequested||!content.Children.Contains(grid))return;var extra=MusicService.Tracks(d);tracks.AddRange(extra);foreach(var track in extra)visibleTracks.Add(track);playAll.IsEnabled=tracks.Count>0;offset=d.GetProperty("nextOffset").GetInt32();if(load!=null)load.Visibility=d.GetProperty("more").GetBoolean()?Visibility.Visible:Visibility.Collapsed;message.Text=$"{heading} · {tracks.Count} 首";}catch(Exception e){message.Text=e.Message;}});grid.Children.Add(load);Grid.SetRow(load,2);}
        content.Children.Add(grid);message.Text=tracks.Count==0?"还没有歌曲，可以搜索、导入本地音乐或打开歌单":$"{tracks.Count} 首";
    }
}



