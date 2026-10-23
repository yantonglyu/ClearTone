using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace ClearTone;

// Optional compatibility decoder. DSD is converted to PCM, never sent as DoP.
public static class DsdDecoder
{
    const long CacheBudget=1024L*1024*1024;
    static readonly SemaphoreSlim serial=new(1,1);
    public static bool IsDsd(string path)=>Path.GetExtension(path).ToLowerInvariant() is ".dsf" or ".dff";
    public static string? FindFfmpeg(){
        string? configured=Environment.GetEnvironmentVariable("CLEARTONE_FFMPEG");if(!string.IsNullOrWhiteSpace(configured)&&File.Exists(configured))return Path.GetFullPath(configured);
        var bundled=Path.Combine(Storage.Root,"runtime","ffmpeg.exe");if(File.Exists(bundled))return bundled;
        var binaries=Path.Combine(Storage.Root,"analysis-runtime","Lib","site-packages","imageio_ffmpeg","binaries");
        return Directory.Exists(binaries)?Directory.EnumerateFiles(binaries,"ffmpeg-win-x86_64-*.exe").OrderByDescending(Path.GetFileName).FirstOrDefault():null;
    }
    public static string CachePath(string source){
        var file=new FileInfo(Path.GetFullPath(source));
        var identity=file.FullName+"\n"+file.Length+"\n"+file.LastWriteTimeUtc.Ticks+"\n176400-f32-v1";
        return Path.Combine(Storage.Data,"decoded-audio",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))))+".wav";
    }
    public static async Task<string> Resolve(string source,CancellationToken token=default){
        if(!IsDsd(source))return source;
        if(!File.Exists(source))throw new FileNotFoundException("找不到这个 DSD 文件",source);
        string target=CachePath(source);
        await serial.WaitAsync(token);
        try{
            if(File.Exists(target)&&new FileInfo(target).Length>44){File.SetLastWriteTimeUtc(target,DateTime.UtcNow);return target;}
            string executable=FindFfmpeg()??throw new NotSupportedException("DSD 兼容播放需要 FFmpeg。当前安装未提供解码器，普通音频仍可正常播放。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string partial=target+".partial";
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var argument in new[]{"-nostdin","-hide_banner","-v","error","-threads","2","-i",Path.GetFullPath(source),"-map","0:a:0","-vn","-ar","176400","-acodec","pcm_f32le","-fs",CacheBudget.ToString(),"-f","wav","-y",partial})info.ArgumentList.Add(argument);
            using var process=Process.Start(info)??throw new IOException("无法启动 DSD 解码器");
            try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
            using var cancellation=token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch{}});
            var errorTask=process.StandardError.ReadToEndAsync();
            try{
                await process.WaitForExitAsync(token);string error=await errorTask;token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0||!File.Exists(partial)||new FileInfo(partial).Length<=44)throw new IOException("DSD 解码失败。请确认文件完整且为受支持的 DSF / DFF。"+(error.Length>0?" "+error[..Math.Min(180,error.Length)]:""));
                if(new FileInfo(partial).Length>=CacheBudget-65536)throw new IOException("这个 DSD 文件转换后超过 1 GB，暂不缓存整张超长录音。原文件没有修改。");
                File.Move(partial,target,true);TrimCache(target);return target;
            }catch(OperationCanceledException){
                try{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch{}
                throw;
            }finally{if(File.Exists(partial))File.Delete(partial);}
        }finally{serial.Release();}
    }
    static void TrimCache(string keep){
        var directory=Path.GetFullPath(Path.GetDirectoryName(keep)!)+Path.DirectorySeparatorChar;
        var files=new DirectoryInfo(directory).GetFiles("*.wav").OrderBy(f=>f.LastWriteTimeUtc).ToList();long total=files.Sum(f=>f.Length);
        foreach(var file in files){if(total<=CacheBudget)break;if(file.FullName.Equals(keep,StringComparison.OrdinalIgnoreCase)||!file.FullName.StartsWith(directory,StringComparison.OrdinalIgnoreCase))continue;try{long size=file.Length;file.Delete();total-=size;}catch(IOException){}}
    }
}
