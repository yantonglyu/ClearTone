using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage
{
    int librarySection;
    string catalogQuery="";
    Grid CatalogSearch(Grid content,TextBlock message,CancellationToken token){
        var search=SearchInput("歌曲、歌手或 UP 主");search.Text=catalogQuery;
        search.Background=new SolidColorBrush(Color.FromArgb(0,0,0,0));
        search.Resources["TextControlBackground"]=search.Background;
        var filter=ActionButton("",()=>{});filter.Background=new SolidColorBrush(Color.FromArgb(0,0,0,0));filter.Padding=new(12,6,12,6);
        void PaintFilter(){var label=(catalogSource=="netease"?"网易云":catalogSource=="bilibili"?"B 站":"本地")+" · "+(catalogKind=="artists"?"作者":"歌曲");filter.Content=Row(Text(label,12,.8),new FontIcon{Glyph="\uE70D",FontSize=9,Opacity=.65});Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(filter,"搜索范围："+label);}
        var menu=new MenuFlyout();
        var sourceMenu=new MenuFlyoutSubItem{Text="搜索来源"};
        foreach(var entry in new[]{("netease","网易云"),("bilibili","B 站"),("local","本地")}){var item=new RadioMenuFlyoutItem{Text=entry.Item2,GroupName="CatalogSource",IsChecked=catalogSource==entry.Item1};item.Click+=(_,_)=>{catalogSource=entry.Item1;PaintFilter();};sourceMenu.Items.Add(item);}
        var kindMenu=new MenuFlyoutSubItem{Text="搜索类型"};
        foreach(var entry in new[]{("songs","歌曲 / 视频"),("artists","作者")}){var item=new RadioMenuFlyoutItem{Text=entry.Item2,GroupName="CatalogKind",IsChecked=catalogKind==entry.Item1};item.Click+=(_,_)=>{catalogKind=entry.Item1;PaintFilter();};kindMenu.Items.Add(item);}
        menu.Items.Add(sourceMenu);menu.Items.Add(kindMenu);filter.Flyout=menu;PaintFilter();
        async Task Submit(){catalogQuery=search.Text;await SearchCatalog(search.Text,content,message,token);}
        search.KeyDown+=async(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter)await Submit();};
        var submit=Icon("\uE721","搜索");submit.Click+=async(_,_)=>await Submit();submit.Margin=new(0,0,4,0);
        var row=new Grid{Background=new SolidColorBrush(Color.FromArgb(17,255,245,238)),CornerRadius=new(12),MinHeight=46};
        row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.Children.Add(search);row.Children.Add(filter);Grid.SetColumn(filter,1);row.Children.Add(submit);Grid.SetColumn(submit,2);return row;
    }
    void BuildLibraryNavigation(Grid bar,Grid content,TextBlock message,CancellationToken token,Button account){
        bar.ColumnSpacing=16;bar.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};bar.Children.Add(tabs);
        var tabButtons=new List<Button>();
        void Select(int index){
            librarySection=index;searchVersion++;
            for(int i=0;i<tabButtons.Count;i++){var button=tabButtons[i];button.Opacity=i==index?1:.65;button.Foreground=new SolidColorBrush(i==index?Color.FromArgb(255,248,205,220):Color.FromArgb(255,255,245,238));button.BorderBrush=new SolidColorBrush(i==index?Color.FromArgb(235,231,169,186):Color.FromArgb(0,0,0,0));Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,new[]{"收藏","队列","最近播放","歌单"}[i]+(i==index?"，已选中":""));}
            if(index==3){ShowLocalPlaylists(content,message,token);return;}
            ShowCatalogTracks(index==0?shelf.Tracks:index==1?state.Queue:shelf.History,new[]{"收藏的音乐","播放队列","最近播放"}[index],content,message,token);
        }
        for(int i=0;i<4;i++){int index=i;var button=ActionButton(new[]{"收藏","队列","最近","歌单"}[i],()=>Select(index));button.Background=new SolidColorBrush(Color.FromArgb(0,0,0,0));button.CornerRadius=new(0);button.BorderThickness=new(0,0,0,2);button.Padding=new(10,10,10,10);tabButtons.Add(button);tabs.Children.Add(button);}
        var cloud=Icon("\uE753",accountLogged?"我的网易云":"登录网易云");cloud.Click+=async(_,_)=>{await Library();await LoadAccount();if(!token.IsCancellationRequested)BuildCatalog(false);};
        var add=Icon("\uE710","添加音乐");var addMenu=new MenuFlyout();
        var import=new MenuFlyoutItem{Text="导入本地音乐",Icon=new FontIcon{Glyph="\uE8B5"}};import.Click+=async(_,_)=>await ImportLocal();
        var bili=new MenuFlyoutItem{Text="打开 B 站链接",Icon=new FontIcon{Glyph="\uE71B"}};bili.Click+=async(_,_)=>await BiliLibrary();
        var save=new MenuFlyoutItem{Text="将队列存为歌单",Icon=new FontIcon{Glyph="\uE8F1"},IsEnabled=state.Queue.Count>0};save.Click+=async(_,_)=>await SaveMixed(state.Queue.ToList());
        addMenu.Opening+=(_,_)=>save.IsEnabled=state.Queue.Count>0;
        addMenu.Items.Add(import);addMenu.Items.Add(bili);addMenu.Items.Add(new MenuFlyoutSeparator());addMenu.Items.Add(save);add.Flyout=addMenu;
        var tools=Row(cloud,add);tools.Spacing=4;tools.HorizontalAlignment=HorizontalAlignment.Right;bar.Children.Add(tools);Grid.SetColumn(tools,1);
        Select(librarySection);
    }
    static StackPanel LibraryEmpty(string title,string description){
        var words=Column(Text(title,21,.85),Text(description,12,.65));words.Spacing=10;words.HorizontalAlignment=HorizontalAlignment.Center;words.VerticalAlignment=VerticalAlignment.Center;
        foreach(var child in words.Children.OfType<TextBlock>())child.TextAlignment=TextAlignment.Center;return words;
    }
    sealed record LocalPlaylistRow(int Index,string Name,string Cover,string Subtitle);
    void ShowLocalPlaylists(Grid content,TextBlock message,CancellationToken token){
        content.Children.Clear();
        if(shelf.Playlists.Count==0){content.Children.Add(LibraryEmpty("把喜欢的声音放在一起","歌曲右侧的 ··· 可以收藏到歌单，也可以将播放队列存为歌单。"));message.Text="本地歌单可以混合不同来源的歌曲";return;}
        var rows=shelf.Playlists.Select((p,i)=>new LocalPlaylistRow(i,p.Name,p.Tracks.FirstOrDefault(t=>t.Cover.Length>0)?.Cover??"",p.Tracks.Count+" 首 · 本地歌单")).ToList();
        var list=new ListView{ItemsSource=rows,ItemTemplate=PlaylistTemplate(),SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true};
        list.ItemClick+=(_,e)=>{if(token.IsCancellationRequested||e.ClickedItem is not LocalPlaylistRow row)return;var playlist=shelf.Playlists[row.Index];ShowCatalogTracks(playlist.Tracks,playlist.Name,content,message,token);};content.Children.Add(list);message.Text=$"{rows.Count} 张歌单";
    }
}

