using ClearTone;
using NAudio.Wave;
using System.Text.Json;
void Assert(bool value,string message){if(!value)throw new Exception(message);}
double Level(double frequency,bool eq){
    var tap=new AudioTap(new Tone(frequency));tap.SetEq(eq,[0,0,6,0,0]);var samples=new float[44100];tap.Read(samples.AsSpan());return Math.Sqrt(samples.Skip(22050).Select(v=>(double)v*v).Average());
}
double center=Level(1000,true)/Level(1000,false),low=Level(100,true)/Level(100,false);
Assert(center>.95&&center<1.05,"EQ 1kHz peak with headroom");Assert(low>.45&&low<.65,"EQ headroom away from band");
var fft=new AudioTap(new Tone(440));fft.Read(new float[8192].AsSpan());Assert(fft.Spectrum().Max()>.1,"Actual PCM spectrum");
var lowRate=new AudioTap(new Tone(440,22050));lowRate.SetEq(true,[0,0,0,0,12]);var lowSamples=new float[22050];lowRate.Read(lowSamples.AsSpan());Assert(lowSamples.All(float.IsFinite)&&lowSamples.Max()>.49&&lowSamples.Max()<=.501,"Out-of-band EQ is bypassed at 22050 Hz");
var lyrics=Lyrics.Merge("[00:01.25][00:02.50]hello\n[00:05]world","[00:01.25]你好","[00:02.50]he-lo");
Assert(JsonSerializer.Deserialize<Track>("{\"netEaseId\":\"42\"}",Storage.Json)?.NetEaseId=="42","Service track identifier mapping");
Assert(lyrics.Count==3&&lyrics[0].Translation=="你好"&&lyrics[1].Romanization=="he-lo","Multi-time bilingual lyrics");
var data=Path.Combine(AppContext.BaseDirectory,"isolated-data-"+Guid.NewGuid().ToString("N"));Environment.SetEnvironmentVariable("CLEARTONE_DATA",data);
var state=Storage.Load();state.TotalSeconds=3600;Storage.Save(state);var clock=new StudyClock(state);
clock.Toggle();await Task.Delay(250);clock.Checkpoint();double first=state.TotalSeconds;clock.Checkpoint();Assert(state.TotalSeconds-first<.1,"No duplicated checkpoint");
clock.Toggle();double rest=state.TotalSeconds;await Task.Delay(150);clock.Checkpoint();Assert(Math.Abs(state.TotalSeconds-rest)<.01,"Rest excludes elapsed");
clock.Finish();Assert(state.TotalSeconds>=3600.2&&state.TotalSeconds<3602,"Only actual study delta counted");
Assert(Math.Abs(Storage.Load().TotalSeconds-state.TotalSeconds)<.001,"Atomic state readback");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,eqCenterRatio=center,eqLowRatio=low,checks=new[]{"native PCM FFT","22050 Hz EQ Nyquist guard","EQ transfer ratios and headroom","multi-timestamp translation alignment","monotonic study accumulation","no double checkpoint","rest excluded","atomic persistence"}}));
var listening=new ListeningCounter();listening.Sample(0,0,0,true);
Assert(listening.Sample(5,5.1,5,true)==5,"Device rendered time counted");
listening.Sample(5,5.1,5,false);Assert(listening.Sample(5,5.1,105,false)==5,"Pause time excluded");
listening.Sample(5,5.1,105,true);Assert(listening.Sample(6,6.1,106,true)==6,"Resume has no pause backlog");
Assert(listening.Sample(600,7.1,107,true)==7,"Seek or device jump cannot add media distance");
Assert(listening.Sample(601,7,108,true)==7,"Silent device progress without decoded PCM excluded");
listening.Sample(0,7,109,true);Assert(listening.Sample(1,8,110,true)==8,"Device reset adds only newly rendered audio");
Assert(listening.Sample(1,900,120,true)==8,"Preload or analysis cannot add listening time");
var outbox=Path.Combine(data,"listening-test.json");int submissions=0;
var sync=new ListeningSync(outbox,r=>{submissions++;Assert(r.Seconds==8,"Only audible seconds sent");return Task.FromResult("sent");});
sync.Begin("7","42","9");sync.Checkpoint(8,true);await sync.Pump("7");Assert(submissions==0,"Active song not reported repeatedly");
sync.Finish();await sync.Pump("8");Assert(submissions==0,"Wrong account does not receive a report");
await sync.Pump("7");await sync.Pump("7");Assert(submissions==1,"Completed session sent once");
var recovery=new ListeningSync(outbox,_=>throw new IOException());recovery.Begin("7","42","9");recovery.Checkpoint(8,true);
var restarted=new ListeningSync(outbox,r=>{submissions++;return Task.FromResult("sent");});await restarted.Pump("7");Assert(submissions==2,"Saved real time recovered after exit");
Console.WriteLine("Listening counter and durable outbox checks passed");
sealed class Tone(double frequency,int rate=44100):ISampleProvider{
 long sample;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(rate,1);
 public int Read(Span<float> buffer){for(int i=0;i<buffer.Length;i++)buffer[i]=(float)(.5*Math.Sin(2*Math.PI*frequency*sample++/rate));return buffer.Length;}
}
