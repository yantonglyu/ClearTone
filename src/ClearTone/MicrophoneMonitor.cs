using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ClearTone;

// One independent, in-memory microphone path for the entire window. Never recorded
// or routed through music analysis, listening statistics, or the study clock.
public sealed class MicrophoneMonitor
{
    readonly SemaphoreSlim commands=new(1,1);
    WasapiCapture? capture;
    WasapiPlayer? output;
    MMDevice? inputDevice,outputDevice;
    VolumeSampleProvider? volume;
    volatile bool closing;
    float gain=.25f;
    long capturedBytes;
    public long CapturedBytes=>Interlocked.Read(ref capturedBytes);
    public bool Active=>capture!=null;
    public event Action<Exception>? Failed;
    public static IReadOnlyList<AudioOutputDevice> Inputs(){
        var result=new List<AudioOutputDevice>{new("","系统默认麦克风")};
        using var devices=new MMDeviceEnumerator();
        foreach(var device in devices.EnumerateAudioEndPoints(DataFlow.Capture,DeviceState.Active))
            using(device)result.Add(new(device.ID,device.FriendlyName));
        return result;
    }
    public void SetVolume(double value){gain=(float)Math.Clamp(double.IsFinite(value)?value:.25,0,1);if(volume is {} v)v.Volume=gain;}
    public Task Configure(bool enabled,string input,string destination)=>Task.Run(async()=>{
        await commands.WaitAsync();
        try{
            StopCore();if(!enabled||closing)return;
            using var devices=new MMDeviceEnumerator();
            inputDevice=input.Length==0?devices.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications):devices.GetDevice(input);
            outputDevice=destination.Length==0?devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):devices.GetDevice(destination);
            var next=new WasapiCapture(inputDevice,true,20);
            capture=next;Interlocked.Exchange(ref capturedBytes,0);
            var buffer=new BufferedWaveProvider(next.WaveFormat,TimeSpan.FromMilliseconds(160)){DiscardOnBufferOverflow=true,ReadFully=true};
            volume=new VolumeSampleProvider(new MicrophoneStereo(buffer.ToSampleProvider())){Volume=gain};
            next.DataAvailable+=(_,e)=>{
                if(!ReferenceEquals(capture,next))return;
                // Drop stale input instead of letting latency grow after an output stall.
                if(buffer.BufferedDuration.TotalMilliseconds>100)buffer.ClearBuffer();
                buffer.AddSamples(e.Buffer,0,e.BytesRecorded);Interlocked.Add(ref capturedBytes,e.BytesRecorded);
            };
            next.RecordingStopped+=(_,e)=>{if(ReferenceEquals(capture,next))DeviceStopped(next,e.Exception??new IOException("麦克风已停止"));};
            var render=new WasapiPlayerBuilder().WithDevice(outputDevice).WithSharedMode().WithEventSync().WithLatency(40).Build();output=render;
            render.PlaybackStopped+=(_,e)=>{if(ReferenceEquals(output,render))DeviceStopped(next,e.Exception??new IOException("耳返输出已停止"));};
            render.Init(volume.ToWaveProvider());render.Play();next.StartRecording();
        }catch{StopCore();throw;}finally{commands.Release();}
    });
    void DeviceStopped(WasapiCapture expected,Exception error){_=Task.Run(async()=>{await commands.WaitAsync();bool current=false;try{current=ReferenceEquals(capture,expected);if(current)StopCore();}finally{commands.Release();}if(current&&!closing)Failed?.Invoke(error);});}
    void StopCore(){
        var c=capture;capture=null;var o=output;output=null;volume=null;
        try{c?.StopRecording();}catch(Exception e){Storage.Log(e);}
        try{o?.Stop();}catch(Exception e){Storage.Log(e);}
        try{c?.Dispose();}catch(Exception e){Storage.Log(e);}
        try{o?.Dispose();}catch(Exception e){Storage.Log(e);}
        inputDevice?.Dispose();inputDevice=null;outputDevice?.Dispose();outputDevice=null;
    }
    public void Close(){closing=true;_=Configure(false,"","");}
}

// Microphone arrays can expose 4+ channels. Send a centered voice to both ears
// instead of asking WASAPI to render that capture layout on stereo headphones.
sealed class MicrophoneStereo(ISampleProvider source):ISampleProvider
{
    float[] scratch=[];
    public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate,2);
    public int Read(Span<float> buffer){
        int count=buffer.Length,offset=0;
        int channels=source.WaveFormat.Channels,needed=count/2*channels;
        if(scratch.Length<needed)scratch=new float[needed];
        int read=source.Read(scratch.AsSpan(0,needed)),frames=read/channels;
        for(int i=0;i<frames;i++){
            float sum=0;for(int c=0;c<channels;c++)sum+=scratch[i*channels+c];
            buffer[offset+2*i]=buffer[offset+2*i+1]=sum/channels;
        }
        return frames*2;
    }
}





