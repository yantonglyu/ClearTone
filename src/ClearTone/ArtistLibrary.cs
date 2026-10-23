using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Text.Json;
namespace ClearTone;

public sealed partial class MainPage
{
    sealed record FavoriteArtist(string Name,string Cover,List<Track> Tracks){public string Subtitle=>Tracks.Count+" 首收藏";}
    void ShowFavorites(Grid content,TextBlock message,CancellationToken token){
        content.Children.Clear();
        var frame=new Grid{RowSpacing=12};frame.RowDefinitions.Add(new(){Height=GridLength.Auto});frame.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var sort=Choice("整理收藏",["按添加顺序","按歌手"],shelf.FavoriteSort=="artist"?1:0,i=>{shelf.FavoriteSort=i==1?"artist":"added";shelf.Save();ShowFavorites(content,message,token);});sort.HorizontalAlignment=HorizontalAlignment.Right;frame.Children.Add(sort);
        var body=new Grid();frame.Children.Add(body);Grid.SetRow(body,1);content.Children.Add(frame);
        if(shelf.FavoriteSort!="artist"){ShowCatalogTracks(shelf.Tracks,"收藏的音乐",body,message,token);return;}
        var groups=shelf.Tracks.SelectMany(t=>t.Artist.Split(" / ",StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).DefaultIfEmpty("未知歌手").Select(name=>(Name:name,Track:t)))
            .GroupBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).OrderBy(g=>g.Key,StringComparer.CurrentCultureIgnoreCase)
            .Select(g=>new FavoriteArtist(g.Key,g.Select(x=>x.Track.Cover).FirstOrDefault(s=>s.Length>0)??"",g.Select(x=>x.Track).DistinctBy(t=>t.Key).ToList())).ToList();
        if(groups.Count==0){body.Children.Add(LibraryEmpty("还没有收藏","歌曲右侧的 ··· 可以收藏到音乐库。"));message.Text="";return;}
        var list=ArtworkList();list.ItemsSource=groups;
        list.ItemClick+=(_,e)=>{if(token.IsCancellationRequested||e.ClickedItem is not FavoriteArtist artist)return;
            body.Children.Clear();var songs=new Grid{RowSpacing=8};songs.RowDefinitions.Add(new(){Height=GridLength.Auto});songs.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
            var back=ActionButton("‹ 所有歌手",()=>ShowFavorites(content,message,token));back.HorizontalAlignment=HorizontalAlignment.Left;songs.Children.Add(back);var tracks=new Grid();songs.Children.Add(tracks);Grid.SetRow(tracks,1);body.Children.Add(songs);ShowCatalogTracks(artist.Tracks,artist.Name,tracks,message,token);};
        body.Children.Add(list);message.Text=$"{groups.Count} 位歌手 · {shelf.Tracks.Count} 首收藏";
    }
    void ShowSavedArtists(Grid content,TextBlock message,CancellationToken token){
        content.Children.Clear();
        if(shelf.Artists.Count==0){content.Children.Add(LibraryEmpty("收藏喜欢的歌手","将搜索类型切换为「作者」，进入歌手主页后点亮星标。"));message.Text="歌手主页包含平台上的全部歌曲与 MV";return;}
        var list=ArtworkList();list.ItemsSource=shelf.Artists;
        list.ItemClick+=async(_,e)=>{if(e.ClickedItem is SavedArtist artist)await ShowArtistPage(artist,content,message,token,()=>ShowSavedArtists(content,message,token));};
        content.Children.Add(list);message.Text=$"{shelf.Artists.Count} 位收藏歌手";
    }
    async Task ShowArtistPage(SavedArtist artist,Grid content,TextBlock message,CancellationToken token,Action? back=null){
        int pageVersion=++searchVersion;content.Children.Clear();
        var frame=new Grid{RowSpacing=16};frame.RowDefinitions.Add(new(){Height=GridLength.Auto});frame.RowDefinitions.Add(new(){Height=GridLength.Auto});frame.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var heading=new Grid{ColumnSpacing=12};heading.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.Children.Add(AlbumHeading(artist.Cover,artist.Name,artist.Source=="netease"?"网易云歌手":"B 站 UP 主"));
        var favorite=Icon("\uE734","收藏歌手");
        void Paint(){bool saved=shelf.Artists.Any(a=>a.Key==artist.Key);((FontIcon)favorite.Content).Glyph=saved?"\uE735":"\uE734";favorite.Foreground=new SolidColorBrush(saved?Color.FromArgb(255,244,190,209):Color.FromArgb(230,255,245,238));ToolTipService.SetToolTip(favorite,saved?"取消收藏歌手":"收藏歌手");Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(favorite,saved?"取消收藏歌手":"收藏歌手");}
        favorite.Click+=(_,_)=>{if(shelf.Artists.Any(a=>a.Key==artist.Key))shelf.Artists.RemoveAll(a=>a.Key==artist.Key);else shelf.Artists.Add(artist);shelf.Save();Paint();};Paint();heading.Children.Add(favorite);Grid.SetColumn(favorite,1);frame.Children.Add(heading);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};frame.Children.Add(tools);Grid.SetRow(tools,1);
        if(back!=null)tools.Children.Add(ActionButton("‹ 返回",back));
        var detail=new Grid();frame.Children.Add(detail);Grid.SetRow(detail,2);content.Children.Add(frame);
        int sectionVersion=0;var tabs=new List<Button>();
        async Task Load(bool videos){
            int version=++sectionVersion;foreach(var b in tabs)b.Opacity=(string)b.Tag==(videos?"mv":"songs")?1:.5;
            detail.Children.Clear();
            message.Text=videos?"正在读取 MV…":"正在读取歌曲…";
            try{
                var request=new SearchPage(artist.Name,artist.Source,videos?"mvs":"songs",artist.Id);var data=await service.Call(request.Route,request.Request(0),token);
                if(token.IsCancellationRequested||version!=sectionVersion||pageVersion!=searchVersion||!content.Children.Contains(frame))return;
                ShowCatalogTracks(MusicService.Tracks(data),videos?"音乐影像":"全部歌曲",detail,message,token,search:request,more:data.TryGetProperty("more",out var more)&&more.GetBoolean(),nextOffset:data.TryGetProperty("nextOffset",out var offset)?offset.GetInt32():0);
            }catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested&&version==sectionVersion&&pageVersion==searchVersion&&content.IsLoaded&&content.Children.Contains(frame))message.Text=e.Message;}
        }
        var songs=AsyncButton("歌曲",()=>Load(false));songs.Tag="songs";tabs.Add(songs);tools.Children.Add(songs);
        if(artist.Source=="netease"){var videos=AsyncButton("MV",()=>Load(true));videos.Tag="mv";tabs.Add(videos);tools.Children.Add(videos);}
        await Load(false);
    }
    static ListView ArtworkList(){
        var style=new Style(typeof(ListViewItem));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,0,8,0)));
        return new(){ItemTemplate=PlaylistTemplate(),ItemContainerStyle=style,SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true,HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new(0)};
    }
}
