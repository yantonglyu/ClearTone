using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace ClearTone;

public record Track
{
    public string Bvid {get;set;}="";
    public int BiliPage {get;set;}=1;
    public long BiliCid {get;set;}
    public string Name {get;set;}="";
    public string Artist {get;set;}="";
    public string Cover {get;set;}="";
    public string Url {get;set;}="";
    public string Lrc {get;set;}="";
    public string NetEaseId {get;set;}="";
    public double FullDuration {get;set;}
    public override string ToString()=>Name+"  ·  "+Artist;
}
public record Lyric(double Time,string Text,string Translation="",string Romanization="");
public sealed class AppState
{
    public double TotalSeconds {get;set;}
    public bool Translation {get;set;}=true;
    public bool Romanization {get;set;}
    public bool Motion {get;set;}=true;
    public bool Strict {get;set;}
    public bool Eq {get;set;}
    public double[] Gains {get;set;}=new double[5];
    public double Volume {get;set;}=.65;
    public bool Shuffle {get;set;}
    public bool RepeatOne {get;set;}
    public List<Track> Queue {get;set;}=[];
    public int Index {get;set;}
    public string PlaylistId {get;set;}="";
    public List<string> CoverGallery {get;set;}=[];
    public bool Daily {get;set;}
}
public static class Storage
{
    public static readonly string Root=Environment.GetEnvironmentVariable("CLEARTONE_HOME")??Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
    public static string Data=>Environment.GetEnvironmentVariable("CLEARTONE_DATA")??(File.Exists(Path.Combine(Root,"portable.mode"))?Path.Combine(Root,"data"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearTone"));
    public static JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    public static string StatePath=>Path.Combine(Data,"state.json");
    public static AppState Load()
    {
        Directory.CreateDirectory(Data);
        if(File.Exists(StatePath))return JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath),Json)??throw new InvalidDataException("设置文件为空");
        var s=new AppState();
        Save(s);return s;
    }
    public static void Save(AppState state){
        var tmp=StatePath+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(state,Json));
        if(File.Exists(StatePath))File.Replace(tmp,StatePath,StatePath+".bak");else File.Move(tmp,StatePath);
    }
    public static void Log(Exception e)=>File.AppendAllText(Path.Combine(Data,"errors.log"),DateTimeOffset.Now+" "+e.GetType().Name+": "+e.Message+Environment.NewLine);
}
public sealed class StudyClock
{
    readonly AppState state;readonly Stopwatch watch=new();double session;double savedSession;public bool Resting {get;private set;}
    public StudyClock(AppState s)=>state=s;
    public bool Running=>watch.IsRunning;
    public double Seconds=>session+watch.Elapsed.TotalSeconds;
    public void Toggle(){if(Running){session+=watch.Elapsed.TotalSeconds;watch.Reset();Resting=true;}else{watch.Start();Resting=false;}Checkpoint();}
    public void Finish(){if(Running){session+=watch.Elapsed.TotalSeconds;watch.Reset();}Checkpoint();session=0;savedSession=0;Resting=false;}
    public void Checkpoint(){double now=Seconds;state.TotalSeconds+=Math.Max(0,now-savedSession);savedSession=now;Storage.Save(state);}
    public static string Format(double s){var t=TimeSpan.FromSeconds(Math.Max(0,s));return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";}
}
public sealed class MusicService
{
    public static string Origin=>"http://127.0.0.1:"+(Environment.GetEnvironmentVariable("CLEARTONE_PORT")??"18754");
    readonly string port=Environment.GetEnvironmentVariable("CLEARTONE_PORT")??"18754";
    readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(70)};
    Process? process;
    public MusicService(){http.BaseAddress=new Uri("http://127.0.0.1:"+port+"/");http.DefaultRequestHeaders.Add("Origin",Origin);http.DefaultRequestHeaders.Add("X-ClearTone-Client","1");}
    public async Task Start(){
        try{var health=await http.GetFromJsonAsync<JsonElement>("health");if(health.GetProperty("service").GetString()=="cleartone-service-v1")return;}catch{}
        var p=new ProcessStartInfo(Path.Combine(Storage.Root,"runtime/node.exe")){WorkingDirectory=Path.Combine(Storage.Root,"service"),UseShellExecute=false,CreateNoWindow=true};
        p.ArgumentList.Add(Path.Combine(Storage.Root,"service/server.cjs"));p.Environment["CLEARTONE_PORT"]=port;p.Environment["CLEARTONE_ORIGIN"]=Origin;p.Environment["CLEARTONE_DATA"]=Storage.Data;
        process=Process.Start(p);
        for(int i=0;i<60;i++){await Task.Delay(250);try{var health=await http.GetFromJsonAsync<JsonElement>("health");if(health.GetProperty("service").GetString()=="cleartone-service-v1")return;}catch{}}
        throw new IOException("独立音乐服务没有启动");
    }
    public async Task<JsonElement> Call(string route,object? data=null,CancellationToken token=default){
        using var r=await http.PostAsJsonAsync("api/"+route,data??new{},token);
        var doc=await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);
        if(!r.IsSuccessStatusCode)throw new IOException(doc.TryGetProperty("error",out var e)?e.GetString():"音乐服务请求失败");
        return doc;
    }
    public static List<Track> Tracks(JsonElement d)=>JsonSerializer.Deserialize<List<Track>>(d.GetProperty("tracks"),Storage.Json)??[];
    public async Task<Track> Refresh(Track t){
        // Service media tokens belong to one process lifetime. Never reuse a persisted URL token.
        if(t.Bvid.Length>0)return await BiliAudio.Resolve(t);
        if(t.NetEaseId.Length==0)return t;
        var d=await Call("native-track",new{id=t.NetEaseId});
        return JsonSerializer.Deserialize<Track>(d.GetProperty("track"),Storage.Json)??t;
    }
    public void Stop(){try{if(process is {HasExited:false})process.Kill(true);}catch{}}
}
public static partial class Lyrics
{
    [GeneratedRegex(@"\[(\d+):(\d+(?:\.\d+)?)\]")]private static partial Regex Stamp();
    public static List<Lyric> Parse(string text){
        var result=new List<Lyric>();
        foreach(var line in text.Split('\n')){
            var stamps=Stamp().Matches(line);if(stamps.Count==0)continue;
            var words=Stamp().Replace(line,"").Trim();if(words.Length==0)continue;
            foreach(Match m in stamps)result.Add(new(double.Parse(m.Groups[1].Value)*60+double.Parse(m.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture),words));
        }
        return result.OrderBy(x=>x.Time).ToList();
    }
    public static List<Lyric> Merge(string main,string translation,string roman){
        var a=Parse(translation);var b=Parse(roman);
        string Match(List<Lyric> list,double t)=>list.FirstOrDefault(l=>Math.Abs(l.Time-t)<.15)?.Text??"";
        return Parse(main).Select(l=>l with{Translation=Match(a,l.Time),Romanization=Match(b,l.Time)}).ToList();
    }
}
