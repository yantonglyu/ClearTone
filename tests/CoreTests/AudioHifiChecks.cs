using ClearTone;
using NAudio.Wave;
using System.Buffers.Binary;
using System.Text;

internal static class AudioHifiChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception("Hi-Fi: "+message);}
    public static void Run(){
        float[] values=[-.75f,-.1f,0,.1f,.75f];
        Check(Render(values,new(false,0,true)).SequenceEqual(values),"default processing preserves in-range samples exactly");
        var attenuated=Render(values,new(false,-6,false));float scale=(float)Math.Pow(10,-6/20.0);
        Check(attenuated.Zip(values).All(x=>Math.Abs(x.First-x.Second*scale)<1e-7),"preamp matches the dB amplitude definition");
        var rg=new AudioFileMetadata("test",-6,-3,.9,.95);
        Check(Render(values,new(false,0,false,"track"),rg).SequenceEqual(attenuated),"track ReplayGain reads the track gain");
        Check(Render(values,new(false,0,false,"album"),rg).Zip(values).All(x=>Math.Abs(x.First-x.Second*(float)Math.Pow(10,-3/20.0))<1e-7),"album ReplayGain reads the album gain");
        Check(Render(values,new(false,0,false,"album")).SequenceEqual(values),"missing tags apply no invented gain");
        var ceiling=Render([.8f,-.8f],new(false,6,true,"track"),new("test",6,null,.8));
        Check(ceiling.All(x=>Math.Abs(x)<=1)&&Math.Abs(ceiling[0]-1)<1e-6,"tagged peak reserves sufficient gain headroom");
        Check(Render([1.25f,-1.25f],new(true,12,false,"track"),rg,true).SequenceEqual(new[]{1.25f,-1.25f}),"tone bypass disables EQ/preamp/RG without silently clipping float headroom");
        Check(Render([1.25f,-1.25f],new(false,0,true)).SequenceEqual(new[]{1f,-1f}),"sample peak guard can be enabled explicitly");
        Check(Render(values,new(false,double.NaN,false)).SequenceEqual(values),"invalid preamp cannot poison output samples");

        string folder=Path.Combine(Path.GetTempPath(),"ClearTone-audio-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            string path=Path.Combine(folder,"tags.flac");WriteMetadataFlac(path,["REPLAYGAIN_TRACK_GAIN=-6.25 dB","replaygain_album_gain=+2.0 dB","REPLAYGAIN_TRACK_PEAK=0.876","REPLAYGAIN_ALBUM_PEAK=1.012"]);
            var tags=AudioFileMetadata.Read(path);
            Check(tags.TrackGainDb==-6.25&&tags.AlbumGainDb==2&&tags.TrackPeak==.876&&tags.AlbumPeak==1.012,"FLAC comments preserve independent gains and peaks");
            Check(tags.Description=="FLAC · 96 kHz / 24-bit / 2 ch","FLAC STREAMINFO reports original precision, not float decoder precision");
            WriteMetadataFlac(path,["REPLAYGAIN_TRACK_GAIN=NaN dB","REPLAYGAIN_ALBUM_GAIN=999 dB","REPLAYGAIN_TRACK_PEAK=-1"]);
            tags=AudioFileMetadata.Read(path);Check(tags.TrackGainDb==null&&tags.AlbumGainDb==null&&tags.TrackPeak==null,"malformed tags are ignored");
            File.WriteAllBytes(path,"fLaC\x84\xff\xff\xff"u8.ToArray());Check(AudioFileMetadata.Read(path).TrackGainDb==null,"truncated metadata never prevents playback");
            Check(AudioFileMetadata.Read("https://invalid.example/track.flac")==AudioFileMetadata.Unknown,"remote sources do not trigger tag requests");
            string wav=Path.Combine(folder,"precision.wav");int[] ints=[1,17,65537,-17,8388607,-8388608];
            using(var writer=new WaveFileWriter(wav,new WaveFormat(96000,24,1))){foreach(int value in ints){writer.WriteByte((byte)value);writer.WriteByte((byte)(value>>8));writer.WriteByte((byte)(value>>16));}}
            using(var reader=AudioEngine.OpenReader(wav)){var samples=new float[ints.Length];int read=reader.ToSampleProvider().Read(samples);Check(read==samples.Length&&samples.Zip(ints).All(x=>x.First==x.Second/8388608f),"local 24-bit PCM retains low-order bits through the decoded float path");}
            Check(AudioFileMetadata.Read(wav).Description.Contains("24-bit"),"WAV source bit depth is separately reported");
            if(Environment.GetEnvironmentVariable("CLEARTONE_TEST_FFMPEG") is {Length:>0} ffmpeg){
                using(var writer=new WaveFileWriter(wav,new WaveFormat(96000,24,1))){for(int i=0;i<4096;i++){int value=ints[i%ints.Length];writer.WriteByte((byte)value);writer.WriteByte((byte)(value>>8));writer.WriteByte((byte)(value>>16));}}
                string flac=Path.Combine(folder,"precision.flac");
                var start=new System.Diagnostics.ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
                foreach(string arg in new[]{"-hide_banner","-loglevel","error","-y","-i",wav,"-c:a","flac","-metadata","REPLAYGAIN_TRACK_GAIN=-6.25 dB",flac})start.ArgumentList.Add(arg);
                using(var process=System.Diagnostics.Process.Start(start)!){string error=process.StandardError.ReadToEnd();process.WaitForExit();Check(process.ExitCode==0,"fixture FLAC encoding: "+error);}
                using var reader=AudioEngine.OpenReader(flac);var samples=new float[ints.Length];int read=reader.ToSampleProvider().Read(samples);
                Check(read==samples.Length&&samples.Zip(ints).All(x=>x.First==x.Second/8388608f),"24-bit FLAC low bits survive Media Foundation float negotiation");
                Check(AudioFileMetadata.Read(flac).TrackGainDb==-6.25,"ReplayGain reads a real encoder-produced FLAC");
                Console.WriteLine("Media Foundation FLAC precision verified: "+AudioFileMetadata.Describe(reader.WaveFormat));
            }
            using(var writer=new WaveFileWriter(wav,WaveFormat.CreateIeeeFloatWaveFormat(176400,2))){writer.WriteSamples([.0000001f,-.0000001f,1.25f,-1.25f],0,4);}
            using(var reader=AudioEngine.OpenReader(wav)){var samples=new float[4];reader.ToSampleProvider().Read(samples);Check(samples.SequenceEqual(new[]{.0000001f,-.0000001f,1.25f,-1.25f}),"float WAV compatibility path retains high-rate and float headroom");}
        }finally{foreach(string file in Directory.EnumerateFiles(folder))File.Delete(file);Directory.Delete(folder);}
        Console.WriteLine("Hi-Fi checks passed: exact bypass, preamp, ReplayGain track/album/missing tags, peak headroom, malformed tags, 24-bit and 176.4 kHz float WAV precision");
    }
    static float[] Render(float[] values,AudioProcessingOptions options,AudioFileMetadata? tags=null,bool eq=false){
        var tap=new AudioTap(new Samples(values));tap.SetMetadata(tags??AudioFileMetadata.Unknown);tap.SetEq(eq,[6,3,0,-3,6]);tap.ConfigureProcessing(options);var result=new float[values.Length];tap.Read(result);return result;
    }
    static void WriteMetadataFlac(string path,string[] tags){
        using var stream=File.Create(path);using var writer=new BinaryWriter(stream,Encoding.UTF8,true);writer.Write("fLaC"u8);writer.Write(new byte[]{0,0,0,34});
        var info=new byte[34];ulong packed=(96000UL<<44)|(1UL<<41)|(23UL<<36)|96000;BinaryPrimitives.WriteUInt64BigEndian(info.AsSpan(10,8),packed);writer.Write(info);
        using var comments=new MemoryStream();using(var w=new BinaryWriter(comments,Encoding.UTF8,true)){w.Write(0);w.Write(tags.Length);foreach(string tag in tags){byte[] text=Encoding.UTF8.GetBytes(tag);w.Write(text.Length);w.Write(text);}}
        writer.Write(new byte[]{132,(byte)(comments.Length>>16),(byte)(comments.Length>>8),(byte)comments.Length});writer.Write(comments.ToArray());
    }
    sealed class Samples(float[] samples):ISampleProvider{
        public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(44100,1);
        public int Read(Span<float> buffer){int count=Math.Min(samples.Length,buffer.Length);samples.AsSpan(0,count).CopyTo(buffer);return count;}
    }
}
