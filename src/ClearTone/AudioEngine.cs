using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
using NAudio.CoreAudioApi;
namespace ClearTone;

public sealed record AudioOutputDevice(string Id,string Name);

public sealed class AudioEngine : IDisposable
{
    internal Func<bool>? CanContinue;
    WaveStream? reader;WasapiPlayer? output;MMDevice? outputDevice;AudioTap? tap;readonly object gate=new();int generation;
    ListeningCounter listening=new();TempoSource? tempo;double tempoPosition;
    float volume=1;static readonly float[] emptySpectrum=new float[48];
    AudioProcessingOptions processing=new();AudioFileMetadata metadata=AudioFileMetadata.Unknown;
    public string OutputDeviceId {get;private set;}="";
    public string ActiveOutputDeviceId {get{lock(gate){return outputDevice?.ID??"";}}}
    public bool ExclusiveOutput {get;private set;}
    public bool ActiveExclusiveOutput {get;private set;}
    public string OutputNotice {get;private set;}="";
    public string OutputDescription=>output is null?"尚未播放":$"{output.DeviceFriendlyName} · {(ActiveExclusiveOutput?"WASAPI 独占":"WASAPI 共享")}";
    public int OutputSampleRate=>output?.OutputWaveFormat.SampleRate??0;
    public string SourceFormatDescription=>metadata.Description;
    public string DecodedFormatDescription {get;private set;}="尚未播放";
    public string OutputFormatDescription {get;private set;}="尚未播放";
    public string DeviceMixFormatDescription {get;private set;}="尚未播放";
    public string FormatDescription=>$"来源：{SourceFormatDescription}\n解码：{DecodedFormatDescription}\n输出流：{OutputFormatDescription}"+(ActiveExclusiveOutput?"\nWASAPI 独占，保留歌曲采样率":$"\nWindows 共享混音：{DeviceMixFormatDescription}");
    public string ProcessingDescription=>tap?.ProcessingDescription??"尚未播放";
    public static IReadOnlyList<AudioOutputDevice> GetOutputDevices(){
        var devices=new List<AudioOutputDevice>{new("","系统默认设备")};
        using var enumerator=new MMDeviceEnumerator();
        foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){
            using(device)devices.Add(new(device.ID,device.FriendlyName));
        }
        return devices;
    }
    public double Speed=>tempo?.Speed??1;
    public double PlayedSeconds {get{lock(gate){SampleListening();return listening.Seconds;}}}
    void SampleListening(){try{if(output!=null&&tap!=null)listening.Sample(output.GetPosition()/(double)output.OutputWaveFormat.AverageBytesPerSecond,tap.SuppliedSeconds,System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency,Playing);}catch{/* Device loss must not interrupt playback or study state. */}}
    public event Action? Ended;
    public bool Playing=>output?.PlaybackState==PlaybackState.Playing;
    public double Duration=>reader?.TotalTime.TotalSeconds??0;
    public double Position=>tempo!=null&&tempo.Speed!=1?Math.Min(Duration,tempoPosition+tempo.OutputFrames/(double)Math.Max(1,SampleRate)*tempo.Speed):reader?.CurrentTime.TotalSeconds??0;
    double presentationSource,presentationClock;
    double OutputClock()=>output==null?0:output.GetPosition()/(double)output.OutputWaveFormat.AverageBytesPerSecond;
    void AnchorPresentation(){presentationSource=Position;try{presentationClock=OutputClock();}catch{presentationClock=0;}}
    // A video blend follows samples delivered by the device, not the decoder read-ahead.
    public double PresentationPosition {get{lock(gate){try{return Math.Clamp(presentationSource+Math.Max(0,OutputClock()-presentationClock)*Speed,0,Duration);}catch{return Position;}}}}
    public int SampleRate=>reader?.WaveFormat.SampleRate??0;
    public async Task Load(string url,AppState state){
        int current=Interlocked.Increment(ref generation);
        var loaded=await Task.Run(()=> (Reader:OpenReader(url),Metadata:AudioFileMetadata.Read(url)));
        var next=loaded.Reader;
        lock(gate){
            if(current!=generation||CanContinue?.Invoke()==false){next.Dispose();return;}
            Release();listening=new();reader=next;metadata=loaded.Metadata;DecodedFormatDescription=AudioFileMetadata.Describe(next.WaveFormat);tempo=new TempoSource(next.ToSampleProvider());tempoPosition=0;tap=new AudioTap(tempo);tap.SetMetadata(metadata);tap.ConfigureProcessing(processing);tap.SetEq(state.Eq,state.Gains);volume=(float)Math.Clamp(state.Volume,0,1);OutputNotice="";
            // New tracks negotiate their own format inside the fallback boundary. Do not try the saved
            // route against the previous track first: a missing DAC must not block every later song.
            OutputDeviceId=state.OutputDeviceId??"";ExclusiveOutput=state.ExclusiveOutput;
            try{OpenOutput(OutputDeviceId,ExclusiveOutput);}
            catch when(ExclusiveOutput||OutputDeviceId.Length>0){
                // A disconnected DAC or unsupported exclusive sample rate must not strand the next song.
                OpenOutput("",false);OutputNotice="所选输出暂不可用，已使用系统默认设备的共享模式。";
            }
        }
    }
    // Float output avoids an unnecessary integer intermediate when the installed decoder supports it.
    // Some Media Foundation codecs only negotiate PCM, so retain the proven compatibility path.
    internal static WaveStream OpenReader(string url){
        if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.IsFile)url=uri.LocalPath;
        if(File.Exists(url)&&string.Equals(Path.GetExtension(url),".wav",StringComparison.OrdinalIgnoreCase)){
            var wav=new WaveFileReader(url);
            if(wav.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat||wav.WaveFormat is WaveFormatExtensible ext&&(ext.SubFormat==new Guid("00000001-0000-0010-8000-00aa00389b71")||ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71")))return wav;
            wav.Dispose();
        }
        try{return new MediaFoundationReader(url,new(){RequestFloatOutput=true});}
        catch(System.Runtime.InteropServices.COMException){return new MediaFoundationReader(url);}
    }
    public void ConfigureProcessing(AudioProcessingOptions options){lock(gate){processing=options;tap?.ConfigureProcessing(options);}}
    public void SelectOutput(string? deviceId,bool exclusive){
        lock(gate){
            deviceId??="";
            if(deviceId==OutputDeviceId&&exclusive==ExclusiveOutput&&(reader is null||output!=null&&exclusive==ActiveExclusiveOutput&&(deviceId.Length==0||outputDevice?.ID==deviceId)))return;
            if(reader is null){OutputDeviceId=deviceId;ExclusiveOutput=exclusive;return;}
            // Resolve and check the source format before pausing a working stream.
            var candidate=CreateOutput(deviceId,exclusive);
            try{var capability=candidate.Player.GetPlaybackCapability(tap!.WaveFormat);if(!capability.Supported)throw new NotSupportedException("此设备不支持歌曲采样率的独占播放，请使用共享模式。");}
            catch{candidate.Player.Dispose();candidate.Device.Dispose();throw;}
            bool playing=Playing;double position=Position;string previousDevice=OutputDeviceId,previousActualDevice=outputDevice?.ID??"";bool previousExclusive=ActiveExclusiveOutput,previousRequestedExclusive=ExclusiveOutput;
            bool candidateConsumed=false;
            try{
                Pause();CloseOutput();reader.CurrentTime=TimeSpan.FromSeconds(position);tempoPosition=position;tempo!.Reset();
                candidateConsumed=true;
                InitializeOutput(candidate.Player,candidate.Device,exclusive);
                OutputDeviceId=deviceId;ExclusiveOutput=exclusive;OutputNotice="";
                if(playing)Play();
            }catch(Exception error){
                // Initializing may fail when another app holds the device. Restore the previous route.
                if(!candidateConsumed){candidate.Player.Dispose();candidate.Device.Dispose();}
                CloseOutput();OutputDeviceId=previousDevice;ExclusiveOutput=previousRequestedExclusive;
                try{OpenOutput(previousActualDevice,previousExclusive);if(playing)Play();}
                catch{
                    CloseOutput();
                    try{OpenOutput("",false);if(playing)Play();OutputNotice="原输出设备暂不可用，已恢复系统默认共享输出。";}
                    catch(Exception fallback){CloseOutput();OutputNotice="输出设备暂不可用，请重新选择音频设备。";throw new InvalidOperationException(OutputNotice,new AggregateException(error,fallback));}
                }
                throw new InvalidOperationException("无法启用所选音频输出，已恢复可用输出。设备可能被占用或不支持此格式。",error);
            }
        }
    }
    static (WasapiPlayer Player,MMDevice Device) CreateOutput(string deviceId,bool exclusive){
        using var enumerator=new MMDeviceEnumerator();
        var device=deviceId.Length==0?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console):enumerator.GetDevice(deviceId);
        try{
            var builder=new WasapiPlayerBuilder().WithDevice(device).WithEventSync().WithLatency(100);
            return ((exclusive?builder.WithExclusiveMode():builder.WithSharedMode()).Build(),device);
        }catch{device.Dispose();throw;}
    }
    void OpenOutput(string deviceId,bool exclusive){var created=CreateOutput(deviceId,exclusive);InitializeOutput(created.Player,created.Device,exclusive);}
    void InitializeOutput(WasapiPlayer created,MMDevice device,bool exclusive){
        try{
            tap!.SoftwareVolume=exclusive?volume:1;created.Init(tap);
            // Session volume does not apply to exclusive streams. Never change endpoint/master volume.
            if(!exclusive)created.Volume=volume;
            OutputFormatDescription=AudioFileMetadata.Describe(created.OutputWaveFormat);
            DeviceMixFormatDescription=AudioFileMetadata.Describe(created.DeviceMixFormat);
            output=created;outputDevice=device;ActiveExclusiveOutput=exclusive;AnchorPresentation();int current=generation;
            created.PlaybackStopped+=(_,e)=>{
                if(ReferenceEquals(output,created)&&current==generation&&e.Exception==null&&Duration>0&&Position>=Duration-.3)Ended?.Invoke();
            };
        }catch{created.Dispose();device.Dispose();throw;}
    }
    public void Play(){lock(gate){if(CanContinue?.Invoke()==false)return;SampleListening();output?.Play();SampleListening();}}
    public void Pause(){lock(gate){SampleListening();output?.Pause();SampleListening();}}
    public void Seek(double seconds,Func<bool>? resume=null){lock(gate){if(reader!=null){bool playing=Playing;Pause();reader.CurrentTime=TimeSpan.FromSeconds(Math.Clamp(seconds,0,Duration));tempoPosition=reader.CurrentTime.TotalSeconds;tempo?.Reset();AnchorPresentation();if(resume?.Invoke()??playing)Play();}}}
    public void SetSpeed(double speed){lock(gate){if(reader==null||tempo==null||Math.Abs(speed-tempo.Speed)<.0001)return;bool playing=Playing;double position=Position;Pause();reader.CurrentTime=TimeSpan.FromSeconds(position);tempoPosition=position;tempo.SetSpeed(speed);AnchorPresentation();if(playing)Play();}}
    public void Volume(double value){lock(gate){volume=(float)Math.Clamp(value,0,1);if(tap!=null)tap.SoftwareVolume=ActiveExclusiveOutput?volume:1;if(output!=null&&!ActiveExclusiveOutput)output.Volume=volume;}}
    public void Eq(bool enabled,double[] gains){lock(gate)tap?.SetEq(enabled,gains);}
    public float[] Spectrum()=>tap?.Spectrum()??emptySpectrum;
    void CloseOutput(){var previous=output;output=null;try{previous?.Dispose();}finally{outputDevice?.Dispose();outputDevice=null;ActiveExclusiveOutput=false;}}
    void Release(){CloseOutput();reader?.Dispose();reader=null;tap=null;tempo=null;}
    public void Dispose(){Interlocked.Increment(ref generation);lock(gate)Release();}
}
public sealed class AudioTap:ISampleProvider
{
    readonly ISampleProvider source;readonly float[] ring=new float[4096];int write;readonly object ringGate=new();
    long supplied;public double SuppliedSeconds=>Interlocked.Read(ref supplied)/(double)(WaveFormat.SampleRate*WaveFormat.Channels);
    sealed record Dsp(BiQuadFilter[][]? Filters,float Gain,bool Protect,string Description);
    Dsp dsp=new(null,1,true,"音色处理关闭 · 采样峰值保护");
    AudioProcessingOptions options=new();AudioFileMetadata metadata=AudioFileMetadata.Unknown;
    bool eq;double[] eqGains=new double[5];
    readonly Complex[] fft=new Complex[4096];readonly float[] bars=new float[48];readonly (int Start,int End)[] bins=new (int,int)[48];
    public float SoftwareVolume {get;set;}=1;
    public string ProcessingDescription=>Volatile.Read(ref dsp).Description;
    static readonly float[] hann=Enumerable.Range(0,4096).Select(i=>(float)(.5-.5*Math.Cos(2*Math.PI*i/4095))).ToArray();
    public WaveFormat WaveFormat=>source.WaveFormat;
    public AudioTap(ISampleProvider source){
        this.source=source;
        for(int i=0;i<bins.Length;i++){
            int start=(int)(30*Math.Pow(16000.0/30,i/48.0)*4096/WaveFormat.SampleRate);
            int end=Math.Max(start+1,(int)(30*Math.Pow(16000.0/30,(i+1)/48.0)*4096/WaveFormat.SampleRate));
            bins[i]=(Math.Min(start,2048),Math.Min(end,2048));
        }
    }
    public void SetEq(bool enabled,double[] gains){
        eq=enabled;eqGains=Enumerable.Range(0,5).Select(i=>i<gains.Length&&double.IsFinite(gains[i])?Math.Clamp(gains[i],-12,12):0).ToArray();Rebuild();
    }
    public void ConfigureProcessing(AudioProcessingOptions value){options=value;Rebuild();}
    public void SetMetadata(AudioFileMetadata value){metadata=value;Rebuild();}
    void Rebuild(){
        int[] frequencies=[60,250,1000,4000,12000];
        var g=eqGains;BiQuadFilter[][]? next=null;double eqHeadroom=0;
        var supported=frequencies.Select((f,i)=>(f,i)).Where(x=>x.f<WaveFormat.SampleRate*.49).ToArray();
        if(eq&&!options.Bypass){
            next=new BiQuadFilter[WaveFormat.Channels][];
            for(int c=0;c<next.Length;c++)next[c]=supported.Select(x=>x.i==0?BiQuadFilter.LowShelf(WaveFormat.SampleRate,x.f,.8f,(float)g[x.i]):x.i==4?BiQuadFilter.HighShelf(WaveFormat.SampleRate,x.f,.8f,(float)g[x.i]):BiQuadFilter.PeakingEQ(WaveFormat.SampleRate,x.f,.8f,(float)g[x.i])).ToArray();
            eqHeadroom=-Math.Max(0,supported.Select(x=>g[x.i]).DefaultIfEmpty(0).Max());
        }
        string mode=options.ReplayGainMode;var rg=metadata.GainFor(mode);
        double gain=options.Bypass?0:(double.IsFinite(options.PreampDb)?Math.Clamp(options.PreampDb,-12,12):0)+(rg.Gain??0);
        if(!options.Bypass&&options.PeakProtection&&rg.Gain.HasValue&&rg.Peak is >0)gain=Math.Min(gain,-20*Math.Log10(rg.Peak.Value));
        var parts=new List<string>{options.Bypass?"音色处理旁路（保留音量 / 跟唱变速）":eq?"EQ 开启":"EQ 关闭"};
        if(!options.Bypass){
            parts.Add($"预放大 {Math.Clamp(double.IsFinite(options.PreampDb)?options.PreampDb:0,-12,12):+0.#;-0.#;0} dB");
            if(mode is "track" or "album")parts.Add(rg.Gain.HasValue?$"ReplayGain {(mode=="track"?"曲目":"专辑")} {rg.Gain.Value:+0.#;-0.#;0} dB":"无对应 ReplayGain 标签（0 dB）");
            if(gain!=(double.IsFinite(options.PreampDb)?Math.Clamp(options.PreampDb,-12,12):0)+(rg.Gain??0))parts.Add("已按标签峰值留出余量");
        }
        parts.Add(options.PeakProtection?"采样峰值保护":"峰值保护关闭");
        Volatile.Write(ref dsp,new(next,(float)Math.Pow(10,(gain+eqHeadroom)/20),options.PeakProtection,string.Join(" · ",parts)));
    }
    public int Read(float[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public int Read(Span<float> buffer){
        int n=source.Read(buffer),channels=WaveFormat.Channels;var settings=Volatile.Read(ref dsp);var f=settings.Filters;float gain=SoftwareVolume;
        lock(ringGate){
            for(int i=0;i<n;i+=channels){
                float mono=0;for(int c=0;c<channels&&i+c<n;c++){int p=i+c;float sample=buffer[p];mono+=sample;sample*=settings.Gain;if(f!=null)foreach(var filter in f[c])sample=filter.Transform(sample);buffer[p]=(settings.Protect?Math.Clamp(sample,-1,1):sample)*gain;}
                ring[write]=mono/channels;write=(write+1)&4095;
            }
        }
        Interlocked.Add(ref supplied,n);return n;
    }
    public float[] Spectrum(){
        lock(ringGate)for(int i=0;i<4096;i++){fft[i].X=ring[(write+i)&4095]*hann[i];fft[i].Y=0;}
        FastFourierTransform.FFT(true,12,fft);
        for(int i=0;i<48;i++){
            var(start,end)=bins[i];double sum=0;
            for(int k=start;k<end;k++){var c=fft[k];double db=20*Math.Log10(Math.Sqrt(c.X*c.X+c.Y*c.Y)+1e-8);sum+=Math.Clamp((db+95)/65,0,1);}
            double raw=sum/Math.Max(1,end-start);bars[i]=(float)(.55*bars[i]+.45*raw*raw);
        }return bars;
    }
}

