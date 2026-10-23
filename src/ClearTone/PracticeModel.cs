using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ClearTone;

public record ReadingToken(string Text,string Reading="",string Roman="",bool Uncertain=false);
public record ReadingLine(string Text,List<ReadingToken> Tokens);
public record ReadingRevision(DateTimeOffset At,string Line,List<ReadingToken> Before);
public static class ReadingDisplay
{
    public const int Off=0,Hiragana=1,Romaji=2;
    public static int Resolve(int mode,bool legacyRomanization)=>mode is >=Off and <=Romaji?mode:legacyRomanization?Romaji:Off;
    public static int Next(int mode)=>(Math.Clamp(mode,Off,Romaji)+1)%3;
    // Kuromoji already emits hiragana. Normalize corrections too, without changing the source lyric.
    public static string HiraganaOf(string reading)=>string.Concat(reading.Select(c=>c is >= '\u30a1' and <= '\u30f6'?(char)(c-0x60):c));
    public static string Ruby(ReadingToken token){string reading=HiraganaOf(token.Reading);return reading==HiraganaOf(token.Text)?"":reading;}
    public static string Romanization(Lyric line,IReadOnlyList<ReadingToken> tokens,bool corrected){
        if(!corrected&&line.Romanization.Length>0)return line.Romanization;
        if(!tokens.Any(t=>t.Roman.Length>0||t.Reading.Length>0))return "";
        return string.Join(" ",tokens.Select(t=>t.Roman.Length>0?t.Roman:t.Reading.Length>0?t.Reading:t.Text));
    }
}
public sealed class PracticeDocument
{
    public double Offset {get;set;}
    public Dictionary<string,List<ReadingToken>> Corrections {get;set;}=[];
    public List<ReadingRevision> Revisions {get;set;}=[];
    public static string PathFor(Track track)=>Path.Combine(Storage.Data,"practice",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Key)))+".json");
    public static PracticeDocument Load(Track track)=>File.Exists(PathFor(track))?JsonSerializer.Deserialize<PracticeDocument>(File.ReadAllText(PathFor(track)),Storage.Json)??new():new();
    public void Save(Track track){string file=PathFor(track);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this,Storage.Json));File.Move(file+".tmp",file,true);}
    public static string LineKey(Lyric line)=>line.Time.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\n"+line.Text;
    public void Correct(Lyric line,List<ReadingToken> before,List<ReadingToken> after){
        if(string.Concat(after.Select(t=>t.Text))!=line.Text)throw new ArgumentException("纠错不能更改原歌词");
        Revisions.Add(new(DateTimeOffset.UtcNow,LineKey(line),before));if(Revisions.Count>40)Revisions.RemoveAt(0);
        Corrections[LineKey(line)]=after;
    }
    public bool Undo(Lyric line){int i=Revisions.FindLastIndex(r=>r.Line==LineKey(line));if(i<0)return false;Corrections[LineKey(line)]=Revisions[i].Before;Revisions.RemoveAt(i);return true;}
}
public static class PracticeTiming
{
    public static int Current(IReadOnlyList<Lyric> lines,double offset,double position){
        for(int i=lines.Count-1;i>=0;i--)if(lines[i].Time+offset<=position)return i;
        return lines.Count==0?-1:0;
    }
    // Positive offset means the words occur later in the media.
    public static (double Start,double End) Range(IReadOnlyList<Lyric> lines,int index,double offset,double lead,double duration){
        if(index<0||index>=lines.Count||duration<=0)return (0,0);
        double start=Math.Clamp(lines[index].Time+offset-Math.Clamp(lead,0,5),0,duration);
        double end=lines.Skip(index+1).FirstOrDefault(l=>l.Time>lines[index].Time)?.Time??duration-offset;
        end=Math.Clamp(end+offset,0,duration);return (start,Math.Max(start,end));
    }
}
public sealed class LocalCollection
{
    public string Name {get;set;}="我的混合歌单";
    public List<Track> Tracks {get;set;}=[];
}
public sealed class MusicShelf
{
    public List<SavedArtist> Artists {get;set;}=[];
    public string FavoriteSort {get;set;}="added";
    public List<Track> Tracks {get;set;}=[];
    public List<LocalCollection> Playlists {get;set;}=[];
    public List<Track> History {get;set;}=[];
    static string FileName=>Path.Combine(Storage.Data,"music-library.json");
    public static MusicShelf Load()=>File.Exists(FileName)?JsonSerializer.Deserialize<MusicShelf>(File.ReadAllText(FileName),Storage.Json)??new():new();
    public void Save(){File.WriteAllText(FileName+".tmp",JsonSerializer.Serialize(this,Storage.Json));File.Move(FileName+".tmp",FileName,true);}
    public void Remember(Track track){History.RemoveAll(t=>t.Key==track.Key);History.Insert(0,track);if(History.Count>200)History.RemoveRange(200,History.Count-200);Save();}
    public void Add(IEnumerable<Track> tracks){var keys=Tracks.Select(t=>t.Key).ToHashSet();Tracks.AddRange(tracks.Where(t=>keys.Add(t.Key)));Save();}
}
public sealed record SavedArtist(string Id,string Name,string Cover,string Source="netease",string Description="")
{
    [System.Text.Json.Serialization.JsonIgnore] public string Key=>Source+":"+Id;
    [System.Text.Json.Serialization.JsonIgnore] public string Subtitle=>Description.Length>0?Description:Source=="netease"?"网易云歌手":"B 站 UP 主";
}
