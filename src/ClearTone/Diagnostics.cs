using Microsoft.UI.Xaml;
using System.Text.Json;
namespace ClearTone;

public sealed partial class MainPage
{
    // Opt-in local harness. Never enabled by the normal launcher.
    readonly bool testControl=Environment.GetCommandLineArgs().Contains("--test-control");
    string testCommandId="";
    bool controlBusy;
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
                        case "study":study.Toggle();break;
                        case "pause":audio.Pause();break;
                        case "play":audio.Play();break;
                        case "load-first":if(state.Queue.Count>0)await LoadTrack(0,false);break;
                        case "seek":audio.Seek(command.GetProperty("seconds").GetDouble());break;
                        case "finish-listening":audio.Pause();EndListening();await listening.Pump(accountId);break;
                        case "lyric-drag":BeginLyricDrag(300);MoveLyricDrag(100);EndLyricDrag(100);break;
                        case "lyric-follow":ReturnToLyrics();break;
                        case "library":_=Library();break;
                        case "close-dialog":openDialog?.Hide();break;
                    }
                }
            }
            await File.WriteAllTextAsync(Path.Combine(Storage.Data,"diagnostics.json"),JsonSerializer.Serialize(new{testCommandId,ready,playing=audio.Playing,position=audio.Position,rate=audio.SampleRate,lyrics=lyrics.Count,lyricRendering,lyricManual=DateTime.Now<lyricManualUntil,totalSeconds=state.TotalSeconds,studyRunning=study.Running,accountLogged,heardSeconds=audio.PlayedSeconds,syncSeconds=listening.Seconds,syncResult=listening.LastResult,notice=notice.Text},Storage.Json));
        }catch(Exception e){Storage.Log(e);}finally{controlBusy=false;}
    }
}
