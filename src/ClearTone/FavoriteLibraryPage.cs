using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
namespace ClearTone;

public sealed partial class MainPage
{
    bool biliFolderView;
    void ShowPlaylistSources(Grid content,TextBlock message,CancellationToken token){
        content.Children.Clear();
        var panel=new Grid{RowSpacing=12};
        panel.RowDefinitions.Add(new(){Height=GridLength.Auto});
        panel.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var area=new Grid();Grid.SetRow(area,1);panel.Children.Add(area);
        var local=ActionButton("本地歌单",()=>{});var bili=ActionButton("B 站收藏夹",()=>{});
        local.Click+=(_,_)=>Choose(false);bili.Click+=(_,_)=>Choose(true);
        var sources=Row(local,bili);sources.Spacing=2;
        var strip=new Border{Child=sources,Padding=new Thickness(3),CornerRadius=new CornerRadius(12),
            HorizontalAlignment=HorizontalAlignment.Left,Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(18,255,240,245))};
        panel.Children.Add(strip);content.Children.Add(panel);
        void Choose(bool online){
            searchVersion++;biliFolderView=online;
            var selected=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(54,226,143,172));
            var clear=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            local.Background=online?clear:selected;bili.Background=online?selected:clear;
            local.Opacity=online?.7:1;bili.Opacity=online?1:.7;
            if(online)ShowBiliFolders(area,message,token);else ShowLocalPlaylists(area,message,token);
        }
        Choose(biliFolderView);
    }
    void ShowBiliFolders(Grid area,TextBlock message,CancellationToken token){
        area.Children.Clear();var grid=new Grid{RowSpacing=12};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});area.Children.Add(grid);
        var input=SearchInput("UID、个人空间或公开收藏夹链接");input.Text=shelf.BiliFavoriteSource;
        var list=new ListView{ItemsSource=shelf.BiliFolders.ToList(),ItemTemplate=PlaylistTemplate(),IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.None};
        Grid.SetRow(list,1);grid.Children.Add(list);
        bool Current(int version)=>version==searchVersion&&!token.IsCancellationRequested&&area.Children.Contains(grid);
        async Task Open(string id){
            int version=++searchVersion;message.Text="正在读取收藏夹…";
            try{
                var first=await BiliFavorites.Read(id,1,token);if(!Current(version))return;
                shelf.BiliFolders.RemoveAll(f=>f.Id==id);shelf.BiliFolders.Insert(0,first.Folder);shelf.Save();
                ShowBiliFolder(area,message,first,token);
            }catch(Exception e){if(Current(version))message.Text=e.Message;}
        }
        async Task Browse(){
            int version=++searchVersion;
            try{
                var (id,user)=BiliFavorites.Parse(input.Text);
                if(!user){await Open(id);return;}
                message.Text="正在读取公开收藏夹…";var rows=await BiliFavorites.ForUser(id,token);if(!Current(version))return;
                shelf.BiliFavoriteSource=id;shelf.Save();list.ItemsSource=rows;
                message.Text=rows.Count==0?"这个账号没有公开收藏夹":$"{rows.Count} 个公开收藏夹";
            }catch(Exception e){if(Current(version))message.Text=e.Message;}
        }
        list.ItemClick+=async(_,e)=>{if(e.ClickedItem is BiliFolder folder)await Open(folder.Id);};
        var header=new Grid{ColumnSpacing=10};header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var browse=AsyncButton("打开",Browse);header.Children.Add(input);header.Children.Add(browse);Grid.SetColumn(browse,1);grid.Children.Add(header);
        input.KeyDown+=async(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter)await Browse();};
        message.Text=shelf.BiliFolders.Count==0?"首次打开后，收藏夹会保存在这里":"最近打开的收藏夹";
    }
    void ShowBiliFolder(Grid area,TextBlock message,BiliFolderPage first,CancellationToken token){
        area.Children.Clear();var grid=new Grid{RowSpacing=12};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        area.Children.Add(grid);var tracks=new ObservableCollection<Track>(first.Tracks.DistinctBy(t=>t.Key));var keys=tracks.Select(t=>t.Key).ToHashSet();
        var list=SongList(tracks,text=>message.Text=text);grid.Children.Add(list);Grid.SetRow(list,1);
        int pageNumber=1,skipped=first.Skipped,version=searchVersion;bool more=first.More,busy=false;
        var next=ActionButton("下一页",()=>{});var all=Primary(ActionButton("整夹加入队列",()=>{}));
        next.Click+=async(_,_)=>await Read(false);all.Click+=async(_,_)=>await Read(true);
        var back=Icon("\uE72B","返回收藏夹");back.Click+=(_,_)=>{searchVersion++;ShowBiliFolders(area,message,token);};
        var title=Text(first.Folder.Name,18,.95);title.TextTrimming=TextTrimming.CharacterEllipsis;title.TextWrapping=TextWrapping.NoWrap;title.VerticalAlignment=VerticalAlignment.Center;
        var header=new Grid{ColumnSpacing=8};foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})header.ColumnDefinitions.Add(new(){Width=width});
        var actions=Row(next,all);header.Children.Add(back);header.Children.Add(title);Grid.SetColumn(title,1);header.Children.Add(actions);Grid.SetColumn(actions,2);grid.Children.Add(header);
        bool Attached()=>version==searchVersion&&!token.IsCancellationRequested&&area.Children.Contains(grid)&&grid.IsLoaded;
        void Paint(){next.Visibility=more?Visibility.Visible:Visibility.Collapsed;message.Text=$"{tracks.Count} 条视频"+(skipped>0?$" · 已略过 {skipped} 条失效或非视频内容":"");}
        async Task Read(bool enqueue){
            if(busy)return;busy=true;all.IsEnabled=next.IsEnabled=false;
            try{
                while(more){
                    message.Text=$"正在读取第 {pageNumber+1} 页…";
                    var result=await BiliFavorites.Read(first.Folder.Id,pageNumber+1,token);if(!Attached())return;
                    var added=result.Tracks.Where(t=>keys.Add(t.Key)).ToList();
                    if(result.More&&added.Count==0&&result.Skipped==0)throw new IOException("收藏夹分页没有推进，请返回后重试。");
                    foreach(var track in added)tracks.Add(track);
                    pageNumber++;more=result.More;skipped+=result.Skipped;if(!enqueue)break;
                }
                Paint();if(enqueue&&tracks.Count>0){AppendSelection(tracks);message.Text=$"已加入 {tracks.Count} 条视频"+(skipped>0?$" · 略过 {skipped} 条":"");}
            }catch(Exception e){if(Attached())message.Text=e.Message;}finally{busy=false;all.IsEnabled=next.IsEnabled=true;}
        }
        Paint();
    }
}
