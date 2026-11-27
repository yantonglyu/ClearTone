using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClearTone;


public sealed record BiliFolderPage(BiliFolder Folder,List<Track> Tracks,bool More,int Skipped);

public static class BiliFavorites
{
    static readonly HttpClient client=new(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(18)};
    public static (string Id,bool User) Parse(string input){
        input=input.Trim();
        if(Regex.IsMatch(input,@"^\d{1,19}$"))return(input,true);
        var direct=Regex.Match(input,@"^ml(\d{1,19})$",RegexOptions.IgnoreCase);
        if(direct.Success)return(direct.Groups[1].Value,false);
        if(Uri.TryCreate(input,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http" &&
            (uri.Host=="bilibili.com"||uri.Host.EndsWith(".bilibili.com",StringComparison.OrdinalIgnoreCase))){
            var folder=Regex.Match(uri.PathAndQuery,@"(?:/ml|[?&]fid=)(\d{1,19})(?=[/?&#]|$)");
            if(folder.Success)return(folder.Groups[1].Value,false);
            var user=Regex.Match(uri.AbsolutePath,@"^/(\d{1,19})(?:/|$)");
            if(uri.Host=="space.bilibili.com"&&user.Success)return(user.Groups[1].Value,true);
        }
        throw new ArgumentException("请输入 UID、个人空间网址或公开收藏夹链接（也支持 ml 编号）");
    }
    static async Task<JsonElement> Get(string path,CancellationToken token){
        using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.bilibili.com/x/v3/fav/"+path);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0");req.Headers.Referrer=new("https://www.bilibili.com/");
        using var response=await client.SendAsync(req,token);response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var root=doc.RootElement;
        if(root.GetProperty("code").GetInt32()!=0)throw new IOException("收藏夹暂不可访问，请确认已公开后重试。");
        return root.GetProperty("data").Clone();
    }
    static string Field(JsonElement obj,string key)=>obj.ValueKind==JsonValueKind.Object&&obj.TryGetProperty(key,out var v)?v.ToString():"";
    static BiliFolder Folder(JsonElement item)=>new(Field(item,"id"),Field(item,"title"),Field(item,"cover"),Field(item,"media_count")+" 条 · B 站");
    public static async Task<List<BiliFolder>> ForUser(string uid,CancellationToken token){
        var data=await Get("folder/created/list-all?up_mid="+Uri.EscapeDataString(uid),token);
        return data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("list",out var list)&&list.ValueKind==JsonValueKind.Array?
            list.EnumerateArray().Select(Folder).ToList():[];
    }
    public static async Task<BiliFolderPage> Read(string id,int page,CancellationToken token){
        var data=await Get($"resource/list?media_id={Uri.EscapeDataString(id)}&pn={page}&ps=20&platform=web",token);
        var tracks=new List<Track>();int skipped=0;
        if(data.TryGetProperty("medias",out var rows)&&rows.ValueKind==JsonValueKind.Array)
            foreach(var item in rows.EnumerateArray()){
                var bv=Field(item,"bvid");var title=Field(item,"title");
                if(Field(item,"type")!="2"||!Regex.IsMatch(bv,@"^BV[A-Za-z0-9]{10}$")||title=="已失效视频"||
                    (int.TryParse(Field(item,"attr"),out int attr)&&(attr&1)!=0)){skipped++;continue;}
                tracks.Add(new Track{Bvid=bv,Name=title,Cover=Field(item,"cover"),Artist=item.TryGetProperty("upper",out var upper)?Field(upper,"name"):"",FullDuration=int.TryParse(Field(item,"duration"),out int seconds)?seconds:0});
            }
        return new(Folder(data.GetProperty("info")),tracks,data.GetProperty("has_more").GetBoolean(),skipped);
    }
}
