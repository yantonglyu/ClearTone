using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NAudio.Wave;
namespace ClearTone;

public sealed record AudioProcessingOptions(bool Bypass=false,double PreampDb=0,bool PeakProtection=true,string ReplayGainMode="off");

// Read only the original local file's headers. Network sources never trigger a second download or a loudness scan.
public sealed record AudioFileMetadata(string Description="来源格式未提供",double? TrackGainDb=null,double? AlbumGainDb=null,double? TrackPeak=null,double? AlbumPeak=null)
{
    public static readonly AudioFileMetadata Unknown=new();
    public (double? Gain,double? Peak) GainFor(string mode)=>mode switch{
        "track"=>(TrackGainDb,TrackPeak),"album"=>(AlbumGainDb,AlbumPeak),_=>(null,null)};
    public static AudioFileMetadata Read(string location){
        try{
            if(Uri.TryCreate(location,UriKind.Absolute,out var uri)){if(!uri.IsFile)return Unknown;location=uri.LocalPath;}
            if(!File.Exists(location))return Unknown;
            using var input=File.Open(location,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            Span<byte> header=stackalloc byte[4];input.ReadExactly(header);
            if(header.SequenceEqual("fLaC"u8))return ReadFlac(input);
            if(header.SequenceEqual("RIFF"u8))return ReadWave(input);
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){/* Optional metadata must never prevent playback. */}
        return Unknown;
    }
    public static string Describe(WaveFormat format){
        string kind=format.Encoding==WaveFormatEncoding.IeeeFloat||format is WaveFormatExtensible ext&&ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71")?"float":format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible?"PCM":format.Encoding.ToString();
        return $"{format.SampleRate/1000.0:0.###} kHz / {format.BitsPerSample}-bit {kind} / {format.Channels} ch";
    }
    static AudioFileMetadata ReadWave(Stream input){
        Span<byte> header=stackalloc byte[8];Span<byte> format=stackalloc byte[40];
        input.ReadExactly(header);if(!header[4..].SequenceEqual("WAVE"u8))return Unknown;
        for(int chunks=0;chunks<128&&input.Position+8<=input.Length;chunks++){
            input.ReadExactly(header);uint length=BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);long end=input.Position+length;
            if(end>input.Length)break;
            if(header[..4].SequenceEqual("fmt "u8)&&length>=16){
                int count=(int)Math.Min(length,40);input.ReadExactly(format[..count]);
                ushort tag=BinaryPrimitives.ReadUInt16LittleEndian(format),channels=BinaryPrimitives.ReadUInt16LittleEndian(format[2..]),bits=BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
                uint rate=BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                string kind=tag==3?"float":tag==1?"PCM":((WaveFormatEncoding)tag).ToString();
                // NAudio may expose WAVE_FORMAT_EXTENSIBLE as WaveFormatExtraData. Read the file's
                // actual SubFormat GUID rather than inferring integer PCM from that managed type.
                if(tag==65534&&count>=40&&BinaryPrimitives.ReadUInt16LittleEndian(format[16..])>=22){
                    var subtype=new Guid(format[24..40]);
                    if(subtype==new Guid("00000003-0000-0010-8000-00aa00389b71"))kind="float";
                    else if(subtype==new Guid("00000001-0000-0010-8000-00aa00389b71"))kind="PCM";
                }
                return new($"WAV · {rate/1000.0:0.###} kHz / {bits}-bit {kind} / {channels} ch");
            }
            input.Position=end+(length&1);
        }
        return new("WAV · 格式未提供");
    }
    static AudioFileMetadata ReadFlac(Stream input){
        string description="FLAC";double? track=null,album=null,trackPeak=null,albumPeak=null;
        Span<byte> header=stackalloc byte[4];Span<byte> info=stackalloc byte[34];
        // Artwork and padding are skipped, not allocated. Bound malformed metadata traversal and tag allocations.
        for(int blocks=0;blocks<128&&input.Position+4<=input.Length;blocks++){
            input.ReadExactly(header);bool last=(header[0]&128)!=0;int type=header[0]&127;
            int length=header[1]<<16|header[2]<<8|header[3];long end=input.Position+length;
            if(end>input.Length)break;
            if(type==0&&length==34){
                input.ReadExactly(info);
                ulong value=BinaryPrimitives.ReadUInt64BigEndian(info[10..18]);
                int rate=(int)(value>>44),channels=(int)((value>>41)&7)+1,bits=(int)((value>>36)&31)+1;
                description=$"FLAC · {rate/1000.0:0.###} kHz / {bits}-bit / {channels} ch";
            }else if(type==4&&length<=1024*1024){
                byte[] bytes=new byte[length];input.ReadExactly(bytes);var tags=Comments(bytes);
                track=Number(tags,"REPLAYGAIN_TRACK_GAIN",false);album=Number(tags,"REPLAYGAIN_ALBUM_GAIN",false);
                trackPeak=Number(tags,"REPLAYGAIN_TRACK_PEAK",true);albumPeak=Number(tags,"REPLAYGAIN_ALBUM_PEAK",true);
            }
            input.Position=end;if(last)break;
        }
        return new(description,track,album,trackPeak,albumPeak);
    }
    static Dictionary<string,string> Comments(ReadOnlySpan<byte> data){
        var tags=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int offset=0;
        if(data.Length<8)return tags;uint vendor=BinaryPrimitives.ReadUInt32LittleEndian(data);
        if(vendor>(uint)(data.Length-8))return tags;offset=4+(int)vendor;
        uint count=BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);offset+=4;
        for(uint i=0;i<count&&i<4096&&offset+4<=data.Length;i++){
            uint length=BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);offset+=4;
            if(length>(uint)(data.Length-offset))break;
            if(length<=1024){string item=Encoding.UTF8.GetString(data.Slice(offset,(int)length));int equals=item.IndexOf('=');if(equals>0)tags[item[..equals]]=item[(equals+1)..];}
            offset+=(int)length;
        }
        return tags;
    }
    static double? Number(Dictionary<string,string> tags,string name,bool peak){
        if(!tags.TryGetValue(name,out string? text))return null;
        text=text.Trim();if(text.EndsWith("dB",StringComparison.OrdinalIgnoreCase))text=text[..^2].Trim();
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value))return null;
        return peak?value>0&&value<=100?value:null:value>=-60&&value<=60?value:null;
    }
}
