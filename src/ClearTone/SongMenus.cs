using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
namespace ClearTone;

public sealed partial class MainPage
{
    // Keep real ListView virtualization. Row buttons bind to the recycled row's current Track.
    ListView SongList(IEnumerable<Track> tracks,Action<string>? status=null){
        var list=new ListView{ItemsSource=tracks,SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=false,ItemTemplate=SongRowTemplate()};
        list.ContainerContentChanging+=(_,e)=>{
            if(e.InRecycleQueue)return;
            if(e.ItemContainer.ContentTemplateRoot is FrameworkElement root&&root.FindName("SongMenu") is Button menu){
                menu.Tag=e.Item;
                if(menu.DataContext is not SongMenuBinding){menu.DataContext=new SongMenuBinding();menu.Click+=async(_,_)=>{if(menu.Tag is Track track)await OpenSongMenu(menu,track,status);};}
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(menu,((Track)e.Item).Name+"，更多操作");
            }
        };
        list.DoubleTapped+=async(_,e)=>{
            for(DependencyObject? source=e.OriginalSource as DependencyObject;source!=null;source=VisualTreeHelper.GetParent(source))if(source is Button)return;
            if(list.SelectedItem is Track track)try{await PlaySelection(track);}catch(Exception ex){ShowError(ex);}
        };
        return list;
    }
    sealed class SongMenuBinding {}
    static DataTemplate SongRowTemplate()=>(DataTemplate)XamlReader.Load("""
    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Grid Padding="2,7" ColumnSpacing="14">
        <Grid.ColumnDefinitions><ColumnDefinition Width="44"/><ColumnDefinition Width="*"/><ColumnDefinition Width="36"/></Grid.ColumnDefinitions>
        <Border CornerRadius="7" Width="44" Height="44" Background="#15FFFFFF"><Image Stretch="UniformToFill"><Image.Source><BitmapImage UriSource="{Binding Cover}" DecodePixelWidth="88"/></Image.Source></Image></Border>
        <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="3">
          <TextBlock Text="{Binding Name}" FontSize="14" FontFamily="FONT" Foreground="#FFF5EE" TextTrimming="CharacterEllipsis"/>
          <TextBlock Text="{Binding Detail}" FontSize="11" FontFamily="FONT" Foreground="#B6ADAFC0" TextTrimming="CharacterEllipsis"/>
        </StackPanel>
        <Button x:Name="SongMenu" Grid.Column="2" Width="34" Height="34" Padding="0" Background="Transparent" VerticalAlignment="Center"><FontIcon Glyph="&#xE712;" FontSize="17"/></Button>
      </Grid>
    </DataTemplate>
    """.Replace("FONT",AppFont));
    async Task OpenSongMenu(Button anchor,Track track,Action<string>? status){
        var flyout=new MenuFlyout();
        void Report(string message){notice.Text=message;status?.Invoke(message);_=DismissNotice(message);}
        MenuFlyoutItem Add(string text,string glyph,Func<Task> action){
            var item=new MenuFlyoutItem{Text=text,Icon=new FontIcon{Glyph=glyph,FontSize=16}};
            item.Click+=async(_,_)=>{try{await action();}catch(Exception e){Report(e.Message);Storage.Log(e);}};flyout.Items.Add(item);return item;
        }
        Add("播放","\uE768",()=>PlaySelection(track));
        Add("加入播放列表","\uE710",()=>{AppendSelection([track]);Report("已加入播放列表");return Task.CompletedTask;});
        var local=new MenuFlyoutSubItem{Text="收藏到本地歌单",Icon=new FontIcon{Glyph="\uE8F1",FontSize=16}};
        var library=new MenuFlyoutItem{Text="音乐库"};library.Click+=(_,_)=>{shelf.Add([track]);Report("已收藏到音乐库");};local.Items.Add(library);
        foreach(var playlist in shelf.Playlists){var p=playlist;var item=new MenuFlyoutItem{Text=p.Name};item.Click+=(_,_)=>{if(!p.Tracks.Any(t=>t.Key==track.Key))p.Tracks.Add(track);shelf.Add([track]);Report("已收藏到 "+p.Name);};local.Items.Add(item);}flyout.Items.Add(local);
        if(track.NetEaseId.Length>0){
            var cloud=new MenuFlyoutSubItem{Text="收藏到网易云歌单",Icon=new FontIcon{Glyph="\uE8B7",FontSize=16},IsEnabled=accountLogged};
            var loading=new MenuFlyoutItem{Text="正在读取…",IsEnabled=false};cloud.Items.Add(loading);flyout.Items.Add(cloud);
            var heart=Add("喜欢","\uEB51",async()=>{
                var old=await service.Call("liked",new{id=track.NetEaseId});bool next=!old.GetProperty("liked").GetBoolean();
                await service.Call("like",new{id=track.NetEaseId,liked=next});if(current?.Key==track.Key)await UpdateLike(loadVersion);Report(next?"已喜欢":"已取消喜欢");
            });heart.IsEnabled=accountLogged;
            flyout.ShowAt(anchor);
            if(!accountLogged)return;
            try{
                var d=await service.Call("liked",new{id=track.NetEaseId});heart.Text=d.GetProperty("liked").GetBoolean()?"取消喜欢":"喜欢";
                int offset=0;cloud.Items.Clear();
                async Task LoadMore(){
                    var data=await service.Call("playlists",new{offset});
                    foreach(var value in data.GetProperty("items").EnumerateArray())if(value.GetProperty("owned").GetBoolean()){
                        var p=ReadPlaylist(value);var item=new MenuFlyoutItem{Text=p.Name};item.Click+=async(_,_)=>{try{await service.Call("collect",new{id=track.NetEaseId,playlistId=p.Id});Report("已收藏到 "+p.Name);}catch(Exception e){Report(e.Message);}};cloud.Items.Add(item);
                    }
                    offset+=data.GetProperty("items").GetArrayLength();
                    if(data.GetProperty("more").GetBoolean()){var more=new MenuFlyoutItem{Text="加载更多歌单"};more.Click+=async(_,_)=>{cloud.Items.Remove(more);try{await LoadMore();flyout.ShowAt(anchor);}catch(Exception e){Report(e.Message);}};cloud.Items.Add(more);}
                    if(cloud.Items.Count==0)cloud.Items.Add(new MenuFlyoutItem{Text="暂无自己创建的歌单",IsEnabled=false});
                }
                await LoadMore();
            }catch(Exception e){cloud.Items.Clear();cloud.Items.Add(new MenuFlyoutItem{Text="歌单读取失败，请重试",IsEnabled=false});Storage.Log(e);}
        }else flyout.ShowAt(anchor);
    }
}
