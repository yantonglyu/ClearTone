using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage
{
    List<Track> homeDaily=[];
    List<PlaylistItem> homeRecommendations=[];
    DateTime homeCachedAt=DateTime.MinValue;string homeAccount="";
    static Image CatalogCover(string cover,double size){
        var image=new Image{Width=size,Height=size,Stretch=Stretch.UniformToFill};SetCatalogCover(image,cover);return image;
    }
    static void SetCatalogCover(Image image,string cover){if(Uri.TryCreate(cover,UriKind.Absolute,out var uri))image.Source=new BitmapImage(uri){DecodePixelWidth=(int)Math.Clamp(image.Width*2,120,512)};}
    async Task OpenPlaylist(PlaylistItem item,Grid content,TextBlock message,CancellationToken token){
        int version=++searchVersion;message.Text="正在读取 "+item.Name+"…";
        try{var data=await service.Call("playlist",new{id=item.Id},token);if(token.IsCancellationRequested||version!=searchVersion)return;ShowCatalogTracks(MusicService.Tracks(data),item.Name,content,message,token,item.Id);}
        catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested&&version==searchVersion)message.Text=e.Message;}
    }
    void BuildDiscovery(Grid content,TextBlock message,CancellationToken token){
        int buildVersion=homeBuilds;
        if(homeAccount!=accountId||homeCachedAt.Date!=DateTime.Now.Date){homeDaily=[];homeRecommendations=[];homeCachedAt=DateTime.MinValue;homeAccount=accountId;}
        var stack=new StackPanel{Spacing=24};var scroll=new ScrollViewer{Content=stack,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};content.Children.Add(scroll);
        var dailyCover=CatalogCover(homeDaily.FirstOrDefault()?.Cover??shelf.History.FirstOrDefault()?.Cover??current?.Cover??"",142);
        var daily=AsyncButton("今日推荐",async()=>{
            int version=++searchVersion;message.Text="正在读取今日推荐…";
            try{var tracks=homeDaily.Count>0?homeDaily:MusicService.Tracks(await service.Call("daily",null,token));if(token.IsCancellationRequested||version!=searchVersion)return;homeDaily=tracks;ShowCatalogTracks(tracks,"今日推荐",content,message,token,daily:true);}
            catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested&&version==searchVersion)message.Text=e.Message;}
        });
        daily.Padding=new(22);daily.HorizontalContentAlignment=HorizontalAlignment.Stretch;daily.VerticalContentAlignment=VerticalAlignment.Stretch;daily.HorizontalAlignment=HorizontalAlignment.Stretch;daily.VerticalAlignment=VerticalAlignment.Stretch;daily.Background=new SolidColorBrush(Color.FromArgb(34,237,187,204));daily.CornerRadius=new(18);
        var hero=new Grid{ColumnSpacing=22};hero.ColumnDefinitions.Add(new(){Width=GridLength.Auto});hero.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var dailyArt=new Border{CornerRadius=new(12),Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),Child=dailyCover,VerticalAlignment=VerticalAlignment.Center};hero.Children.Add(dailyArt);
        var words=Column(Text(DateTime.Now.ToString("MM / dd"),11,.6),Text("今日推荐",24),Text("每天新的心动",12,.7));words.VerticalAlignment=VerticalAlignment.Center;hero.Children.Add(words);Grid.SetColumn(words,1);daily.Content=hero;
        var recCover=CatalogCover(homeRecommendations.FirstOrDefault()?.Cover??"",64);var recentCover=CatalogCover(shelf.History.FirstOrDefault()?.Cover??"",64);
        Button Entry(string title,string caption,Image image,Func<Task> action){var button=AsyncButton(title,action);button.Padding=new(14);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.VerticalAlignment=VerticalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var row=new Grid{ColumnSpacing=16};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.Children.Add(new Border{CornerRadius=new(9),Background=new SolidColorBrush(Color.FromArgb(18,255,255,255)),Child=image});var labels=Column(Text(title,16),Text(caption,11,.58));labels.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(labels);Grid.SetColumn(labels,1);button.Content=row;return button;}
        var recommendations=Entry("推荐歌单","发现新的喜欢",recCover,async()=>{
            int version=++searchVersion;
            if(homeRecommendations.Count>0){ShowRecommendationGrid(homeRecommendations,content,message,token);return;}
            try{var data=await service.Call(accountLogged?"recommendations":"discovery-playlists",null,token);if(token.IsCancellationRequested||version!=searchVersion||!content.IsLoaded||!content.Children.Contains(scroll))return;ShowRecommendationGrid(data.GetProperty("items").EnumerateArray().Select(ReadPlaylist).ToList(),content,message,token);}catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested&&version==searchVersion&&content.IsLoaded)message.Text=e.Message;}
        });
        var recent=Entry("继续听",shelf.History.FirstOrDefault()?.Name??"回到熟悉的声音",recentCover,()=>{searchVersion++;ShowCatalogTracks(shelf.History,"最近播放",content,message,token);return Task.CompletedTask;});
        var group=new Grid{ColumnSpacing=16,RowSpacing=14,Height=202};group.ColumnDefinitions.Add(new(){Width=new(1.3,GridUnitType.Star)});group.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});group.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});group.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});group.Children.Add(daily);Grid.SetRowSpan(daily,2);group.Children.Add(recommendations);Grid.SetColumn(recommendations,1);group.Children.Add(recent);Grid.SetColumn(recent,1);Grid.SetRow(recent,1);stack.Children.Add(group);
        var title=Text("为你挑选",19,.9);stack.Children.Add(title);var cards=new Grid{ColumnSpacing=16,RowSpacing=20};stack.Children.Add(cards);
        void Adapt(){
            bool compact=content.ActualHeight>0&&content.ActualHeight<380;
            bool narrow=content.ActualWidth>0&&content.ActualWidth<620;
            group.Height=narrow?198:compact?128:202;group.RowSpacing=compact||narrow?10:14;stack.Spacing=compact?12:24;
            group.ColumnDefinitions[0].Width=new(narrow?1:1.3,GridUnitType.Star);
            group.RowDefinitions[0].Height=narrow?new(128):new(1,GridUnitType.Star);group.RowDefinitions[1].Height=narrow?new(60):new(1,GridUnitType.Star);
            Grid.SetColumnSpan(daily,narrow?2:1);Grid.SetRowSpan(daily,narrow?1:2);Grid.SetColumn(recommendations,narrow?0:1);Grid.SetRow(recommendations,narrow?1:0);
            double art=compact||narrow?92:content.ActualWidth<760?96:142;dailyCover.Width=dailyCover.Height=art;hero.ColumnSpacing=compact||narrow?16:22;daily.Padding=new(compact||narrow?16:22);
            recCover.Width=recCover.Height=recentCover.Width=recentCover.Height=compact||narrow?38:64;
            recommendations.Padding=recent.Padding=new(compact||narrow?10:14);
            foreach(var button in new[]{recommendations,recent})if(button.Content is Grid row&&row.Children.OfType<StackPanel>().FirstOrDefault() is StackPanel labels){labels.Spacing=compact||narrow?2:8;row.ColumnSpacing=narrow?10:16;foreach(var text in labels.Children.OfType<TextBlock>()){text.MaxLines=1;text.TextTrimming=TextTrimming.CharacterEllipsis;}if(labels.Children.Count>1)labels.Children[1].Visibility=narrow&&content.ActualWidth<440?Visibility.Collapsed:Visibility.Visible;}
            title.FontSize=compact?17:19;
        }
        SizeChangedEventHandler adapt=(_,_)=>Adapt();content.SizeChanged+=adapt;scroll.Unloaded+=(_,_)=>content.SizeChanged-=adapt;Adapt();
        void PaintCards(){FillRecommendationGrid(cards,homeRecommendations,content,message,token);SetCatalogCover(recCover,homeRecommendations.FirstOrDefault()?.Cover??"");}
        PaintCards();message.Text=accountLogged?"日推与歌单来自网易云":"登录网易云开启每日推荐";
        if(ready)_=Populate();
        async Task Populate(){
            if(DateTime.Now-homeCachedAt<TimeSpan.FromMinutes(15))return;
            string requestedAccount=accountId;
            bool Attached()=>!token.IsCancellationRequested&&buildVersion==homeBuilds&&accountId==requestedAccount&&content.IsLoaded&&content.Children.Contains(scroll);
            async Task Daily(){if(!accountLogged)return;try{var data=await service.Call("daily",null,token);if(!Attached())return;homeDaily=MusicService.Tracks(data);SetCatalogCover(dailyCover,homeDaily.FirstOrDefault()?.Cover??"");}catch(OperationCanceledException){}catch(Exception e){Storage.Log(e);}}
            async Task Recommendations(){
                var collected=new List<PlaylistItem>();
                foreach(string route in accountLogged?new[]{"recommendations","discovery-playlists"}:new[]{"discovery-playlists"}){
                    try{var data=await service.Call(route,null,token);if(!Attached())return;collected.AddRange(data.GetProperty("items").EnumerateArray().Select(ReadPlaylist));homeRecommendations=collected.DistinctBy(p=>p.Id).Take(30).ToList();PaintCards();}
                    catch(OperationCanceledException){return;}catch(Exception e){Storage.Log(e);}
                }
                if(Attached()){if(homeRecommendations.Count>0)homeCachedAt=DateTime.Now;else message.Text="推荐暂不可用，可以先搜索或打开音乐库";}
            }
            await Task.WhenAll(Daily(),Recommendations());
        }
    }
    void ShowRecommendationGrid(List<PlaylistItem> items,Grid content,TextBlock message,CancellationToken token){
        searchVersion++;content.Children.Clear();var grid=new Grid{ColumnSpacing=16,RowSpacing=20};var scroll=new ScrollViewer{Content=grid,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};content.Children.Add(scroll);FillRecommendationGrid(grid,items,content,message,token);message.Text=$"{items.Count} 张推荐歌单";
    }
    void FillRecommendationGrid(Grid grid,List<PlaylistItem> items,Grid content,TextBlock message,CancellationToken token){
        grid.Children.Clear();grid.RowDefinitions.Clear();grid.ColumnDefinitions.Clear();
        var buttons=new List<Button>();
        foreach(var item in items){
            var button=AsyncButton(item.Name,()=>OpenPlaylist(item,content,message,token));button.Background=new SolidColorBrush(Color.FromArgb(0,0,0,0));button.Padding=new(0);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.VerticalAlignment=VerticalAlignment.Top;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var image=CatalogCover(item.Cover,160);image.HorizontalAlignment=HorizontalAlignment.Stretch;var cover=new Border{CornerRadius=new(12),Background=new SolidColorBrush(Color.FromArgb(18,255,255,255)),Child=image};var name=Text(item.Name,13,.9);name.MaxLines=2;name.TextTrimming=TextTrimming.CharacterEllipsis;var tile=Column(cover,name);tile.Spacing=9;button.Content=tile;grid.Children.Add(button);buttons.Add(button);
            button.SizeChanged+=(_,e)=>{if(e.NewSize.Width>0){image.Width=e.NewSize.Width;image.Height=e.NewSize.Width;}};
        }
        void Arrange(double width){int columns=Math.Clamp((int)((Math.Max(1,width)+16)/180),2,6);if(grid.ColumnDefinitions.Count==columns&&grid.RowDefinitions.Count==(int)Math.Ceiling(buttons.Count/(double)columns))return;grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();for(int i=0;i<columns;i++)grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});for(int i=0;i<Math.Ceiling(buttons.Count/(double)columns);i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});for(int i=0;i<buttons.Count;i++){Grid.SetColumn(buttons[i],i%columns);Grid.SetRow(buttons[i],i/columns);}}
        // Replace one resize subscription per population, avoiding handlers retaining old tiles.
        if(grid.Tag is SizeChangedEventHandler previous)grid.SizeChanged-=previous;
        SizeChangedEventHandler resize=(_,e)=>Arrange(e.NewSize.Width);grid.Tag=resize;grid.SizeChanged+=resize;Arrange(grid.ActualWidth>0?grid.ActualWidth:workspace.ActualWidth);
    }
}
