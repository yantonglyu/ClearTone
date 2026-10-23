using Microsoft.UI.Xaml;
using System.Text.Json;
namespace ClearTone;

public sealed partial class MainPage
{
    // Opt-in local harness. Never enabled by the normal launcher.
    readonly bool testControl=Environment.GetCommandLineArgs().Contains("--test-control");
    string testCommandId="";
    bool controlBusy;
    double eggTestUntil,eggTestAge;
    public void RefreshVisibility(){SetMotion();WakeLyricAnimation();}
    async Task TestControl(){
        if(!testControl||controlBusy)return;
        controlBusy=true;
        try{
            var file=Path.Combine(Storage.Data,"control.json");
            if(File.Exists(file)){
                var command=JsonDocument.Parse(await File.ReadAllTextAsync(file)).RootElement;
                var id=command.GetProperty("id").GetString()??"";
                if(id!=testCommandId){
                    testCommandId=id;
                    switch(command.GetProperty("action").GetString()){
                        case "egg-test-phase":if(File.Exists(Path.Combine(Storage.Data,".cleartone-test"))){eggTestAge=Math.Clamp(command.GetProperty("seconds").GetDouble(),-.55,3);eggTestUntil=eggWatch.Elapsed.TotalSeconds+8;UpdateSongEgg();}break;
                        case "egg-test-end":eggTestUntil=0;break;
                        case "expand-player":await Navigate("now");break;
                        case "navigation-burst":await Task.WhenAll(Navigate("library"),Navigate("practice"),Navigate("now"),Navigate("discover"));break;
                        case "collapse-player":await Navigate(lastHomePage);break;
                        case "library-many":shelf.Tracks=Enumerable.Range(0,10000).Select(i=>new Track{Name="测试曲目 "+i,Artist="虚拟化测试",Url="test-"+i+".wav"}).ToList();await Navigate("library");break;
                        case "navigate":await Navigate(command.GetProperty("page").GetString()!);break;
                        case "practice-select":practiceSelected=command.GetProperty("index").GetInt32();practiceFollow=false;practiceLoop=true;SeekPractice();audio.Play();PaintPractice(true);break;
                        case "practice-speed":audio.SetSpeed(command.GetProperty("speed").GetDouble());break;
                        case "practice-correct":if(current!=null){var line=lyrics[practiceSelected];var before=TokensFor(practiceSelected);practiceDocument.Correct(line,before,before.Select((t,i)=>i==0?t with{Reading="てすと"}:t).ToList());practiceDocument.Save(current);PaintPractice(true);}break;
                        case "practice-toggle-loop":SetPracticeLoop(!practiceLoop);break;
                        case "practice-word-timeline":SetPracticeWordTimeline(command.GetProperty("timeline"));PaintPractice(true);break;
                        case "system-media-test":await TestSystemMediaSession();break;
                        case "cycle-reading":CyclePlaybackReadings();break;
                        case "settings":_=Settings();break;
                        case "practice-stop":StopPractice();audio.Pause();break;
                        case "shelf-add":if(current!=null)shelf.Add([current]);break;
                        case "study":ToggleStudy();break;
                        case "study-finish":FinishStudy();break;
                        case "pause":audio.Pause();break;
                        case "play":audio.Play();break;
                        case "load-first":if(state.Queue.Count>0)await LoadTrack(0,false);break;
                        case "load-index":int testIndex=command.GetProperty("index").GetInt32();if(testIndex>=0&&testIndex<state.Queue.Count)await LoadTrack(testIndex,false);break;
                        case "seek":SeekAudio(command.GetProperty("seconds").GetDouble());break;
                        case "finish-listening":audio.Pause();EndListening();await listening.Pump(accountId);break;
                        case "lyric-drag":BeginLyricDrag(300);MoveLyricDrag(100);EndLyricDrag(100);break;
                        case "lyric-follow":ReturnToLyrics();break;
                        case "library":_=Library();break;
                        case "close-dialog":openDialog?.Hide();break;
                    }
                }
            }
            await File.WriteAllTextAsync(Path.Combine(Storage.Data,"diagnostics.json"),JsonSerializer.Serialize(new{eggOpen=eggPopup?.IsOpen??false,eggOpacity=eggFrame.Opacity,eggBurst=eggFrame.Burst,eggBursts=eggClock.Bursts,eggMotion=eggFrame.Motion,eggTimerRunning=eggTimer.IsEnabled,eggStart=eggCue?.Start,testCommandId,ready,visible=App.Main?.IsVisibleForAnimation??true,playing=audio.Playing,position=audio.Position,duration=audio.Duration,rate=audio.SampleRate,audioFormat=audio.FormatDescription,audioProcessing=audio.ProcessingDescription,lyrics=lyrics.Count,lyricRendering,lyricManual=DateTime.Now<lyricManualUntil,totalSeconds=state.TotalSeconds,page,navigating,shellTransitions,trailStars=magicTrail?.Children.Count??0,shellMotionEnabled=ShellMotionEnabled,homeBuilds,realizedTracks=catalogList?.ItemsPanelRoot?.Children.Count??0,practiceKaraokeExact=practiceKaraoke?.Exact??false,practiceKaraokePosition,practiceTransitions,practiceTransitionRunning,practiceKaraokeWords=practiceKaraokeWords.Count,systemMediaReady=systemMedia!=null,systemMediaTestResult,practiceSelected,practiceLoop,practiceLoops,practiceFollow,playbackReadingMode=PlaybackReadingMode,playbackReadingsReady=practiceReadings.Count,practiceReady=practiceReadings.Count,practiceTokens=practiceSelected>=0&&practiceSelected<lyrics.Count?TokensFor(practiceSelected):[],speed=audio.Speed,shelfCount=shelf.Tracks.Count,studyRunning=study.Running,studyResting=study.Resting,studySeconds=study.Seconds,restSeconds=study.RestSeconds,studyDisplay=studyTime.Text,studyOpacity=studyTime.Opacity,accountLogged,heardSeconds=audio.PlayedSeconds,syncSeconds=listening.Seconds,syncResult=listening.LastResult,notice=notice.Text},Storage.Json));
        }catch(Exception e){Storage.Log(e);}finally{controlBusy=false;}
    }
}



