using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using System.Text.Json;
using Windows.UI;

namespace ClearTone;

public sealed partial class MainPage
{
    Task? readingRequest;
    List<Lyric>? readingSource;
    readonly Dictionary<int,PlaybackRubyLine> playbackRubyLines=[];
    int PlaybackReadingMode=>ReadingDisplay.Resolve(state.LyricReadingMode,state.Romanization);

    void UpdateReadingButton(){
        int mode=PlaybackReadingMode;
        string help=mode==ReadingDisplay.Hiragana?"平假名 · 点击切换罗马音":mode==ReadingDisplay.Romaji?"罗马音 · 点击关闭注音":"注音 · 点击开启平假名";
        roman.Content=Text(mode==ReadingDisplay.Romaji?"ā":"あ",17);
        roman.Opacity=mode==ReadingDisplay.Off?.38:.78;ToolTipService.SetToolTip(roman,help);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(roman,help);
    }
    void CyclePlaybackReadings(){
        state.LyricReadingMode=ReadingDisplay.Next(PlaybackReadingMode);state.Romanization=PlaybackReadingMode==ReadingDisplay.Romaji;
        UpdateReadingButton();ClearLyricTextures();Storage.Save(state);lyricCanvas.Invalidate();WakeLyricAnimation();
        if(PlaybackReadingMode!=ReadingDisplay.Off)_=LoadPlaybackReadings();
    }
    void PrepareTrackReadings(){
        EnsurePracticeDocument();UpdateReadingButton();
        if(PlaybackReadingMode!=ReadingDisplay.Off)_=LoadPlaybackReadings();
    }
    void EnsurePracticeDocument(){
        if(current==null||practiceTrack==current.Key)return;
        practiceTrack=current.Key;practiceDocument=PracticeDocument.Load(current);practiceReadings=[];practiceSelected=-1;practicePainted=-2;practiceLoop=false;practiceFollow=true;
    }
    async Task LoadPlaybackReadings(){try{await EnsureTrackReadings();}catch(OperationCanceledException){}catch(Exception e){Storage.Log(e);}}
    Task EnsureTrackReadings(){
        EnsurePracticeDocument();
        if(current==null||lyrics.Count==0)return Task.CompletedTask;
        if(ReferenceEquals(readingSource,lyrics)&&readingRequest is {IsFaulted:false,IsCanceled:false})return readingRequest;
        readingSource=lyrics;practiceReadings=[];
        // A single request per lyric document, shared by playback and practice. The service owns the disk cache.
        return readingRequest=FetchTrackReadings(current.Key,lyrics,trackCancel.Token);
    }
    async Task FetchTrackReadings(string key,List<Lyric> source,CancellationToken token){
        var result=await service.Call("practice-readings",new{lines=source.Select(l=>l.Text).Take(500).ToArray()},token);
        if(token.IsCancellationRequested||current?.Key!=key||!ReferenceEquals(source,lyrics))return;
        practiceReadings=JsonSerializer.Deserialize<List<ReadingLine>>(result.GetProperty("lines"),Storage.Json)??[];
        InvalidateReadingDisplay();
    }
    void InvalidateReadingDisplay(){
        ClearLyricTextures();lyricCanvas.Invalidate();WakeLyricAnimation();
        if(page=="practice")PaintPractice(true);
    }
    string PlaybackRomanization(int index)=>PlaybackReadingMode==ReadingDisplay.Romaji?ReadingDisplay.Romanization(lyrics[index],TokensFor(index),practiceDocument.Corrections.ContainsKey(PracticeDocument.LineKey(lyrics[index]))):"";
    bool HasPlaybackRuby(int index)=>PlaybackReadingMode==ReadingDisplay.Hiragana&&TokensFor(index).Any(t=>ReadingDisplay.Ruby(t).Length>0);
    void ClearPlaybackRubyLayouts(){foreach(var line in playbackRubyLines.Values)line.Dispose();playbackRubyLines.Clear();}
    float MeasurePlaybackLine(ICanvasResourceCreator creator,int index,float width,CanvasTextFormat format){
        if(!HasPlaybackRuby(index)){using var plain=new CanvasTextLayout(creator,lyrics[index].Text,format,width,0);return (float)plain.LayoutBounds.Height;}
        using var measured=CreatePlaybackRuby(creator,index,width,format);return measured.Height;
    }
    void DrawPlaybackLine(ICanvasResourceCreator creator,CanvasDrawingSession drawing,int index,float x,float y,float width,CanvasTextFormat format){
        if(!HasPlaybackRuby(index)){drawing.DrawText(lyrics[index].Text,new Windows.Foundation.Rect(x,y,width,mainHeights[index]+4),Colors.White,format);return;}
        var line=PlaybackRuby(creator,index,width,format);
        foreach(var word in line.Words){
            drawing.DrawTextLayout(word.Main,new Vector2(x+word.X+word.MainOffset,y+word.Y+word.ReadingHeight),Colors.White);
            if(word.Reading!=null)drawing.DrawTextLayout(word.Reading,new Vector2(x+word.X+word.ReadingOffset,y+word.Y),Color.FromArgb(word.Uncertain?(byte)155:(byte)215,255,218,225));
        }
    }
    PlaybackRubyLine PlaybackRuby(ICanvasResourceCreator creator,int index,float width,CanvasTextFormat format){
        if(playbackRubyLines.TryGetValue(index,out var cached))return cached;
        var result=CreatePlaybackRuby(creator,index,width,format);playbackRubyLines[index]=result;
        if(playbackRubyLines.Count>40){int oldest=playbackRubyLines.Keys.First(k=>k!=index);playbackRubyLines[oldest].Dispose();playbackRubyLines.Remove(oldest);}
        return result;
    }
    PlaybackRubyLine CreatePlaybackRuby(ICanvasResourceCreator creator,int index,float width,CanvasTextFormat format){
        var readingFormat=lyricFormats!.Reading;
        var result=new PlaybackRubyLine();float x=0,y=0,rowHeight=0,space=Math.Max(1,format.FontSize*.035f);
        foreach(var token in TokensFor(index)){
            if(token.Text.Length==0)continue;
            string annotation=ReadingDisplay.Ruby(token);
            var main=new CanvasTextLayout(creator,token.Text,format,width,0);
            CanvasTextLayout? reading=annotation.Length>0?new CanvasTextLayout(creator,annotation,readingFormat,width,0):null;
            float mainWidth=Math.Min(width,(float)main.LayoutBoundsIncludingTrailingWhitespace.Width),readingWidth=reading==null?0:Math.Min(width,(float)reading.LayoutBounds.Width);
            float wordWidth=Math.Max(mainWidth,readingWidth),readingHeight=reading==null?0:(float)reading.LayoutBounds.Height+2;
            // Reserve a ruby band in every row, so plain kana and annotated kanji share a baseline.
            float band=Math.Max(readingFormat.FontSize*1.5f,readingHeight),height=band+(float)main.LayoutBounds.Height;
            if(x>0&&x+wordWidth>width){x=0;y+=rowHeight;rowHeight=0;}
            result.Words.Add(new(main,reading,x,y,(wordWidth-mainWidth)/2,(wordWidth-readingWidth)/2,band,token.Uncertain));
            x+=wordWidth+space;rowHeight=Math.Max(rowHeight,height);
        }
        result.Height=Math.Max(format.FontSize,y+rowHeight);return result;
    }
    sealed record PlaybackRubyWord(CanvasTextLayout Main,CanvasTextLayout? Reading,float X,float Y,float MainOffset,float ReadingOffset,float ReadingHeight,bool Uncertain);
    sealed class PlaybackRubyLine:IDisposable {
        public readonly List<PlaybackRubyWord> Words=[];public float Height;
        public void Dispose(){foreach(var word in Words){word.Main.Dispose();word.Reading?.Dispose();}}
    }
}
