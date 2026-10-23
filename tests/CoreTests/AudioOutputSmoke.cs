using ClearTone;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using System.Text.Json;

// Opt in only after the UI performance baseline is finished. No account or persisted app state is used.
internal static class AudioOutputSmoke
{
    static void Check(bool value,string message){if(!value)throw new Exception("Physical output smoke: "+message);}
    public static async Task Run(){
        var devices=AudioEngine.GetOutputDevices();
        using var enumerator=new MMDeviceEnumerator();
        using var device=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console);
        float masterBefore=device.AudioEndpointVolume.MasterVolumeLevelScalar;
        object exclusiveCapability;
        // Capability probing does not initialize or acquire an exclusive stream.
        using(var probe=new WasapiPlayerBuilder().WithDevice(device).WithExclusiveMode().WithEventSync().Build()){
            var capability=probe.GetPlaybackCapability(WaveFormat.CreateIeeeFloatWaveFormat(44100,2));
            exclusiveCapability=new{capability.Supported,rate=capability.OutputFormat.SampleRate,capability.Reason};
        }

        string path=Path.Combine(AppContext.BaseDirectory,"output-silence-"+Guid.NewGuid().ToString("N")+".wav");
        using(var wav=new WaveFileWriter(path,new WaveFormat(44100,16,2))){var silence=new byte[44100*4];for(int i=0;i<8;i++)wav.Write(silence,0,silence.Length);}
        var audio=new AudioEngine();var elapsed=Stopwatch.StartNew();double beforeSwitch=0,afterSwitch=0,paused=0,final=0;
        try{
            await audio.Load(path,new AppState{Volume=0,Eq=false});
            Check(!audio.Playing&&!audio.ActiveExclusiveOutput,"initial stream is paused and shared");
            audio.Play();await SampleFor(audio,550);beforeSwitch=audio.PlayedSeconds;
            Check(beforeSwitch>.2&&beforeSwitch<1.1,"default shared stream advances actual device time");
            var actualDescription=audio.OutputDescription;
            bool rejected=false;try{audio.SelectOutput("cleartone-test-missing-endpoint",false);}catch{rejected=true;}
            Check(rejected&&audio.Playing&&audio.OutputDescription==actualDescription,"unavailable endpoint is rejected before interrupting the working stream");
            double afterRejected=audio.PlayedSeconds;Check(afterRejected>=beforeSwitch&&afterRejected-beforeSwitch<.3,"rejected selection preserves accumulated listening time");
            audio.ConfigureProcessing(new(false,-6,true,"track"));
            Check(audio.Playing&&audio.PlayedSeconds>=afterRejected,"changing live processing preserves rendering and heard time");
            double position=audio.Position;
            audio.SelectOutput(device.ID,false);afterSwitch=audio.PlayedSeconds;
            Check(audio.Playing,"switching to the explicit default endpoint preserves play state");
            Check(afterSwitch>=beforeSwitch&&afterSwitch-beforeSwitch<.3,"device replacement preserves the listening accumulator");
            Check(Math.Abs(audio.Position-position)<.2,"device replacement preserves the source position");
            await SampleFor(audio,350);Check(audio.PlayedSeconds>afterSwitch+.1,"the rebuilt shared stream continues rendering");

            audio.Pause();paused=audio.PlayedSeconds;await SampleFor(audio,300);
            Check(Math.Abs(audio.PlayedSeconds-paused)<.01,"paused time does not enter the listening accumulator");
            audio.Seek(5);Check(Math.Abs(audio.PlayedSeconds-paused)<.01,"seeking does not report the skipped media time");
            audio.SelectOutput("",false);
            Check(!audio.Playing&&audio.OutputDeviceId.Length==0&&!audio.ActiveExclusiveOutput,"returning to default shared preserves pause state");
            Check(Math.Abs(audio.Position-5)<.2,"paused device switch preserves the seek position");
            Check(Math.Abs(audio.PlayedSeconds-paused)<.01,"paused device switch does not reset or add listening time");
            audio.ConfigureProcessing(new(true,12,false,"album"));
            Check(Math.Abs(audio.PlayedSeconds-paused)<.01&&!audio.Playing,"DSP bypass changes do not accrue paused time or unpause");
            audio.Play();await SampleFor(audio,350);audio.Pause();final=audio.PlayedSeconds;
            Check(final>paused+.1&&final<=elapsed.Elapsed.TotalSeconds,"resumed time increases without counting media distance");
            var unavailable=new AppState{Volume=0,Eq=false,OutputDeviceId="cleartone-test-missing-endpoint",ExclusiveOutput=true};
            for(int track=0;track<2;track++){
                await audio.Load(path,unavailable);
                Check(audio.OutputDeviceId==unavailable.OutputDeviceId&&audio.ExclusiveOutput&&!audio.ActiveExclusiveOutput,"each new track preserves unavailable preference while falling back to shared output");
                Check(audio.OutputNotice.Length>0,"new-track fallback is visible");
                audio.Play();await SampleFor(audio,200);audio.Pause();
                Check(audio.PlayedSeconds>.05,"consecutive tracks with an unavailable saved endpoint still render");
            }
            Check(Math.Abs(device.AudioEndpointVolume.MasterVolumeLevelScalar-masterBefore)<.0001,"system master volume is unchanged");
            Console.WriteLine(JsonSerializer.Serialize(new{physicalOutputSmoke="passed",activeEndpoints=devices.Count-1,exclusiveCapability,beforeSwitch,afterRejected,afterSwitch,paused,final,elapsedSeconds=elapsed.Elapsed.TotalSeconds,systemMasterUnchanged=true,exclusiveStreamOpened=false,unavailablePreferenceConsecutiveLoads=2,formats=audio.FormatDescription}));
        }finally{
            try{audio.Pause();audio.SelectOutput("",false);}finally{audio.Dispose();File.Delete(path);}
        }
    }
    static async Task SampleFor(AudioEngine audio,int milliseconds){
        var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<milliseconds){await Task.Delay(25);_=audio.PlayedSeconds;}
    }
}
