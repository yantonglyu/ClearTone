using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using System.Numerics;
using System.Text.Json;
using Windows.Foundation;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage
{
    PracticeDocument practiceDocument=new();List<ReadingLine> practiceReadings=[];string practiceTrack="";int practiceSelected=-1,practicePainted=-2,practiceGeneration;
    bool practiceLoop,practiceTranslation,practiceRoman,practiceFollow=true,practiceCompact;double practiceLead=1,practiceFontSize;int practiceLoops;
    ComboBox? practiceSpeedControl;RubyPanel? ruby;TextBlock? practicePrevious,practiceNext,practiceTranslationText,practiceRomanText,practiceMessage;ListView? practiceLines;Button? loopButton;
    StackPanel? practiceWords,practiceControls;Grid? practiceSong;ScrollViewer? practiceWordViewport;
    Grid? practiceRubyHost;Canvas? practiceOutgoing;RubyPanel? practiceOutgoingWords;int practiceTransitionGeneration,practiceTransitions;bool practiceTransitionRunning;
    readonly List<PracticeKaraokeWord> practiceKaraokeWords=[],practiceOutgoingKaraoke=[];
    KaraokeSource practiceWordSource=new("",[]);KaraokeLine? practiceKaraoke;double practiceKaraokePosition;
    async Task BuildPractice(){
        StopPracticeMotion();ClearPracticeKaraoke(practiceKaraokeWords);practiceLines=null;ruby=null;
        int generation=++practiceGeneration;var token=pageCancel.Token;
        var frame=CatalogFrame(current?.Name??"跟唱练习",out var actions,out var message,out var content);practiceMessage=message;workspace.Children.Clear();workspace.Children.Add(frame);
        var translationToggle=new ToggleButton{FontFamily=UiFont,Content="译文",IsChecked=practiceTranslation,MinHeight=36,BorderThickness=new(0),CornerRadius=new(9),Padding=new(14,8,14,8)};
        var romanToggle=new ToggleButton{FontFamily=UiFont,Content="罗马音",IsChecked=practiceRoman,MinHeight=36,BorderThickness=new(0),CornerRadius=new(9),Padding=new(14,8,14,8)};
        translationToggle.Click+=(_,_)=>{practiceTranslation=translationToggle.IsChecked==true;PaintPractice(true);};romanToggle.Click+=(_,_)=>{practiceRoman=romanToggle.IsChecked==true;PaintPractice(true);};
        var toolbar=Row(translationToggle,romanToggle,ActionButton("◎ 跟随当前句",()=>SetPracticeLoop(false)),MoreActions(AsyncButton("导入歌词",ImportPracticeLyrics),AsyncButton("修正读音",EditReadings)));toolbar.Spacing=8;actions.Children.Add(toolbar);
        if(current==null){content.Children.Add(Text("先播放一首歌，再来唱一段吧。",20,.7));return;}
        EnsurePracticeDocument();
        if(lyrics.Count==0){content.Children.Add(Text("这首歌还没有同步歌词\n可以导入 .lrc 文件开始练习",20,.7));return;}
        var grid=new Grid{ColumnSpacing=28};grid.ColumnDefinitions.Add(new(){Width=new GridLength(1.7,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var song=practiceSong=new Grid{RowSpacing=14};song.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});song.RowDefinitions.Add(new(){Height=GridLength.Auto});
        practicePrevious=Text("",18,.4);practiceNext=Text("",18,.4);ruby=NewPracticeRuby();practiceRubyHost=new Grid{Background=new SolidColorBrush(Color.FromArgb(0,0,0,0))};practiceRubyHost.Children.Add(ruby);practiceTranslationText=Text("",18,.62);practiceRomanText=Text("",17,.65);
        foreach(var context in new[]{practicePrevious,practiceNext}){context.TextWrapping=TextWrapping.NoWrap;context.TextTrimming=TextTrimming.CharacterEllipsis;}
        var words=practiceWords=Column(practicePrevious,practiceRubyHost,practiceRomanText,practiceTranslationText,practiceNext);
        practiceWordViewport=new ScrollViewer{Content=words,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        practiceWordViewport.SizeChanged+=(_,_)=>{if(page=="practice")PaintPractice(false);};song.Children.Add(practiceWordViewport);
        loopButton=ActionButton("循环此句",()=>SetPracticeLoop(!practiceLoop));
        var previous=ActionButton("‹ 上一句",()=>MovePractice(-1));var next=ActionButton("下一句 ›",()=>MovePractice(1));
        var speed=practiceSpeedControl=new ComboBox{FontFamily=UiFont,MinWidth=100};double[] speeds=[.5,.65,.75,.85,1,1.15,1.25,1.5];foreach(double value in speeds)speed.Items.Add(value.ToString("0.##")+"×");speed.SelectedIndex=4;speed.SelectionChanged+=(_,_)=>{if(speed.SelectedIndex>=0)audio.SetSpeed(speeds[speed.SelectedIndex]);};ToolTipService.SetToolTip(speed,"变速不变调");
        var lead=Choice("句前预备时间",["0 秒","1 秒","2 秒","3 秒"],(int)practiceLead,i=>practiceLead=i);
        var offset=new NumberBox{FontFamily=UiFont,Value=practiceDocument.Offset,Minimum=-15,Maximum=15,SmallChange=.1,LargeChange=.5,Width=100,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};offset.ValueChanged+=(_,e)=>{if(!double.IsFinite(e.NewValue))return;practiceDocument.Offset=e.NewValue;if(current!=null)practiceDocument.Save(current);};ToolTipService.SetToolTip(offset,"歌词偏移（秒），正数表示歌词晚出现");
        Primary(loopButton);var transport=Row(previous,loopButton,next);transport.HorizontalAlignment=HorizontalAlignment.Center;transport.Spacing=10;
        var controls=practiceControls=Column(transport,EqualColumns(LabeledControl("速度 · 保持音高",speed),LabeledControl("句前预备",lead),LabeledControl("歌词偏移 · 秒",offset)));controls.Spacing=18;controls.Margin=new(0,12,0,0);song.Children.Add(controls);Grid.SetRow(controls,1);grid.Children.Add(song);
        practiceLines=new ListView{SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=true,ItemsSource=lyrics,ItemTemplate=(DataTemplate)XamlReader.Load("""<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding Text}" TextWrapping="Wrap" FontSize="16" Margin="0,12" Opacity="0.75"/></DataTemplate>""")};
        practiceLines.ItemClick+=(_,e)=>{practiceSelected=lyrics.IndexOf((Lyric)e.ClickedItem);SetPracticeLoop(true);};grid.Children.Add(practiceLines);Grid.SetColumn(practiceLines,1);content.Children.Add(grid);
        practiceSelected=Math.Clamp(practiceSelected<0?CurrentPracticeLine():practiceSelected,0,lyrics.Count-1);practicePainted=-2;PaintPractice(true);message.Text="正在生成假名…";
        try{
            await EnsureTrackReadings();
            if(token.IsCancellationRequested||generation!=practiceGeneration)return;
            PaintPractice(true);message.Text="自动读音可修正 · 点击右侧一句即可循环";
        }catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested)message.Text="假名暂不可用，原歌词仍可练习。"+e.Message;}
    }
    int CurrentPracticeLine()=>PracticeTiming.Current(lyrics,practiceDocument.Offset,audio.Position);
    void SetPracticeLoop(bool enabled){
        if(lyrics.Count==0){practiceLoop=false;practiceFollow=true;return;}
        practiceLoop=enabled;practiceFollow=!enabled;
        if(enabled){practiceSelected=Math.Clamp(practiceSelected,0,lyrics.Count-1);SeekPractice();PlayAudio();}
        else practiceSelected=CurrentPracticeLine();
        PaintPractice(true);
    }
    void MovePractice(int step){if(lyrics.Count==0)return;practiceSelected=Math.Clamp(practiceSelected+step,0,lyrics.Count-1);practiceFollow=!practiceLoop;if(practiceLoop)SeekPractice();else SeekAudio(Math.Clamp(lyrics[practiceSelected].Time+practiceDocument.Offset,0,audio.Duration));PaintPractice(true);}
    void SeekPractice(){var range=PracticeTiming.Range(lyrics,practiceSelected,practiceDocument.Offset,practiceLead,audio.Duration);SeekAudio(range.Start);}
    void StopPractice(){practiceLoop=false;practiceFollow=true;audio.SetSpeed(1);if(practiceSpeedControl!=null)practiceSpeedControl.SelectedIndex=4;practiceSelected=CurrentPracticeLine();PaintPractice(true);StopPracticeMotion();}
    bool RepeatPractice(){if(!practiceLoop||page!="practice"||lyrics.Count==0)return false;SeekPractice();PlayAudio();practiceLoops++;return true;}
    void TickPractice(){
        if(lyrics.Count==0)return;
        if(practiceLoop&&audio.Playing){var range=PracticeTiming.Range(lyrics,practiceSelected,practiceDocument.Offset,practiceLead,audio.Duration);if(range.End>range.Start&&audio.Position>=range.End){SeekPractice();practiceLoops++;}}
        if(practiceFollow&&!practiceLoop)practiceSelected=CurrentPracticeLine();
        if(!(App.Main?.IsVisibleForAnimation??true)){StopPracticeMotion();return;}
        PaintPractice(false);UpdatePracticeKaraoke();
    }
    List<ReadingToken> TokensFor(int index){
        var line=lyrics[index];if(practiceDocument.Corrections.TryGetValue(PracticeDocument.LineKey(line),out var corrected))return corrected;
        if(index<practiceReadings.Count&&practiceReadings[index].Text==line.Text)return practiceReadings[index].Tokens;
        return [new(line.Text)];
    }
    void PaintPractice(bool force){
        if(page!="practice"||practiceLines==null||!ReferenceEquals(practiceLines.ItemsSource,lyrics))return;
        bool compact=ActualHeight<740,layoutChanged=compact!=practiceCompact;
        double fontSize=Math.Clamp(ActualWidth*.028,34,compact?38:46);
        if(ruby==null||practiceSelected<0||practiceSelected>=lyrics.Count||(!force&&!layoutChanged&&practicePainted==practiceSelected&&Math.Abs(practiceFontSize-fontSize)<.25))return;
        bool lineChanged=practicePainted!=practiceSelected;practicePainted=practiceSelected;practiceFontSize=fontSize;practiceCompact=compact;
        if(practiceWords!=null){practiceWords.Spacing=compact?8:24;practiceWords.VerticalAlignment=compact?VerticalAlignment.Top:VerticalAlignment.Center;}
        if(practiceWordViewport!=null)practiceWordViewport.VerticalContentAlignment=compact?VerticalAlignment.Top:VerticalAlignment.Center;
        if(practiceControls!=null){practiceControls.Spacing=compact?10:18;practiceControls.Margin=new(0,compact?4:12,0,0);}
        if(practiceSong!=null)practiceSong.RowSpacing=compact?8:14;
        bool animateLine=lineChanged&&practicePainted>=0&&practiceRubyHost!=null&&ruby.Children.Count>0&&page=="practice"&&ShellMotionEnabled;
        ReplacePracticeRuby(animateLine);var tokens=TokensFor(practiceSelected);
        double end=lyrics.Skip(practiceSelected+1).FirstOrDefault(l=>l.Time>lyrics[practiceSelected].Time)?.Time??audio.Duration;
        practiceKaraoke=KaraokeTimeline.Build(lyrics[practiceSelected],end,tokens,practiceWordSource.Lines);
        ToolTipService.SetToolTip(practiceRubyHost,practiceKaraoke.Exact?"逐字时间来自音源":"逐字进度按歌词与读音估算");
        bool hasRuby=tokens.Any(t=>ReadingDisplay.Ruby(t).Length>0);
        int character=0;
        foreach(var token in tokens){var reading=Text(ReadingDisplay.Ruby(token),Math.Max(17,fontSize*.46),token.Uncertain?.48:.8);reading.MinHeight=fontSize*.58;reading.Visibility=hasRuby?Visibility.Visible:Visibility.Collapsed;reading.HorizontalAlignment=HorizontalAlignment.Center;reading.Foreground=new SolidColorBrush(Color.FromArgb(255,255,210,222));
            var word=Text(token.Text,fontSize,.66);var highlight=Text(token.Text,fontSize);highlight.Foreground=new SolidColorBrush(Color.FromArgb(255,255,207,224));Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(highlight,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);var wordLayer=new Grid{HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};wordLayer.Children.Add(word);wordLayer.Children.Add(highlight);
            var visual=ElementCompositionPreview.GetElementVisual(highlight);var clip=visual.Compositor.CreateInsetClip();clip.RightInset=100000;visual.Clip=clip;int count=KaraokeTimeline.Elements(token.Text).Count;
            var karaokeWord=new PracticeKaraokeWord(highlight,visual,clip,character,count);practiceKaraokeWords.Add(karaokeWord);character+=count;highlight.SizeChanged+=(_,_)=>karaokeWord.Set(karaokeWord.Fraction);
            var pair=Column(reading,wordLayer);pair.Spacing=2;pair.Margin=new(0,0,3,compact?4:12);ruby!.Children.Add(pair);}
        SetText(practicePrevious!,practiceSelected>0?lyrics[practiceSelected-1].Text:"");SetText(practiceNext!,practiceSelected+1<lyrics.Count?lyrics[practiceSelected+1].Text:"");
        practicePrevious!.Visibility=!compact&&practicePrevious.Text.Length>0?Visibility.Visible:Visibility.Collapsed;practiceNext!.Visibility=!compact&&practiceNext.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        SetText(practiceTranslationText!,practiceTranslation?lyrics[practiceSelected].Translation:"");practiceTranslationText!.Visibility=practiceTranslation&&practiceTranslationText.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        SetText(practiceRomanText!,practiceRoman?ReadingDisplay.Romanization(lyrics[practiceSelected],tokens,practiceDocument.Corrections.ContainsKey(PracticeDocument.LineKey(lyrics[practiceSelected]))):"");
        practiceRomanText!.Visibility=practiceRoman&&practiceRomanText.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        if(loopButton!=null){loopButton.Content=practiceLoop?"停止循环":"循环此句";loopButton.Background=new SolidColorBrush(Color.FromArgb(practiceLoop?(byte)235:(byte)180,231,169,186));}
        if(practiceLines!=null){practiceLines.SelectedIndex=practiceSelected;if(practiceFollow)practiceLines.ScrollIntoView(lyrics[practiceSelected]);}
        if(lineChanged)practiceWordViewport?.ChangeView(null,0,null,true);
        UpdatePracticeKaraoke();if(animateLine)_=AnimatePracticeLine();
    }
    void SetPracticeWordTimeline(JsonElement? value){
        try{practiceWordSource=value is JsonElement source?JsonSerializer.Deserialize<KaraokeSource>(source,Storage.Json)??new("",[]):new("",[]);}catch(JsonException){practiceWordSource=new("",[]);}
        if(practiceWordSource.Lines==null)practiceWordSource=new("",[]);
        if(page=="practice")PaintPractice(true);
    }
    RubyPanel NewPracticeRuby(){var panel=new RubyPanel{Spacing=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.SizeChanged+=(_,_)=>{if(page=="practice"&&ReferenceEquals(panel,ruby))PaintPractice(false);};return panel;}
    static void ClearPracticeKaraoke(List<PracticeKaraokeWord> words){foreach(var word in words)word.Dispose();words.Clear();}
    void ReplacePracticeRuby(bool animate){
        StopPracticeTransition();if(practiceRubyHost==null)return;
        var previous=ruby;double width=previous?.ActualWidth??0,height=previous?.ActualHeight??0;
        practiceRubyHost.Children.Clear();
        if(animate&&previous!=null){
            previous.Width=width;previous.Height=height;practiceOutgoingWords=previous;
            practiceOutgoing=new Canvas{Height=0,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};practiceOutgoing.Children.Add(previous);practiceOutgoingKaraoke.AddRange(practiceKaraokeWords);practiceKaraokeWords.Clear();
        }else ClearPracticeKaraoke(practiceKaraokeWords);
        ruby=NewPracticeRuby();practiceRubyHost.Children.Add(ruby);if(practiceOutgoing!=null)practiceRubyHost.Children.Add(practiceOutgoing);
    }
    void StopPracticeTransition(){
        if(!practiceTransitionRunning&&practiceOutgoing==null)return;
        practiceTransitionRunning=false;
        practiceTransitionGeneration++;
        if(ruby!=null)ResetShell(ruby);
        if(practiceOutgoingWords!=null)ResetShell(practiceOutgoingWords);
        if(practiceOutgoing!=null)practiceRubyHost?.Children.Remove(practiceOutgoing);
        practiceOutgoing=null;practiceOutgoingWords=null;ClearPracticeKaraoke(practiceOutgoingKaraoke);
    }
    async Task AnimatePracticeLine(){
        if(ruby==null||practiceOutgoingWords==null||!ShellMotionEnabled)return;
        int generation=++practiceTransitionGeneration;practiceTransitions++;practiceTransitionRunning=true;
        _=MoveShell(practiceOutgoingWords,Vector3.Zero,new(0,-8,0),1,0,160);
        await MoveShell(ruby,new(0,10,0),Vector3.Zero,.35f,1,220);
        if(generation==practiceTransitionGeneration)StopPracticeTransition();
    }
    void StopPracticeMotion(){StopPracticeTransition();}
    void UpdatePracticeKaraoke(){
        if(practiceKaraoke==null||page!="practice"||!(App.Main?.IsVisibleForAnimation??true))return;
        double position=audio.Position-practiceDocument.Offset;practiceKaraokePosition=practiceKaraoke.Progress(position);
        foreach(var word in practiceKaraokeWords)word.Set(word.Length>0?Math.Clamp((practiceKaraokePosition-word.Offset)/word.Length,0,1):0);
    }
    sealed class PracticeKaraokeWord(TextBlock text,Visual visual,InsetClip clip,int offset,int length):IDisposable {
        public int Offset=>offset;public int Length=>length;public double Fraction {get;private set;}double width=-1;bool disposed;
        public void Set(double value){
            if(disposed)return;double nextWidth=text.ActualWidth;bool resized=Math.Abs(nextWidth-width)>.1;
            if(!resized&&Math.Abs(Fraction-value)<.002)return;
            Fraction=value;width=nextWidth;
            // Reuse the audio UI tick. Only the word currently filling changes its compositor clip.
            // A repeating 90 ms animation here kept the compositor active at display refresh rate.
            clip.RightInset=(float)Math.Max(0,nextWidth*(1-value));
        }
        public void Dispose(){if(disposed)return;disposed=true;clip.StopAnimation("RightInset");visual.Clip=null;clip.Dispose();}
    }
    async Task EditReadings(){
        if(current==null||practiceSelected<0||practiceSelected>=lyrics.Count)return;var track=current;var document=practiceDocument;var line=lyrics[practiceSelected];var before=TokensFor(practiceSelected).ToList();var inputs=new List<TextBox>();var romans=new List<TextBox>();var rows=new StackPanel{Spacing=8};
        rows.Children.Add(EqualColumns(Text("歌词",11,.5),Text("平假名",11,.5),Text("罗马音",11,.5)));
        foreach(var token in before){var input=new TextBox{FontFamily=UiFont,Text=token.Reading,MaxLength=200,PlaceholderText="平假名",HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0};inputs.Add(input);var original=Text(token.Text,18);original.VerticalAlignment=VerticalAlignment.Center;var roman=new TextBox{FontFamily=UiFont,Text=token.Roman,MaxLength=200,PlaceholderText="可选",HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0};romans.Add(roman);rows.Children.Add(EqualColumns(original,input,roman));}
        var hint=Text("只修改读音，原歌词保持不变",12,.6);
        var save=ActionButton("保存读音",()=>{var after=before.Select((t,i)=>t with{Reading=inputs[i].Text.Trim(),Roman=romans[i].Text.Trim(),Uncertain=false}).ToList();document.Correct(line,before,after);document.Save(track);openDialog?.Hide();InvalidateReadingDisplay();});
        var undo=ActionButton("撤销上次修正",()=>{if(document.Undo(line)){document.Save(track);openDialog?.Hide();InvalidateReadingDisplay();}});
        await Dialog("修正读音",Column(new ScrollViewer{Content=rows,MaxHeight=340,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch},hint,Row(save,undo)));
    }
    async Task ImportPracticeLyrics(){
        if(current==null)return;
        try{var track=current;int version=loadVersion;var picker=new Windows.Storage.Pickers.FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(App.Main));picker.FileTypeFilter.Add(".lrc");var file=await picker.PickSingleFileAsync();if(file==null||current?.Key!=track.Key)return;var text=await File.ReadAllTextAsync(file.Path);if(version!=loadVersion)return;var parsed=Lyrics.Parse(text);if(parsed.Count==0)throw new IOException("歌词没有有效时间戳");var folder=Path.Combine(Storage.Data,"practice-lyrics");Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,Path.GetFileNameWithoutExtension(PracticeDocument.PathFor(track))+".lrc"),text);lyrics=parsed;practiceReadings=[];practiceSelected=-1;practiceFollow=true;practiceLoop=false;practiceWordSource=new("",[]);ClearLyricTextures();Layout();PrepareTrackReadings();if(page=="practice")await BuildPractice();}catch(Exception e){ShowError(e);}
    }
}

// A word and its reading stay together when wrapping; only the current line is materialized.
public sealed class RubyPanel : Panel
{
    public double Spacing {get;set;}=8;
    protected override Size MeasureOverride(Size available){double x=0,y=0,row=0,width=double.IsInfinity(available.Width)?800:available.Width;foreach(var child in Children){child.Measure(new(width,double.PositiveInfinity));var s=child.DesiredSize;if(x>0&&x+s.Width>width){x=0;y+=row;row=0;}x+=s.Width+Spacing;row=Math.Max(row,s.Height);}return new(width,y+row);}
    protected override Size ArrangeOverride(Size final){double x=0,y=0,row=0;foreach(var child in Children){var s=child.DesiredSize;if(x>0&&x+s.Width>final.Width){x=0;y+=row;row=0;}child.Arrange(new(x,y,s.Width,s.Height));x+=s.Width+Spacing;row=Math.Max(row,s.Height);}return final;}
}

