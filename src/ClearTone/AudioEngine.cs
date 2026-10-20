using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
namespace ClearTone;

public sealed class AudioEngine : IDisposable
{
    WaveStream? reader;WasapiPlayer? output;AudioTap? tap;readonly object gate=new();int generation;
    public event Action? Ended;
    public bool Playing=>output?.PlaybackState==PlaybackState.Playing;
    public double Duration=>reader?.TotalTime.TotalSeconds??0;
    public double Position=>reader?.CurrentTime.TotalSeconds??0;
    public int SampleRate=>reader?.WaveFormat.SampleRate??0;
    public async Task Load(string url,AppState state){
        int current=Interlocked.Increment(ref generation);
        var next=await Task.Run<WaveStream>(()=>new MediaFoundationReader(url));
        lock(gate){
            if(current!=generation){next.Dispose();return;}
            Release();reader=next;tap=new AudioTap(next.ToSampleProvider());tap.SetEq(state.Eq,state.Gains);
            output=new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithLatency(100).Build();
            output.Init(tap);output.Volume=(float)state.Volume;
            output.PlaybackStopped+=(_,e)=>{if(e.Exception==null&&Duration>0&&Position>=Duration-.3)Ended?.Invoke();};
        }
    }
    public void Play()=>output?.Play();
    public void Pause()=>output?.Pause();
    public void Seek(double seconds){lock(gate){if(reader!=null){bool playing=Playing;output?.Pause();reader.CurrentTime=TimeSpan.FromSeconds(Math.Clamp(seconds,0,Duration));if(playing)output?.Play();}}}
    public void Volume(double value){if(output!=null)output.Volume=(float)Math.Clamp(value,0,1);}
    public void Eq(bool enabled,double[] gains)=>tap?.SetEq(enabled,gains);
    public float[] Spectrum()=>tap?.Spectrum()??new float[48];
    void Release(){if(output!=null){output.Stop();output.Dispose();output=null;}reader?.Dispose();reader=null;}
    public void Dispose(){Interlocked.Increment(ref generation);lock(gate)Release();}
}
public sealed class AudioTap:ISampleProvider
{
    readonly ISampleProvider source;readonly float[] ring=new float[4096];int write;readonly object ringGate=new();
    BiQuadFilter[][]? filters;float preamp=1;
    readonly Complex[] fft=new Complex[4096];readonly float[] bars=new float[48];
    static readonly float[] hann=Enumerable.Range(0,4096).Select(i=>(float)(.5-.5*Math.Cos(2*Math.PI*i/4095))).ToArray();
    public WaveFormat WaveFormat=>source.WaveFormat;
    public AudioTap(ISampleProvider source)=>this.source=source;
    public void SetEq(bool enabled,double[] gains){
        if(!enabled){filters=null;preamp=1;return;}
        int[] frequencies=[60,250,1000,4000,12000];
        var g=Enumerable.Range(0,5).Select(i=>Math.Clamp(i<gains.Length?gains[i]:0,-12,12)).ToArray();
        var next=new BiQuadFilter[WaveFormat.Channels][];
        var supported=frequencies.Select((f,i)=>(f,i)).Where(x=>x.f<WaveFormat.SampleRate*.49).ToArray();
        for(int c=0;c<next.Length;c++)next[c]=supported.Select(x=>x.i==0?BiQuadFilter.LowShelf(WaveFormat.SampleRate,x.f,.8f,(float)g[x.i]):x.i==4?BiQuadFilter.HighShelf(WaveFormat.SampleRate,x.f,.8f,(float)g[x.i]):BiQuadFilter.PeakingEQ(WaveFormat.SampleRate,x.f,.8f,(float)g[x.i])).ToArray();
        preamp=(float)Math.Pow(10,-Math.Max(0,supported.Select(x=>g[x.i]).DefaultIfEmpty(0).Max())/20);filters=next;
    }
    public int Read(float[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public int Read(Span<float> buffer){
        int n=source.Read(buffer),channels=WaveFormat.Channels;var f=filters;
        lock(ringGate){
            for(int i=0;i<n;i+=channels){
                float mono=0;for(int c=0;c<channels&&i+c<n;c++){int p=i+c;float sample=buffer[p];mono+=sample;if(f!=null){sample*=preamp;foreach(var filter in f[c])sample=filter.Transform(sample);}buffer[p]=Math.Clamp(sample,-1,1);}
                ring[write]=mono/channels;write=(write+1)%ring.Length;
            }
        }
        return n;
    }
    public float[] Spectrum(){
        lock(ringGate)for(int i=0;i<4096;i++){fft[i].X=ring[(write+i)%4096]*hann[i];fft[i].Y=0;}
        FastFourierTransform.FFT(true,12,fft);
        for(int i=0;i<48;i++){
            int start=(int)(30*Math.Pow(16000.0/30,i/48.0)*4096/WaveFormat.SampleRate),end=Math.Max(start+1,(int)(30*Math.Pow(16000.0/30,(i+1)/48.0)*4096/WaveFormat.SampleRate));double sum=0;
            for(int k=start;k<Math.Min(end,2048);k++){var c=fft[k];double db=20*Math.Log10(Math.Sqrt(c.X*c.X+c.Y*c.Y)+1e-8);sum+=Math.Clamp((db+95)/65,0,1);}
            double raw=sum/Math.Max(1,Math.Min(end,2048)-start);bars[i]=(float)(.55*bars[i]+.45*raw*raw);
        }return bars;
    }
}
