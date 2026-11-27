using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.Storage.Pickers;
namespace ClearTone;

public sealed partial class MainPage
{
    sealed record BoundLyrics(string Title,string Lrc,string Translation="",string Romanization="",JsonElement? Words=null);
    static string BindingPath(Track track)=>Path.ChangeExtension(PracticeDocument.PathFor(track),".lyrics.json");
    static void StoreBinding(Track track,BoundLyrics binding){
        if(Lyrics.Parse(binding.Lrc).Count==0)throw new IOException("没有可用的同步歌词");
        var file=BindingPath(track);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(binding,Storage.Json));File.Move(file+".tmp",file,true);
    }
    async Task StartVideoPractice(Track track){
        openDialog?.Hide();
        if(current?.Key!=track.Key||!trackReady)await PlaySelection(track);
        if(current?.Key!=track.Key||!trackReady)return;
        await Navigate("practice");
    }
    async Task<BoundLyrics?> PickVideoLrc(){
        var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(App.Main));
        picker.FileTypeFilter.Add(".lrc");var file=await picker.PickSingleFileAsync();
        return file==null?null:new(file.DisplayName,await File.ReadAllTextAsync(file.Path));
    }
    async Task BindVideoLyrics(Track track){
        openDialog?.Hide();await Task.Yield();
        using var cancel=new CancellationTokenSource();var token=cancel.Token;
        var input=SearchInput("搜索网易云歌曲或歌手");input.Text=track.Name;
        var status=Text("",12,.7);
        var results=new ListView{Height=240,DisplayMemberPath="Name"};
        int queryVersion=0;
        async Task Search(){
            int version=++queryVersion;var query=input.Text.Trim();results.ItemsSource=null;if(query.Length==0)return;
            status.Text="正在搜索…";
            try{var data=await service.Call("search",new{source="netease",kind="songs",query,offset=0},token);
                if(token.IsCancellationRequested||version!=queryVersion)return;
                var rows=MusicService.Tracks(data).Select(t=>new BindingCandidate(t.NetEaseId,t.Name+" · "+t.Artist)).ToList();results.ItemsSource=rows;
                status.Text=rows.Count==0?"没有找到歌曲，可导入 LRC":"选择与这段视频对应的录音版本";
            }catch(Exception e){if(!token.IsCancellationRequested&&version==queryVersion)status.Text=e.Message;}
        }
        bool binding=false;
        async Task Bind(bool local){
            if(binding)return;binding=true;
            try{
                BoundLyrics? value;
                if(local)value=await PickVideoLrc();
                else{
                    if(results.SelectedItem is not BindingCandidate song){status.Text="请先选一首歌";return;}
                    var data=await service.Call("lyrics",new{id=song.Id},token);
                    string Field(string key)=>data.TryGetProperty(key,out var field)?field.GetString()??"":"";
                    value=new(song.Name,Field("lrc"),Field("translation"),Field("romanization"),data.TryGetProperty("wordTimeline",out var times)?times.Clone():null);
                }
                if(value==null||token.IsCancellationRequested)return;
                StoreBinding(track,value);if(current?.Key==track.Key)await LoadLyrics(current,loadVersion,trackCancel.Token);
                if(!token.IsCancellationRequested)await StartVideoPractice(track);
            }catch(Exception e){if(!token.IsCancellationRequested)status.Text=e.Message;}finally{binding=false;}
        }
        input.KeyDown+=async(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter)await Search();};
        var search=AsyncButton("搜索",Search);var grid=new Grid{ColumnSpacing=8};
        grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        grid.Children.Add(input);grid.Children.Add(search);Grid.SetColumn(search,1);
        var actions=Row(AsyncButton("导入 LRC",()=>Bind(true)),Primary(AsyncButton("绑定并跟唱",()=>Bind(false))));
        actions.HorizontalAlignment=HorizontalAlignment.Right;
        try{await Dialog("为视频绑定歌词",Column(grid,results,status,actions));}finally{cancel.Cancel();}
    }
    sealed record BindingCandidate(string Id,string Name);
}
