using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClearTone;

public static class BiliAudio
{
    static readonly HttpClient http=new(new HttpClientHandler{AutomaticDecompression=System.Net.DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(35)};
    const string Agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36";
    static JsonElement Embedded(string html,string marker){
        var match=Regex.Match(html,Regex.Escape(marker)+@"\s*=\s*");
        if(!match.Success)throw new IOException("B 站暂未返回视频信息，请稍后重试");
        var reader=new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(html[(match.Index+match.Length)..]));
        using var doc=JsonDocument.ParseValue(ref reader);return doc.RootElement.Clone();
    }
    static async Task<JsonElement> Page(string id,bool audio,int page,CancellationToken token){
        using var request=Request($"https://www.bilibili.com/video/{Validate(id)}/?p={page}",id);
        using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
        var html=await response.Content.ReadAsStringAsync(token);
        return audio?Embedded(html,"window.__playinfo__").GetProperty("data").Clone():Embedded(html,"window.__INITIAL_STATE__").GetProperty("videoData").Clone();
    }
    static string Validate(string id)=>Regex.IsMatch(id,@"^BV[0-9A-Za-z]{10}$")?id:throw new ArgumentException("请输入完整的 BV 号或视频链接");
    static HttpRequestMessage Request(string url,string id){
        var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd(Agent);
        request.Headers.Referrer=new Uri("https://www.bilibili.com/video/"+Validate(id)+"/");
        return request;
    }
    static async Task<JsonElement> Api(string path,string id,CancellationToken token,int page=1){
        // Overseas requests to the legacy view endpoint may return 412 while the
        // current public WBI endpoint succeeds. No cookies or account impersonation.
        string current=path.Replace("x/web-interface/view?","x/web-interface/wbi/view?").Replace("x/player/playurl?","x/player/wbi/playurl?");
        foreach(string endpoint in new[]{current,path}.Distinct()){
            using var request=Request("https://api.bilibili.com/"+endpoint,id);
            using var response=await http.SendAsync(request,token);
            if((int)response.StatusCode is 403 or 412)continue;
            response.EnsureSuccessStatusCode();
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));var root=doc.RootElement;
            int code=root.GetProperty("code").GetInt32();
            if(code is -352 or -403 or -412)continue;
            if(code!=0)throw new IOException("B 站暂未提供该视频："+root.GetProperty("message").GetString());
            return root.GetProperty("data").Clone();
        }
        return await Page(id,path.Contains("playurl"),page,token);
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
    static int Number(JsonElement item,string name)=>item.TryGetProperty(name,out var value)&&value.TryGetInt32(out int n)?n:0;
    static IEnumerable<string> Candidates(JsonElement track){
        foreach(string key in new[]{"baseUrl","base_url","url"})if(track.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String)yield return item.GetString()!;
        foreach(string key in new[]{"backupUrl","backup_url"})if(track.TryGetProperty(key,out var items)&&items.ValueKind==JsonValueKind.Array)foreach(var item in items.EnumerateArray())if(item.ValueKind==JsonValueKind.String)yield return item.GetString()!;
    }
    static List<string> SafeUrls(IEnumerable<JsonElement> tracks)=>tracks.SelectMany(Candidates).Select(x=>x.StartsWith("http://",StringComparison.OrdinalIgnoreCase)?"https://"+x[7..]:x).Where(x=>Uri.TryCreate(x,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Port==443&&u.UserInfo.Length==0&&(new[]{".bilivideo.com",".bilivideo.cn",".bilivideo.net"}.Any(s=>u.Host.EndsWith(s,StringComparison.OrdinalIgnoreCase))||(u.Host.StartsWith("upos-",StringComparison.OrdinalIgnoreCase)&&u.Host.EndsWith(".akamaized.net",StringComparison.OrdinalIgnoreCase)))).Distinct().Take(8).ToList();
    public static async Task<Track> Resolve(Track track,CancellationToken token=default){
        Validate(track.Bvid);
        if(track.BiliCid<=0){var pages=await Read(track.Bvid,track.Cover,token);track=pages.FirstOrDefault(p=>p.BiliPage==Math.Max(1,track.BiliPage))??throw new IOException("该视频分 P 暂不可用，请重新搜索或粘贴视频链接");}
        var data=await Api($"x/player/playurl?bvid={track.Bvid}&cid={track.BiliCid}&fnval=16&fnver=0&fourk=0&qn=64",track.Bvid,token,track.BiliPage);
        List<string> audioUrls,videoUrls;object? video=null;
        if(data.TryGetProperty("dash",out var dash)&&dash.ValueKind==JsonValueKind.Object){
            if(!dash.TryGetProperty("audio",out var audioTracks)||audioTracks.ValueKind!=JsonValueKind.Array||audioTracks.GetArrayLength()==0)throw new IOException("该视频暂未提供可读取的音轨");
            audioUrls=SafeUrls(audioTracks.EnumerateArray().OrderByDescending(t=>Number(t,"bandwidth")).Take(1));
            var tracks=dash.GetProperty("video").EnumerateArray().Where(t=>t.TryGetProperty("codecs",out var c)&&c.GetString()?.StartsWith("avc",StringComparison.OrdinalIgnoreCase)==true).ToList();
            // H.264 avoids optional HEVC decoder dependencies. Prefer up to 720p,
            // keeping long live performances streaming instead of multi-GB downloads.
            var preferred=tracks.Where(t=>Number(t,"height")<=720).OrderByDescending(t=>Number(t,"height")).ThenByDescending(t=>Number(t,"bandwidth")).ToList();
            if(preferred.Count==0)preferred=tracks.OrderBy(t=>Number(t,"height")).ToList();
            videoUrls=preferred.Count>0?SafeUrls(preferred.Take(1)):[];
            if(preferred.Count>0&&(preferred[0].TryGetProperty("SegmentBase",out var segment)||preferred[0].TryGetProperty("segment_base",out segment))){
                var chosen=preferred[0];
                video=new{duration=dash.TryGetProperty("duration",out var length)?length.GetDouble():track.FullDuration,
                    codecs=chosen.GetProperty("codecs").GetString(),bandwidth=Number(chosen,"bandwidth"),width=Number(chosen,"width"),height=Number(chosen,"height"),
                    initialization=(segment.TryGetProperty("Initialization",out var init)?init:segment.GetProperty("initialization")).GetString(),indexRange=(segment.TryGetProperty("indexRange",out var index)?index:segment.GetProperty("index_range")).GetString()};
            }
        }else if(data.TryGetProperty("durl",out var urls)&&urls.ValueKind==JsonValueKind.Array&&urls.GetArrayLength()==1){audioUrls=videoUrls=SafeUrls(urls.EnumerateArray());}
        else throw new IOException("该视频暂未提供兼容的视频流，请稍后重试");
        if(audioUrls.Count==0||videoUrls.Count==0)throw new IOException("B 站暂未提供可用的 H.264 视频和音轨");
        using var relayRequest=new HttpRequestMessage(HttpMethod.Post,MusicService.Origin+"/api/bili-media"){Content=JsonContent.Create(new{audioUrl=audioUrls[0],videoUrl=videoUrls[0],audioUrls,videoUrls,video,referer="https://www.bilibili.com/video/"+track.Bvid+"/"})};
        relayRequest.Headers.Add("Origin",MusicService.Origin);relayRequest.Headers.Add("X-ClearTone-Client","1");
        using var relayResponse=await http.SendAsync(relayRequest,token);var relay=await relayResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);
        if(!relayResponse.IsSuccessStatusCode)throw new IOException(relay.TryGetProperty("error",out var error)?error.GetString():"B 站媒体转发暂不可用");
        return track with{Url=relay.GetProperty("audioUrl").GetString()!,VideoUrl=relay.GetProperty("videoUrl").GetString()!,VideoAdaptive=relay.TryGetProperty("videoAdaptive",out var adaptive)&&adaptive.GetBoolean()};
    }
}

