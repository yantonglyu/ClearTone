using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using System.Text.Json;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage {
    readonly SongEggClock eggClock=new();
    readonly List<SongEggProfile> eggProfiles=[];
    readonly DispatcherTimer eggTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    Popup? eggPopup;CanvasControl? eggCanvas;EggCue? eggCue;EggFrame eggFrame;
    readonly HashSet<Popup> eggOtherPopups=[];
    readonly List<CanvasRenderTarget> eggSnowSprites=[];
    CanvasRadialGradientBrush? eggGlow;int eggResolvedVersion=-1,eggLyricCount=-1;double eggDuration;
    readonly System.Diagnostics.Stopwatch eggWatch=System.Diagnostics.Stopwatch.StartNew();
    void InitializeSongEggs(){
        var directory=Path.Combine(Storage.Data,"easter-eggs");Directory.CreateDirectory(directory);
        var defaults=Path.Combine(AppContext.BaseDirectory,"Assets","EasterEggs");
        if(Directory.Exists(defaults))foreach(var file in Directory.EnumerateFiles(defaults)){
            string target=Path.Combine(directory,Path.GetFileName(file));if(!File.Exists(target))File.Copy(file,target);
        }
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))try{
            var profile=JsonSerializer.Deserialize<SongEggProfile>(File.ReadAllText(file));
            if(profile is {Id.Length:>0,Title.Length:>0,Anchor.Length:>0})eggProfiles.Add(profile);
        }catch(Exception e){Storage.Log(e);}
        eggTimer.Tick+=(_,_)=>UpdateSongEgg();
    }
    void UpdateSongEgg(){
        if(disposed)return;
        if(eggResolvedVersion!=loadVersion||eggLyricCount!=lyrics.Count||eggDuration!=audio.Duration){
            eggResolvedVersion=loadVersion;eggLyricCount=lyrics.Count;eggDuration=audio.Duration;
            eggCue=current==null?null:eggProfiles.Select(p=>p.Resolve(current,lyrics,audio.Duration)).FirstOrDefault(c=>c!=null);
        }
        bool visible=App.Main?.IsVisibleForAnimation??true,reduced=!motionSettings.AnimationsEnabled;
        eggFrame=eggClock.Update(trackReady?eggCue:null,audio.Position,audio.Playing,visible,reduced,eggWatch.Elapsed.TotalSeconds);
        // Bounded, opt-in render snapshots for visual regression in isolated profiles only.
        bool artPreview=testControl&&eggWatch.Elapsed.TotalSeconds<eggTestUntil;
        if(artPreview)eggFrame=new(eggTestAge<0?0:1,eggTestAge<0?0:Math.Exp(-eggTestAge*2.3),eggWatch.Elapsed.TotalSeconds,true,eggTestAge<0?.85:0,eggTestAge);
        if(!visible){eggTimer.Stop();if(eggPopup!=null)eggPopup.IsOpen=false;return;}
        if(!eggFrame.Active){eggTimer.Stop();if(eggPopup!=null)eggPopup.IsOpen=false;return;}
        if(eggCanvas==null){
            eggCanvas=new CanvasControl{ClearColor=Microsoft.UI.Colors.Transparent,IsHitTestVisible=false};
            eggCanvas.Draw+=DrawSongEgg;
            eggCanvas.CreateResources+=(_,_)=>{eggGlow?.Dispose();eggGlow=new CanvasRadialGradientBrush(eggCanvas,Color.FromArgb(230,255,204,103),Color.FromArgb(0,255,119,25));BuildEggSnowSprites();};
            eggPopup=new Popup{XamlRoot=XamlRoot,Child=eggCanvas,IsHitTestVisible=false,IsLightDismissEnabled=false,ShouldConstrainToRootBounds=true,AllowFocusOnInteraction=false};
        }
        eggCanvas.Width=XamlRoot.Size.Width;eggCanvas.Height=XamlRoot.Size.Height;
        // A noninteractive popup covers pages, sheets and flyouts without blocking controls.
        var popups=VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot);
        bool newlyOpened=popups.Any(p=>!ReferenceEquals(p,eggPopup)&&!eggOtherPopups.Contains(p));
        eggOtherPopups.Clear();foreach(var p in popups)if(!ReferenceEquals(p,eggPopup))eggOtherPopups.Add(p);
        if(eggPopup!.IsOpen&&newlyOpened)eggPopup.IsOpen=false;
        eggPopup.IsOpen=true;eggCanvas.Invalidate();
        // Paused music retains a still image. A short exit fade can finish independently.
        bool settling=eggFrame.Opacity>.002&&(!trackReady||eggCue==null||audio.Position<eggCue.Start||audio.Position>=eggCue.End)||eggFrame.Opacity<.995&&eggCue!=null&&audio.Position>=eggCue.Start&&audio.Position<eggCue.End-eggCue.FadeOut;
        if(artPreview||audio.Playing&&!reduced||settling||eggFrame.Burst>.002||eggFrame.Preheat>.002)eggTimer.Start();else eggTimer.Stop();
    }
    static Color EggColor(double alpha,byte r,byte g,byte b)=>Color.FromArgb((byte)Math.Clamp(alpha*255,0,255),r,g,b);
    void CloseSongEgg(){eggTimer.Stop();if(eggPopup!=null){eggPopup.IsOpen=false;eggPopup.Child=null;}foreach(var image in eggSnowSprites)image.Dispose();eggSnowSprites.Clear();eggGlow?.Dispose();eggCanvas?.RemoveFromVisualTree();eggCanvas=null;eggPopup=null;}
}
