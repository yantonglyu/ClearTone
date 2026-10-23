using ClearTone;
using NAudio.Dsp;
using NAudio.Wave;

internal static class AudioChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run(){
        AudioHifiChecks.Run();
        // The normal-speed path must remain sample-exact and avoid initializing the time stretcher.
        var expected=new float[8192];new Signal().Read(expected);
        var tempo=new TempoSource(new Signal());var normal=new float[8192];tempo.Read(normal);
        Check(normal.SequenceEqual(expected),"Normal-speed bypass is sample-exact");
        var processor=typeof(TempoSource).GetField("processor",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        Check(processor.GetValue(tempo)==null,"Normal-speed playback does not allocate SoundTouch");
        tempo.SetSpeed(.8);Check(processor.GetValue(tempo)!=null,"Practice speed initializes SoundTouch when needed");

        // Cached FFT boundaries must preserve the original 48-bar picture at different source rates.
        foreach(int rate in new[]{22050,44100,48000,96000}){
            var tap=new AudioTap(new Signal(rate));var samples=new float[8192];tap.Read(samples);
            var old=ReferenceSpectrum(samples[^4096..],rate);var actual=tap.Spectrum();
            Check(actual.Zip(old).All(pair=>Math.Abs(pair.First-pair.Second)<1e-7),"Spectrum unchanged at "+rate);
        }

        var quiet=new AudioTap(new Signal()){SoftwareVolume=.25f};var quarter=new float[8192];quiet.Read(quarter);
        Check(quarter.Zip(expected).All(pair=>Math.Abs(pair.First-pair.Second*.25f)<1e-7),"Exclusive software volume preserves gain without endpoint volume changes");
        quiet.SoftwareVolume=0;quiet.Read(quarter);Check(quarter.All(x=>x==0),"Exclusive software mute produces silent PCM");

        using var engine=new AudioEngine();engine.SelectOutput("test-device",true);
        Check(engine.OutputDeviceId=="test-device"&&engine.ExclusiveOutput&&!engine.ActiveExclusiveOutput,"Saved output preference does not acquire a device before playback");
        Check(ReferenceEquals(engine.Spectrum(),engine.Spectrum()),"Unloaded spectrum does not allocate per tick");
        Console.WriteLine("Audio checks passed: exact bypass, lazy SoundTouch, FFT equivalence at four rates, software gain/mute, passive device preference");
        if(Environment.GetEnvironmentVariable("CLEARTONE_TEST_AUDIO_DEVICES")=="1"){
            var devices=AudioEngine.GetOutputDevices();
            Check(devices.Count>0&&devices[0].Id=="","Output device list starts with system default");
            Check(devices.Select(x=>x.Id).Distinct().Count()==devices.Count,"Output endpoints do not contain duplicates");
            Console.WriteLine($"Read-only audio endpoint enumeration passed: {devices.Count-1} active endpoints");
        }
        if(Environment.GetEnvironmentVariable("CLEARTONE_TEST_AUDIO_OUTPUT")=="1")AudioOutputSmoke.Run().GetAwaiter().GetResult();
    }
    static float[] ReferenceSpectrum(float[] samples,int rate){
        var fft=new Complex[4096];for(int i=0;i<fft.Length;i++){fft[i].X=samples[i]*(float)(.5-.5*Math.Cos(2*Math.PI*i/4095));}
        FastFourierTransform.FFT(true,12,fft);var bars=new float[48];
        for(int i=0;i<48;i++){
            int start=(int)(30*Math.Pow(16000.0/30,i/48.0)*4096/rate),end=Math.Max(start+1,(int)(30*Math.Pow(16000.0/30,(i+1)/48.0)*4096/rate));double sum=0;
            for(int k=start;k<Math.Min(end,2048);k++){var c=fft[k];double db=20*Math.Log10(Math.Sqrt(c.X*c.X+c.Y*c.Y)+1e-8);sum+=Math.Clamp((db+95)/65,0,1);}
            double raw=sum/Math.Max(1,Math.Min(end,2048)-start);bars[i]=(float)(.45*raw*raw);
        }return bars;
    }
    sealed class Signal(int rate=44100):ISampleProvider{
        int position;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(rate,1);
        public int Read(Span<float> buffer){for(int i=0;i<buffer.Length;i++){buffer[i]=(float)(.35*Math.Sin(2*Math.PI*440*position/rate)+.12*Math.Cos(2*Math.PI*1713*position/rate));position++;}return buffer.Length;}
    }
}
