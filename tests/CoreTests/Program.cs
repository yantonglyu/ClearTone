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
sealed class Tone(double frequency,int rate=44100):ISampleProvider{
 long sample;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(rate,1);
 public int Read(Span<float> buffer){for(int i=0;i<buffer.Length;i++)buffer[i]=(float)(.5*Math.Sin(2*Math.PI*frequency*sample++/rate));return buffer.Length;}
}
