using System.Text.Json;
namespace ClearTone;

// Device output time is independent of media seeking and network downloads.
public sealed class ListeningCounter
{
    double device,wall;bool active,initialized;
    public double Seconds {get;private set;}
    public double Sample(double outputSeconds,double suppliedSeconds,double now,bool playing){
        if(!double.IsFinite(outputSeconds)||!double.IsFinite(suppliedSeconds)||!double.IsFinite(now))return Seconds;
        if(initialized&&active){
            double delta=Math.Min(Math.Max(0,outputSeconds-device),Math.Max(0,now-wall));
            Seconds+=Math.Min(delta,Math.Max(0,suppliedSeconds-Seconds));
        }
        device=outputSeconds;wall=now;active=playing;initialized=true;return Seconds;
    }
}
public sealed record ListeningReport
{
    public string SessionId {get;set;}=Guid.NewGuid().ToString("N");
    public string AccountId {get;set;}="";
    public string SongId {get;set;}="";
    public string SourceId {get;set;}="";
    public double Seconds {get;set;}
    public long StartedAt {get;set;}=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public bool Finished {get;set;}
}
public sealed class ListeningSync
{
    readonly string file;readonly Func<ListeningReport,Task<string>> send;
    readonly List<ListeningReport> pending;ListeningReport? current;Task? running;DateTime lastSave=DateTime.MinValue;
    public double Seconds=>current?.Seconds??0;
    public string LastResult {get;private set;}="idle";
    public ListeningSync(string file,Func<ListeningReport,Task<string>> send){
        this.file=file;this.send=send;
        try{pending=File.Exists(file)?JsonSerializer.Deserialize<List<ListeningReport>>(File.ReadAllText(file),Storage.Json)??[]:[];}
        catch{pending=[];LastResult="storage-error";}
        // A previous process cannot continue rendering. Recover only its saved audible time.
        foreach(var r in pending)r.Finished=true;
    }
    public void Begin(string account,string song,string source){
        Finish();if(string.IsNullOrEmpty(account)||string.IsNullOrEmpty(song))return;
        current=new(){AccountId=account,SongId=song,SourceId=source};pending.Add(current);Save();
    }
    public void Checkpoint(double seconds,bool force=false){
        if(current==null||!double.IsFinite(seconds))return;
        current.Seconds=Math.Max(current.Seconds,Math.Max(0,seconds));
        if(force||(DateTime.UtcNow-lastSave).TotalSeconds>=5)Save();
    }
    public void Finish(){if(current==null)return;current.Finished=true;if(current.Seconds<1)pending.Remove(current);current=null;Save();}
    void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(file)!);var tmp=file+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(pending,Storage.Json));File.Move(tmp,file,true);lastSave=DateTime.UtcNow;}catch{LastResult="storage-error";}}
    public Task Pump(string account){if(running is {IsCompleted:false})return running;return running=Drain(account);}
    async Task Drain(string account){
        if(account.Length==0)return;
        try{
            foreach(var report in pending.Where(r=>r.Finished&&r.AccountId==account).ToArray()){
                if(report.Seconds<1){pending.Remove(report);Save();continue;}
                LastResult=await send(report);
                if(LastResult is "sent" or "uncertain" or "rejected"){pending.Remove(report);Save();}
                else break;
            }
        }catch{LastResult="pending";}
    }
}
