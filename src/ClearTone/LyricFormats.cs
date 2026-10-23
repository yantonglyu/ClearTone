using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
namespace ClearTone;

public sealed partial class MainPage
{
    LyricFontSet? lyricFormats;
    bool preparingLyricFormats;
    async Task PrepareLyricFormats(float size){
        preparingLyricFormats=true;
        try{
            // File font resolution uses the Windows storage broker. Realize every
            // format on an MTA worker, never from a CanvasControl Draw callback.
            var prepared=await Task.Run(()=>new LyricFontSet(size));
            if(disposed){prepared.Dispose();return;}
            ClearLyricTextures();lyricFormats?.Dispose();lyricFormats=prepared;
        }catch(Exception e){Storage.Log(e);}
        finally{preparingLyricFormats=false;if(!disposed)lyricCanvas.Invalidate();}
    }
    sealed class LyricFontSet:IDisposable {
        public readonly float Size;
        public readonly CanvasTextFormat Main,Translation,Roman,Reading,Empty;
        public LyricFontSet(float size){
            Size=size;
            Main=Make(size,650,1.45f);Translation=Make(size*.52f,450,1.65f);Roman=Make(size*.46f,450,1.65f);
            Reading=Make(Math.Max(10,size*.43f),500,0);Empty=Make(26,400,0);
            foreach(var format in new[]{Main,Translation,Roman,Reading,Empty}){
                using var layout=new CanvasTextLayout(CanvasDevice.GetSharedDevice(),"音あAa",format,500,100);
                _=layout.LayoutBounds;
            }
        }
        static CanvasTextFormat Make(float size,ushort weight,float spacing){
            var f=new CanvasTextFormat{FontFamily=CanvasFont,FontSize=size,FontWeight=new Windows.UI.Text.FontWeight{Weight=weight},WordWrapping=CanvasWordWrapping.Wrap};
            if(spacing>0){f.LineSpacing=size*spacing;f.LineSpacingBaseline=size*spacing*.8f;}return f;
        }
        public void Dispose(){Main.Dispose();Translation.Dispose();Roman.Dispose();Reading.Dispose();Empty.Dispose();}
    }
}
