using ClearTone;
using NAudio.Wave;
using System.Diagnostics;
using System.Reflection;

internal static class BackgroundAudioChecks
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run(){
        using var blocked=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();
        var worker=new MediaCommandWorker("blocked decoder regression");
        var operation=worker.Post(()=>{entered.Set();blocked.Wait();});Check(entered.Wait(1000),"Worker starts injected blocked native operation");
        int applied=0;var commands=new List<Task>();var watch=Stopwatch.StartNew();
        for(int n=1;n<=100;n++){int value=n;commands.Add(worker.Post(()=>applied=value,"seek"));}
        Check(watch.ElapsedMilliseconds<100,"Rapid seek posting never waits for native operation");
        blocked.Set();await operation;foreach(var command in commands)try{await command;}catch(OperationCanceledException){}
        Check(applied==100&&commands.Count(t=>t.IsCompletedSuccessfully)==1,"Only latest pending seek executes");
        bool surfaced=false;try{await worker.Post(()=>throw new IOException("injected decoder failure"));}catch(IOException){surfaced=true;}Check(surfaced,"Worker errors remain observable");
        blocked.Reset();entered.Reset();var blockedClose=worker.Post(()=>{entered.Set();blocked.Wait();});Check(entered.Wait(1000),"Close regression entered blocked native call");
        watch.Restart();worker.Dispose();Check(watch.ElapsedMilliseconds<100,"Window close never joins blocked native thread");blocked.Set();await blockedClose;
        Console.WriteLine("Media worker regression passed: blocked I/O, 100 coalesced seeks, propagated errors and nonblocking close");
        if(Environment.GetEnvironmentVariable("CLEARTONE_TEST_BACKGROUND_AUDIO")=="1")await PhysicalRecovery();
    }
    static async Task PhysicalRecovery(){
        string path=Path.Combine(AppContext.BaseDirectory,"background-silence-"+Guid.NewGuid().ToString("N")+".wav");
        using(var wav=new WaveFileWriter(path,new WaveFormat(44100,16,2))){byte[] samples=new byte[44100*4];for(int n=0;n<8;n++)wav.Write(samples,0,samples.Length);}
        var engine=new BackgroundAudioEngine(TimeSpan.FromMilliseconds(300));var notices=new List<string>();engine.Failed+=e=>notices.Add(e.Message);
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        try{
            await engine.Load(path,new AppState{Volume=0});
            var session=typeof(BackgroundAudioEngine).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine)!;
            var worker=(MediaCommandWorker)session.GetType().GetField("Worker")!.GetValue(session)!;
            var stalled=worker.Post(()=>{entered.Set();release.Wait();});Check(entered.Wait(1000),"Audio worker blocked as a stalled network seek");
            var seek=engine.Seek(5);var timer=Stopwatch.StartNew();
            for(int n=0;n<10000;n++){_=engine.Position;_=engine.PlayedSeconds;_=engine.PresentationPosition;_=engine.Spectrum();}
            engine.Pause();engine.Volume(0);Check(timer.ElapsedMilliseconds<100,"All UI snapshot reads and controls stay responsive during native stall");
            await seek.WaitAsync(TimeSpan.FromSeconds(5));
            Check(Math.Abs(engine.Position-5)<.2&&!engine.Playing,"Timed-out decoder retires and replacement seeks while paused");
            Check(engine.PlayedSeconds==0&&notices.Count==1,"Recovery does not invent listening time and reports one sparse notice");
            timer.Restart();engine.Dispose();Check(timer.ElapsedMilliseconds<100,"Audio facade close stays responsive with retired native decoder");
            release.Set();await stalled;await Task.Delay(200);
            Console.WriteLine("Background audio physical regression passed: cached UI reads under stall, bounded decoder replacement, paused seek, no false heard time, nonblocking close");
        }finally{release.Set();engine.Dispose();await Task.Delay(150);File.Delete(path);}
    }
}
