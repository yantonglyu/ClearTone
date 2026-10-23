using NAudio.Wave;
using SoundTouch;
namespace ClearTone;

// Keep SoundTouch in a separate replaceable DLL. At normal speed this is a direct bypass.
public sealed class TempoSource : ISampleProvider
{
    readonly ISampleProvider source;SoundTouchProcessor? processor;readonly object gate=new();
    float[]? input;bool ended;double speed=1;
    public WaveFormat WaveFormat=>source.WaveFormat;
    public double Speed {get{lock(gate)return speed;}}
    public long OutputFrames {get;private set;}
    public TempoSource(ISampleProvider source)=>this.source=source;
    public void SetSpeed(double value){lock(gate){
        speed=Math.Clamp(value,.5,1.5);
        if(speed!=1){processor??=new(){SampleRate=WaveFormat.SampleRate,Channels=WaveFormat.Channels};input??=new float[4096*WaveFormat.Channels];}
        processor?.Clear();if(processor!=null)processor.Tempo=speed;ended=false;OutputFrames=0;
    }}
    public void Reset(){lock(gate){processor?.Clear();ended=false;OutputFrames=0;}}
    public int Read(float[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public int Read(Span<float> buffer){lock(gate){
        int channels=WaveFormat.Channels,read=0;
        if(speed==1){read=source.Read(buffer);OutputFrames+=read/channels;return read;}
        while(read<buffer.Length-channels+1){
            var target=buffer[read..];int n=processor!.ReceiveSamples(target,target.Length/channels)*channels;read+=n;
            if(n>0)continue;if(ended)break;
            int supplied=source.Read(input.AsSpan());
            if(supplied==0){processor.Flush();ended=true;}else processor.PutSamples(input.AsSpan(0,supplied),supplied/channels);
        }
        OutputFrames+=read/channels;return read;
    }}
}
