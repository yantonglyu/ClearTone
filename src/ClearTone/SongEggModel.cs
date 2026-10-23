using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClearTone;

public sealed record SongEggProfile {
    public string Id {get;init;}="";
    public string Title {get;init;}="";
    public string[] Artists {get;init;}=[];
    public string[] NetEaseIds {get;init;}=[];
    public string Anchor {get;init;}="";
    public double FallbackSeconds {get;init;}
    public double MinimumDuration {get;init;}=245;
    public double MaximumDuration {get;init;}=275;
    public double EndBeforeTrackEnd {get;init;}=6;
    public double FadeOutSeconds {get;init;}=12;
    public bool Enabled {get;init;}=true;
    static string Normal(string value)=>Regex.Replace(value.ToLowerInvariant(),@"[\s’'\-]","");
    public bool Matches(Track track)=>Enabled&&(NetEaseIds.Contains(track.NetEaseId)||Normal(track.Name)==Normal(Title)&&Artists.Any(a=>Normal(a)==Normal(track.Artist)));
    public EggCue? Resolve(Track track,IReadOnlyList<Lyric> lyrics,double duration){
        if(!Matches(track)||!double.IsFinite(duration)||duration<MinimumDuration||duration>MaximumDuration)return null;
        var anchor=lyrics.LastOrDefault(l=>l.Time>duration*.6&&l.Time<duration-20&&Normal(l.Text).StartsWith(Normal(Anchor),StringComparison.Ordinal));
        double start=anchor?.Time??FallbackSeconds;
        double end=duration-EndBeforeTrackEnd;
        return start>0&&start<end?new(Id,track.Key,start,end,Math.Clamp(FadeOutSeconds,2,20)):null;
    }
}
public sealed record EggCue(string Id,string TrackKey,double Start,double End,double FadeOut);
public readonly record struct EggFrame(double Opacity,double Burst,double Motion,bool Active,double Preheat=0,double BurstAge=-1);

// All timing comes from playback position, not a delayed task that can outlive a song.
public sealed class SongEggClock {
    EggCue? previousCue;double previousPosition=-1,lastWall=-1,opacity,motion,burstAt=-1;
    bool fired,sought;double preheat;
    public int Bursts {get;private set;}
    public void Seek(){sought=true;burstAt=-1;}
    public void Reset(){previousCue=null;previousPosition=-1;fired=false;burstAt=-1;sought=true;}
    public EggFrame Update(EggCue? cue,double position,bool playing,bool visible,bool reduced,double wall){
        double dt=lastWall<0?0:Math.Clamp(wall-lastWall,0,.1);lastWall=wall;
        bool changed=cue?.TrackKey!=previousCue?.TrackKey||cue?.Id!=previousCue?.Id;
        if(changed){previousPosition=-1;fired=false;burstAt=-1;}
        bool inside=cue!=null&&position>=cue.Start&&position<cue.End;
        if(cue!=null&&position<cue.Start-3){fired=false;burstAt=-1;}
        bool crossing=cue!=null&&previousPosition>=cue.Start-1.5&&previousPosition<cue.Start&&position>=cue.Start;
        bool jump=previousPosition>=0&&Math.Abs(position-previousPosition)>1.5;
        double preparation=cue==null?0:(position-(cue.Start-.55))/.55;
        double warmTarget=playing&&visible&&!reduced&&!fired&&!sought&&!jump&&!changed&&preparation>0&&preparation<1?preparation:0;
        preheat=warmTarget>0?warmTarget:preheat*Math.Exp(-dt*10);
        if(preheat<.002)preheat=0;
        if(inside&&playing&&visible&&!reduced&&!fired&&!sought&&!jump&&crossing){burstAt=wall;fired=true;Bursts++;}
        if(sought||jump||changed||!inside)burstAt=-1;
        double target=inside&&visible?Math.Clamp((cue!.End-position)/cue.FadeOut,0,1):0;
        // Smoothstep at the song's end, exponential cross-fade on seeks/switches.
        target=target*target*(3-2*target);
        opacity+=(target-opacity)*(1-Math.Exp(-dt*(target>opacity?5:4)));
        if(opacity<.002&&target==0)opacity=0;
        if(playing&&visible&&!reduced)motion+=dt;
        double burst=burstAt>=0&&!reduced?Math.Exp(-Math.Max(0,wall-burstAt)*2.3):0;
        if(burst<.002)burst=0;
        previousCue=cue;previousPosition=position;sought=false;
        return new(opacity,burst,motion,opacity>0||inside&&visible||preheat>0,preheat,burstAt<0?-1:Math.Max(0,wall-burstAt));
    }
}
