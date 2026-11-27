using ClearTone;
using NAudio.Wave;
foreach(int channels in new[]{1,2,4}){
 var stereo=new MicrophoneStereo(new Signal(channels));float[] b=new float[16];
 if(stereo.Read(b)!=16||b.Any(x=>Math.Abs(x-.25f)>1e-6))throw new Exception("Channel conversion failed: "+channels);
}
Console.WriteLine("PASS mono, stereo and four-channel centered conversion");
if(!args.Contains("--hardware"))return;
var monitor=new MicrophoneMonitor();monitor.SetVolume(0);
for(int i=0;i<3;i++){await monitor.Configure(true,"","");await Task.Delay(500);if(!monitor.Active||monitor.CapturedBytes==0)throw new Exception("No capture");await monitor.Configure(false,"","");if(monitor.Active)throw new Exception("Stop failed");}
try{await monitor.Configure(true,"invalid-device","");throw new Exception("Expected failure");}catch(Exception e) when(e.Message!="Expected failure"){if(monitor.Active)throw new Exception("Failure retained device");}
await monitor.Configure(true,"","");await Task.Delay(200);monitor.Close();await Task.Delay(500);if(monitor.Active)throw new Exception("Close failed");Console.WriteLine("PASS muted hardware capture, restart, failure recovery and close");
sealed class Signal(int channels):ISampleProvider{public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(48000,channels);public int Read(Span<float> buffer){buffer.Fill(.25f);return buffer.Length;}}
namespace ClearTone{public record AudioOutputDevice(string Id,string Name);static class Storage{public static void Log(Exception error)=>Console.Error.WriteLine(error.Message);}}
