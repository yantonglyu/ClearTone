using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;
namespace ClearTone;
public sealed partial class MainPage
{
    static UIElement SettingSwitch(string label,bool enabled,Action<bool> changed){
        var row=new Grid{MinHeight=40};row.Children.Add(Text(label,14,.85));
        var button=new Button{Content=enabled?"●":"○",Width=52,HorizontalAlignment=HorizontalAlignment.Right};
        void Paint(){button.Content=enabled?"●":"○";button.Opacity=enabled?1:.45;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label+(enabled?"，已开启":"，已关闭"));}
        Paint();button.Click+=(_,_)=>{enabled=!enabled;Paint();changed(enabled);};row.Children.Add(button);return row;
    }
    MusicSheet CreateSheet(string title,UIElement content){var d=new MusicSheet(Root,title,content);d.Closed+=(_,_)=>{if(openDialog==d)openDialog=null;};return d;}
    static DataTemplate TrackTemplate()=>ArtworkTemplate("Artist");
    static DataTemplate PlaylistTemplate()=>ArtworkTemplate("Subtitle");
    static DataTemplate ArtworkTemplate(string subtitle)=>(DataTemplate)XamlReader.Load("""
    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
      <Grid Padding="0,7" ColumnSpacing="16">
        <Grid.ColumnDefinitions><ColumnDefinition Width="56"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Border CornerRadius="8" Width="56" Height="56" Background="#20FFFFFF">
          <Image Stretch="UniformToFill"><Image.Source><BitmapImage UriSource="{Binding Cover}" DecodePixelWidth="112"/></Image.Source></Image>
        </Border>
        <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="5">
          <TextBlock Text="{Binding Name}" FontSize="15" Foreground="#F6F4F8" TextTrimming="CharacterEllipsis"/>
          <TextBlock Text="{Binding SUBTITLE}" FontSize="12" Foreground="#A8B1BD" TextTrimming="CharacterEllipsis"/>
        </StackPanel>
      </Grid>
    </DataTemplate>
    """.Replace("SUBTITLE",subtitle));
    static PlaylistItem ReadPlaylist(JsonElement item)=>new(item.GetProperty("id").GetString()!,item.GetProperty("name").GetString()!,
        item.TryGetProperty("cover",out var c)?c.GetString()??"":"",
        item.TryGetProperty("count",out var n)?n.ToString()+" 首歌曲":"");
    static UIElement AlbumHeading(string cover,string name,string subtitle){
        var image=new Image{Width=72,Height=72,Stretch=Stretch.UniformToFill};
        if(Uri.TryCreate(cover,UriKind.Absolute,out var uri))image.Source=new BitmapImage(uri){DecodePixelWidth=144};
        var art=new Border{Child=image,CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(25,255,255,255))};
        var heading=Text(name,20);heading.MaxWidth=380;return Row(art,Column(heading,Text(subtitle,12,.55)));
    }
    void RememberCovers(IEnumerable<Track> tracks){
        state.CoverGallery=state.CoverGallery.Concat(tracks.Where(t=>t.NetEaseId.Length>0).Select(t=>t.Cover)).Where(s=>s.StartsWith("https://")).Distinct().TakeLast(500).ToList();Storage.Save(state);
    }
    async Task<string> RandomAccountCover(CancellationToken token){
        RememberCovers(state.Queue);
        if(state.CoverGallery.Count==0&&accountLogged){
            var mine=await service.Call("playlists",new{offset=0},token);
            foreach(var item in mine.GetProperty("items").EnumerateArray()){
                var album=await service.Call("playlist",new{id=item.GetProperty("id").GetString()},token);
                RememberCovers(MusicService.Tracks(album));if(state.CoverGallery.Count>0)break;
            }
        }
        return state.CoverGallery.Count>0?state.CoverGallery[Random.Shared.Next(state.CoverGallery.Count)]:"";
    }
    async Task BiliLibrary(){
        if(openDialog!=null)return;
        using var cancel=new CancellationTokenSource();
        var input=new TextBox{PlaceholderText="粘贴 B 站视频链接或 BV 号",Background=new SolidColorBrush(Windows.UI.Color.FromArgb(20,255,255,255)),BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(9)};
        var read=new Button{Content="读取音频"};var status=Text("视频标题与 UP 主将作为歌曲信息",12,.55);
        var list=new ListView{Height=280,SelectionMode=ListViewSelectionMode.Single,ItemTemplate=TrackTemplate()};
        var playNow=new Button{Content="播放",IsEnabled=false};var append=new Button{Content="加入播放列表",IsEnabled=false};
        List<Track> tracks=[];
        var d=CreateSheet("B 站音频",Column(input,read,list,Row(playNow,append),status));openDialog=d;
        d.Closed+=(_,_)=>cancel.Cancel();
        read.Click+=async(_,_)=>{
            read.IsEnabled=false;playNow.IsEnabled=append.IsEnabled=false;status.Text="正在读取视频信息…";
            try{string art=await RandomAccountCover(cancel.Token);tracks=await BiliAudio.Read(input.Text,art,cancel.Token);list.ItemsSource=tracks;list.SelectedIndex=0;playNow.IsEnabled=append.IsEnabled=tracks.Count>0;status.Text=art.Length>0?"封面来自你的网易云歌曲":"登录网易云并打开歌单后，可使用歌曲封面";}
            catch(OperationCanceledException){}catch(Exception e){status.Text=e.Message;}finally{read.IsEnabled=true;}
        };
        append.Click+=(_,_)=>{if(list.SelectedItem is not Track t)return;if(!state.Queue.Any(x=>x.Bvid==t.Bvid&&x.BiliPage==t.BiliPage))state.Queue.Add(t);Storage.Save(state);status.Text="已加入播放列表";};
        playNow.Click+=async(_,_)=>{
            if(list.SelectedItem is not Track t)return;
            int index=state.Queue.FindIndex(x=>x.Bvid==t.Bvid&&x.BiliPage==t.BiliPage);
            if(index<0){state.Queue.Add(t);index=state.Queue.Count-1;}d.Hide();notice.Text="正在缓冲 B 站音轨…";await LoadTrack(index,true);
        };
        try{await d.ShowAsync();}finally{if(openDialog==d)openDialog=null;}
    }
}
