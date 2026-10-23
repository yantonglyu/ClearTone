using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClearTone;

public static class BiliAudio
{
    static readonly HttpClient http=new(new HttpClientHandler{AutomaticDecompression=System.Net.DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(90)};
    const string Agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36";
    static JsonElement Embedded(string html,string marker){
        var match=Regex.Match(html,Regex.Escape(marker)+@"\s*=\s*");
        if(!match.Success)throw new IOException("视频页面未提供可读取的音频信息");
        var reader=new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(html[(match.Index+match.Length)..]));
        using var doc=JsonDocument.ParseValue(ref reader);return doc.RootElement.Clone();
    }
    static async Task<JsonElement> Page(string id,bool audio,int page,CancellationToken token){
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://www.bilibili.com/video/{Validate(id)}/?p={page}");
        request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue{NoCache=true};
        request.Headers.UserAgent.ParseAdd(Agent);
        using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
        var html=await response.Content.ReadAsStringAsync(token);
        return audio?Embedded(html,"window.__playinfo__").GetProperty("data").Clone():Embedded(html,"window.__INITIAL_STATE__").GetProperty("videoData").Clone();
    }
    static string Validate(string id)=>Regex.IsMatch(id,@"^BV[0-9A-Za-z]{10}$")?id:throw new ArgumentException("请输入完整的 BV 号或视频链接");
    static async Task<JsonElement> Api(string path,string id,CancellationToken token,int page=1){
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.bilibili.com/"+path);
        request.Headers.UserAgent.ParseAdd(Agent);
        request.Headers.Referrer=new Uri("https://www.bilibili.com/video/"+Validate(id)+"/");
        using var response=await http.SendAsync(request,token);
        if((int)response.StatusCode==412)return await Page(id,path.Contains("playurl"),page,token);
        response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var root=doc.RootElement;
        if(root.GetProperty("code").GetInt32()!=0)throw new IOException("B 站暂未提供该音轨："+root.GetProperty("message").GetString());
        return root.GetProperty("data").Clone();
    }
    public static async Task<List<Track>> Read(string input,string cover,CancellationToken token=default){
        var match=Regex.Match(input,@"BV[0-9A-Za-z]{10}");var id=Validate(match.Value);
        var data=await Api("x/web-interface/view?bvid="+id,id,token);
        var result=new List<Track>();int count=data.GetProperty("pages").GetArrayLength();
        foreach(var p in data.GetProperty("pages").EnumerateArray())result.Add(new Track{
            Bvid=id,BiliPage=p.GetProperty("page").GetInt32(),BiliCid=p.GetProperty("cid").GetInt64(),
            Name=data.GetProperty("title").GetString()!+(count>1?" · "+p.GetProperty("part").GetString():""),
            Artist=data.GetProperty("owner").GetProperty("name").GetString()!,Cover=data.TryGetProperty("pic",out var pic)?pic.GetString()?.Replace("http:","https:")??cover:cover,
            FullDuration=p.GetProperty("duration").GetDouble()});
        return result;
    }
    public static async Task<Track> Resolve(Track track){
        Validate(track.Bvid);
        // Catalogue search supplies a video ID. Resolve its first page lazily when
        // selected, rather than fetching metadata for every row in search results.
        if(track.BiliCid<=0){
            var pages=await Read(track.Bvid,track.Cover);
            var selected=pages.FirstOrDefault(p=>p.BiliPage==Math.Max(1,track.BiliPage));
            track=selected??throw new IOException("该视频分 P 暂不可用，请重新搜索或粘贴视频链接");
        }
        var folder=Path.Combine(Storage.Data,"bili-audio");Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,$"{track.Bvid}-{track.BiliPage}.m4a");
        if(File.Exists(file)&&new FileInfo(file).Length>0)return track with{Url=file};
        var data=await Api($"x/player/playurl?bvid={track.Bvid}&cid={track.BiliCid}&fnval=16&fnver=0&fourk=0&qn=16",track.Bvid,default,track.BiliPage);
        if(!data.TryGetProperty("dash",out var dash)||!dash.TryGetProperty("audio",out var tracks)||tracks.GetArrayLength()==0)
            throw new IOException("该视频暂未提供可直接读取的音轨");
        var urls=new List<Uri>();
        foreach(var chosen in tracks.EnumerateArray().OrderBy(x=>x.GetProperty("bandwidth").GetInt32())){
            var candidates=new List<string>{chosen.GetProperty("baseUrl").GetString()!};
            if(chosen.TryGetProperty("backupUrl",out var backups)&&backups.ValueKind==JsonValueKind.Array)candidates.AddRange(backups.EnumerateArray().Select(x=>x.GetString()!));
            foreach(var candidate in candidates){
                if(!Uri.TryCreate(candidate,UriKind.Absolute,out var u))continue;
                if(u.Scheme=="http"&&u.Port==80)u=new UriBuilder(u){Scheme="https",Port=443}.Uri;
                if(u.Scheme=="https"&&u.Port==443&&u.UserInfo.Length==0&&new[]{".bilivideo.com",".bilivideo.cn",".bilivideo.net"}.Any(s=>u.Host.EndsWith(s,StringComparison.OrdinalIgnoreCase))){if(!urls.Contains(u))urls.Add(u);}
            }
        }
        if(urls.Count==0)throw new IOException("B 站暂未提供可用的标准音轨地址");
        HttpResponseMessage? accepted=null;int lastStatus=0;
        foreach(var uri in urls.Take(8)){
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd(Agent);request.Headers.Referrer=new Uri("https://www.bilibili.com/video/"+track.Bvid+"/");
            request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,null);
            var attempt=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);
            if(attempt.IsSuccessStatusCode){accepted=attempt;break;}lastStatus=(int)attempt.StatusCode;attempt.Dispose();
        }
        using var response=accepted??throw new IOException($"B 站音轨暂不可用（HTTP {lastStatus}），请稍后重试");
        if(response.Content.Headers.ContentLength>80*1024*1024)throw new IOException("该音轨过大，请选择较短的视频");
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            await using(var input=await response.Content.ReadAsStreamAsync())
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                byte[] buffer=new byte[65536];long total=0;using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));int read;
                while((read=await input.ReadAsync(buffer,timeout.Token))>0){total+=read;if(total>80*1024*1024)throw new IOException("音轨超过缓存大小限制");await output.WriteAsync(buffer.AsMemory(0,read),timeout.Token);}
            }
            File.Move(temporary,file,true);
            foreach(var old in new DirectoryInfo(folder).GetFiles("*.m4a").OrderByDescending(f=>f.LastWriteTimeUtc).Skip(6))
                if(old.FullName!=file)try{old.Delete();}catch(IOException){}
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
        return track with{Url=file};
    }
}
