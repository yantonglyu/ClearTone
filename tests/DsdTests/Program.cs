using ClearTone;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.Json;

if(args.Length!=2)throw new ArgumentException("Provide synthetic fixture and new cache directory");
string fixtures=Path.GetFullPath(args[0]);Storage.Data=Path.GetFullPath(args[1]);
if(Directory.Exists(Storage.Data))throw new IOException("Use a fresh test cache directory");
Directory.CreateDirectory(Storage.Data);File.WriteAllText(Path.Combine(Storage.Data,".cleartone-test"),"synthetic");
void Check(bool value,string message){if(!value)throw new Exception(message);}
var results=new List<object>();
foreach(string ext in new[]{"dsf","dff"}){
    string source=Path.Combine(fixtures,"silence-dsd64."+ext);
    byte[] original=SHA256.HashData(File.ReadAllBytes(source));
    string decoded=await DsdDecoder.Resolve(source);
    byte[] wav=File.ReadAllBytes(decoded);Check(wav.AsSpan(0,4).SequenceEqual("RIFF"u8),"WAV container");
    int cursor=12,rate=0,channels=0,bits=0,payload=0;
    while(cursor+8<=wav.Length){int size=BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(cursor+4));if(size<0||cursor+8L+size>wav.Length)break;var id=System.Text.Encoding.ASCII.GetString(wav,cursor,4);if(id=="fmt "){channels=BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(cursor+10));rate=BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(cursor+12));bits=BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(cursor+22));}if(id=="data")payload=size;cursor+=8+size+(size%2);}
    Check(rate==176400&&channels==2&&bits==32,"Expected PCM format");Check(payload==176400*2*4*2,"Decoded duration is 2 seconds");
    var cached=await DsdDecoder.Resolve(source);Check(cached==decoded,"Repeated play reuses cache");Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),"Source remains untouched");
    results.Add(new{ext,rate,channels,bits,seconds=payload/(double)(rate*channels*bits/8),cacheReused=true,sourceUnchanged=true});
}
using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool rejected=false;
try{await DsdDecoder.Resolve(Path.Combine(fixtures,"silence-dsd64.dsf"),cancelled.Token);}catch(OperationCanceledException){rejected=true;}
Check(rejected,"Cancellation honored even for cached DSD");Check(!Directory.EnumerateFiles(Storage.Data,"*.partial",SearchOption.AllDirectories).Any(),"No partial conversion remains");
var result=new{passed=true,results,cancellation=true};string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(Storage.Data,"result.json"),json);Console.WriteLine(json);

namespace ClearTone{public static class Storage{public static string Root=>"E:\\ClearTone";public static string Data{get;set;}="";}}
