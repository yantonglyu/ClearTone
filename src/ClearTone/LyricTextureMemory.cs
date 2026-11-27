namespace ClearTone;

public sealed partial class MainPage
{
    int textureFirst=-1,textureLast=-1,textureWidth=-1;
    const long LyricTextureBudget=12*1024*1024;
    void RemoveLyricTexture(string key){lyricTextures[key].Dispose();lyricTextures.Remove(key);}
    void PruneLyricTextures(int first,int last,int width){
        if(first==textureFirst&&last==textureLast&&width==textureWidth)return;
        textureFirst=first;textureLast=last;textureWidth=width;
        foreach(var key in lyricTextures.Keys.ToArray()){
            var parts=key.Split(':');int line=int.Parse(parts[0]);
            // Keep a small scroll margin and all blur variants of nearby lines.
            if(line<first-3||line>last+3||int.Parse(parts[^1])!=width)RemoveLyricTexture(key);
        }
    }
    void TrimLyricTextureBudget(string newest){
        long bytes=LyricTextureBytes;
        while(lyricTextures.Count>1&&(lyricTextures.Count>24||bytes>LyricTextureBudget)){
            string oldest=lyricTextures.Keys.First(k=>k!=newest);var image=lyricTextures[oldest];
            bytes-=(long)image.SizeInPixels.Width*image.SizeInPixels.Height*4;RemoveLyricTexture(oldest);
        }
    }
}
