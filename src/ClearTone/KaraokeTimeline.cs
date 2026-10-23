using System.Globalization;
using System.Text;

namespace ClearTone;

public sealed record KaraokeWord(double Start,double Duration,string Text);
public sealed record KaraokeSourceLine(double Start,double Duration,string Text,List<KaraokeWord> Words);
public sealed record KaraokeSource(string Source,List<KaraokeSourceLine> Lines);
public sealed record KaraokeSegment(int Offset,int Length,double Start,double End);
public sealed record KaraokeLine(bool Exact,int Characters,IReadOnlyList<KaraokeSegment> Segments)
{
    public double Progress(double position){
        double progress=0;
        foreach(var segment in Segments){
            if(position<segment.Start)break;
            // A later source word may begin before the preceding sung note ends.
            // Follow its real start instead of delaying it until the earlier word finishes.
            double current=position<segment.End?segment.Offset+segment.Length*Math.Clamp((position-segment.Start)/(segment.End-segment.Start),0,1):segment.Offset+segment.Length;
            progress=Math.Max(progress,current);
        }
        return progress;
    }
    public double Fraction(int offset,int length,double position)=>length<=0?0:Math.Clamp((Progress(position)-offset)/length,0,1);
}
public static class KaraokeTimeline
{
    public static List<string> Elements(string text){var result=new List<string>();var iterator=StringInfo.GetTextElementEnumerator(text);while(iterator.MoveNext())result.Add(iterator.GetTextElement());return result;}
    static string MatchText(string text)=>string.Concat(Elements(text.Normalize(NormalizationForm.FormC)).Where(c=>!string.IsNullOrWhiteSpace(c)));
    public static KaraokeLine Build(Lyric line,double end,IReadOnlyList<ReadingToken> tokens,IReadOnlyList<KaraokeSourceLine> sources){
        var characters=Elements(line.Text);string normalized=MatchText(line.Text);
        var source=sources.Where(s=>double.IsFinite(s.Start)&&Math.Abs(s.Start-line.Time)<=1.5&&MatchText(s.Text)==normalized).OrderBy(s=>Math.Abs(s.Start-line.Time)).FirstOrDefault();
        if(source!=null&&source.Words.Count>0&&MatchText(string.Concat(source.Words.Select(w=>w.Text)))==normalized){
            int cursor=0;var segments=new List<KaraokeSegment>();double last=-1;
            foreach(var word in source.Words){
                if(!double.IsFinite(word.Start)||!double.IsFinite(word.Duration)||word.Duration<0||word.Start<last){segments.Clear();break;}
                int count=Elements(word.Text).Count(c=>!string.IsNullOrWhiteSpace(c)),start=cursor,matched=0;
                while(cursor<characters.Count&&matched<count){if(!string.IsNullOrWhiteSpace(characters[cursor]))matched++;cursor++;}
                if(matched!=count){segments.Clear();break;}
                if(cursor>start)segments.Add(new(start,cursor-start,word.Start,word.Start+word.Duration));last=word.Start;
            }
            if(segments.Count>0&&cursor>=characters.FindLastIndex(c=>!string.IsNullOrWhiteSpace(c))+1)return new(true,characters.Count,segments);
        }
        // LRC only identifies line starts. The fallback is a reading-weighted guide, never a source timestamp.
        double duration=double.IsFinite(end)&&end>line.Time?end-line.Time:Math.Max(1,characters.Count*.22);
        var weights=characters.Select(CharacterWeight).ToArray();int at=0;
        if(string.Concat(tokens.Select(t=>t.Text))==line.Text)foreach(var token in tokens){
            int length=Elements(token.Text).Count;double mora=ReadingWeight(token.Reading);
            if(mora>0&&length>0){double weight=weights.Skip(at).Take(length).Sum();if(weight>0)for(int i=at;i<Math.Min(at+length,weights.Length);i++)weights[i]*=mora/weight;}
            at+=length;
        }
        double total=Math.Max(.001,weights.Sum()),elapsed=0;var fallback=new List<KaraokeSegment>();
        for(int i=0;i<characters.Count;i++){double start=line.Time+duration*elapsed/total;elapsed+=weights[i];fallback.Add(new(i,1,start,line.Time+duration*elapsed/total));}
        return new(false,characters.Count,fallback);
    }
    static double CharacterWeight(string value){
        if(string.IsNullOrWhiteSpace(value))return .05;
        var category=CharUnicodeInfo.GetUnicodeCategory(value,0);
        return category is UnicodeCategory.OtherPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation?.12:1;
    }
    static double ReadingWeight(string reading){
        if(reading.Length==0)return 0;double count=0;
        foreach(string element in Elements(ReadingDisplay.HiraganaOf(reading))){char c=element[0];if(c is <'ぁ' or >'ゖ'&&c!='ー')continue;if("ゃゅょぁぃぅぇぉゎ".Contains(c))continue;count++;}
        return count;
    }
}
