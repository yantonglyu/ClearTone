using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Path=System.IO.Path;

namespace ClearTone;

public sealed partial class MainPage
{
    ArtistEffectProfile[] artistProfiles=[];
    ArtistEffectProfile? artistProfile;
    Canvas? artistLayer,artistBurst;
    Image? previousBackground;
    readonly List<Polygon> artistStars=[];
    Polyline? artistConstellation;
    bool artistLoopsRunning,artistEffectsClosed;
    int artistMotionVersion,backgroundMotionVersion;
    Size artistLayerSize;

    void InitializeArtistMotion()
    {
        if(artistLayer!=null||artistEffectsClosed)return;
        artistProfiles=ArtistEffectProfile.Load(Path.Combine(Storage.Data,"artist-effects"),Storage.Log);
        previousBackground=new Image{Stretch=Stretch.UniformToFill,IsHitTestVisible=false,Opacity=0};
        Root.Children.Insert(Math.Min(1,Root.Children.Count),previousBackground);
        artistLayer=new Canvas{IsHitTestVisible=false};
        // Background -> outgoing background -> tint -> stars -> every interactive UI element.
        Root.Children.Insert(Math.Min(3,Root.Children.Count),artistLayer);
    }

    async Task FadeOutTrackMotion(int version)
    {
        artistMotionVersion=version;
        RemoveArtistBurst();
        if(!ShellMotionEnabled||current==null)return;
        try
        {
            foreach(var element in new UIElement[]{cover,title,artist,lyricCanvas})FadeTrackElement(element,0,110);
            if(videoCanvas!=null&&videoCanvas.Visibility==Visibility.Visible)FadeTrackElement(videoCanvas,0,110);
            await Task.Delay(110);
        }
        catch(Exception error){Storage.Log(error);}
    }

    void ShowTrackMotion(int version,Track? track)
    {
        if(disposed||artistEffectsClosed||version!=loadVersion)return;
        artistMotionVersion=version;
        try
        {
            InitializeArtistMotion();
            if(ShellMotionEnabled)
            {
                foreach(var element in new UIElement[]{title,artist})FadeTrackElement(element,1,260);
                // Restore the previous cover softly while the next cover is fetched; a failed request
                // must not leave a permanently invisible album cover.
                FadeTrackElement(cover,1,260);
            }
            else RestoreTrackMotion(version);
            var profile=track?.IsVideo==true?null:artistProfiles.FirstOrDefault(p=>p.Matches(track?.Artist??""));
            if(artistProfile?.Id!=profile?.Id){artistProfile=profile;RebuildArtistStars();}
            RefreshArtistMotion();
            if(profile!=null&&ShellMotionEnabled&&!LiveLocked)_=BurstArtistStars(profile,version);
        }
        catch(Exception error){Storage.Log(error);RestoreTrackMotion(version);}
    }

    void RestoreTrackMotion(int version)
    {
        if(disposed||version!=loadVersion)return;
        foreach(var element in new UIElement[]{cover,title,artist,lyricCanvas})
        {
            var visual=ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Opacity");visual.Opacity=1;
        }
    }

    void ShowTrackLyricsMotion(int version)
    {
        if(disposed||version!=loadVersion)return;
        if(ShellMotionEnabled)FadeTrackElement(lyricCanvas,1,300,true);
        else{var visual=ElementCompositionPreview.GetElementVisual(lyricCanvas);visual.StopAnimation("Opacity");visual.Opacity=1;}
    }

    static void FadeTrackElement(UIElement element,float target,int milliseconds,bool fromZero=false)
    {
        var visual=ElementCompositionPreview.GetElementVisual(element);
        if(fromZero){visual.StopAnimation("Opacity");visual.Opacity=0;}
        using var animation=visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration=TimeSpan.FromMilliseconds(milliseconds);
        // No explicit start keyframe means rapid requests continue from their current value.
        animation.InsertKeyFrame(1,target);visual.StartAnimation("Opacity",animation);
    }

    void SetTrackCover(ImageSource image,int version)
    {
        if(disposed||version!=loadVersion)return;
        cover.Source=image;
        if(ShellMotionEnabled)FadeTrackElement(cover,1,320,true);
        else RestoreTrackMotion(version);
    }

    void SetTrackBackground(ImageSource image,int version)
    {
        if(disposed||version!=loadVersion)return;
        InitializeArtistMotion();
        var previous=background.Source;background.Source=image;
        if(previousBackground==null||previous==null||!ShellMotionEnabled)return;
        int transition=++backgroundMotionVersion;
        var currentVisual=ElementCompositionPreview.GetElementVisual(background);
        var outgoing=ElementCompositionPreview.GetElementVisual(previousBackground);
        // Match the current ambient crop exactly, so the outgoing image cannot jump at the cut.
        previousBackground.Source=previous;outgoing.Scale=currentVisual.Scale;
        ElementCompositionPreview.SetIsTranslationEnabled(previousBackground,true);
        if(currentVisual.Properties.TryGetVector3("Translation",out var translation)==CompositionGetValueStatus.Succeeded)
            outgoing.Properties.InsertVector3("Translation",translation);
        outgoing.StopAnimation("Opacity");outgoing.Opacity=.95f;
        FadeTrackElement(previousBackground,0,700);
        _=ClearPreviousBackground(transition);
    }

    async Task ClearPreviousBackground(int transition)
    {
        await Task.Delay(760);
        if(disposed||artistEffectsClosed||transition!=backgroundMotionVersion||previousBackground==null)return;
        previousBackground.Source=null;
    }

    void RebuildArtistStars()
    {
        if(artistLayer==null)return;
        StopArtistLoops();artistLayer.Children.Clear();artistStars.Clear();artistConstellation=null;
        if(artistProfile==null)return;
        if(artistProfile.Pattern=="starlines")
        {
            artistConstellation=new Polyline{Stroke=new SolidColorBrush(ArtistColor(artistProfile.Colors[0])),StrokeThickness=1.15,Opacity=artistProfile.Opacity*.62,IsHitTestVisible=false};
            artistLayer.Children.Add(artistConstellation);
        }
        var random=new Random(artistProfile.Id.Aggregate(17,(hash,c)=>unchecked(hash*31+c)));
        for(int i=0;i<artistProfile.Stars;i++)
        {
            bool signature=artistProfile.Pattern!="stars"&&i<3;
            double size=signature?(i==0?142:i==1?106:64):12+random.NextDouble()*20;
            var star=ArtistStar(size,ArtistColor(artistProfile.Colors[i%artistProfile.Colors.Length]),signature||i%3==0);
            // A paused first load can be removed before its animation ever starts.
            // Register Translation at creation, not only when playback begins.
            ElementCompositionPreview.SetIsTranslationEnabled(star,true);
            var position=new Point(.025+random.NextDouble()*.95,.16+random.NextDouble()*.64);
            if(signature)position=i switch{0=>new(.065,.43),1=>new(.935,.33),_=>new(.91,.73)};
            if(artistProfile.Pattern=="starlines"&&i>=3&&i<9)position=ConstellationPoint(i-3);
            star.Tag=position;
            star.Opacity=artistProfile.Opacity*.8;artistLayer.Children.Add(star);artistStars.Add(star);
        }
        artistLayerSize=default;PositionArtistStars();
    }

    static Color ArtistColor(string value)=>Color.FromArgb(255,Convert.ToByte(value.Substring(1,2),16),Convert.ToByte(value.Substring(3,2),16),Convert.ToByte(value.Substring(5,2),16));
    static Polygon ArtistStar(double size,Color color,bool outlined)
    {
        var points=new PointCollection();
        for(int i=0;i<10;i++){double angle=-Math.PI/2+i*Math.PI/5,r=size*(i%2==0?.5:.22);points.Add(new(size*.5+Math.Cos(angle)*r,size*.5+Math.Sin(angle)*r));}
        var brush=new SolidColorBrush(color);
        return new Polygon{Width=size,Height=size,Points=points,Fill=outlined?new SolidColorBrush(Color.FromArgb(size>=60?(byte)10:(byte)0,color.R,color.G,color.B)):brush,Stroke=outlined?brush:null,StrokeThickness=outlined?(size>=60?1.8:1.2):0,IsHitTestVisible=false};
    }

    static Point ConstellationPoint(int index)=>index switch{0=>new(.055,.22),1=>new(.19,.14),2=>new(.38,.18),3=>new(.61,.13),4=>new(.79,.19),_=>new(.95,.15)};

    void PositionArtistStars()
    {
        if(artistLayer==null)return;
        var size=new Size(ActualWidth,ActualHeight);if(size==artistLayerSize)return;artistLayerSize=size;
        artistLayer.Clip=new RectangleGeometry{Rect=new Rect(0,0,Math.Max(0,size.Width),Math.Max(0,size.Height))};
        foreach(var star in artistStars)if(star.Tag is Point p){Canvas.SetLeft(star,p.X*size.Width-star.Width/2);Canvas.SetTop(star,p.Y*size.Height-star.Height/2);}
        if(artistConstellation!=null){var points=new PointCollection();for(int i=0;i<6;i++){var p=ConstellationPoint(i);points.Add(new(p.X*size.Width,p.Y*size.Height));}artistConstellation.Points=points;}
    }

    void RefreshArtistMotion()
    {
        if(artistLayer==null||artistEffectsClosed)return;
        try
        {
            PositionArtistStars();
            bool visible=!disposed&&artistProfile!=null&&!LiveLocked&&current?.IsVideo!=true&&(App.Main?.IsVisibleForAnimation??true);
            artistLayer.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            if(artistBurst!=null)artistBurst.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            bool run=visible&&audio.Playing&&state.Motion&&motionSettings.AnimationsEnabled;
            if(run==artistLoopsRunning)return;
            if(!run){StopArtistLoops();return;}
            artistLoopsRunning=true;
            for(int i=0;i<artistStars.Count;i++)
            {
                var star=artistStars[i];ElementCompositionPreview.SetIsTranslationEnabled(star,true);
                var visual=ElementCompositionPreview.GetElementVisual(star);var compositor=visual.Compositor;
                using var drift=compositor.CreateVector3KeyFrameAnimation();drift.Duration=TimeSpan.FromSeconds(12+i%7);drift.IterationBehavior=AnimationIterationBehavior.Forever;
                drift.InsertKeyFrame(0,Vector3.Zero);drift.InsertKeyFrame(.5f,new Vector3(i%2==0?8:-8,-12-i%5*2,0));drift.InsertKeyFrame(1,Vector3.Zero);
                using var shimmer=compositor.CreateScalarKeyFrameAnimation();shimmer.Duration=TimeSpan.FromSeconds(5+i%4);shimmer.IterationBehavior=AnimationIterationBehavior.Forever;
                float opacity=(float)(artistProfile?.Opacity??.1);shimmer.InsertKeyFrame(0,opacity*.64f);shimmer.InsertKeyFrame(.5f,opacity);shimmer.InsertKeyFrame(1,opacity*.64f);
                if(artistProfile?.Pattern=="starlines"&&i>=3&&i<9)continue; // Fixed constellation vertices retain their alignment to the static hairline.
                visual.StartAnimation("Translation",drift);visual.StartAnimation("Opacity",shimmer);
            }
        }
        catch(Exception error){StopArtistLoops();Storage.Log(error);}
    }

    void StopArtistLoops()
    {
        artistLoopsRunning=false;
        foreach(var star in artistStars){ElementCompositionPreview.SetIsTranslationEnabled(star,true);var v=ElementCompositionPreview.GetElementVisual(star);v.StopAnimation("Translation");v.StopAnimation("Opacity");v.Properties.InsertVector3("Translation",Vector3.Zero);v.Opacity=(float)(artistProfile?.Opacity??.1)*.8f;}
    }

    async Task BurstArtistStars(ArtistEffectProfile profile,int version)
    {
        if(artistLayer==null)return;RemoveArtistBurst();
        var burst=new Canvas{IsHitTestVisible=false};artistBurst=burst;artistLayer.Children.Add(burst);
        try
        {
            for(int i=0;i<18;i++)
            {
                var star=ArtistStar(i%6==0?58:i%3==0?31:16,ArtistColor(profile.Colors[i%profile.Colors.Length]),i%2==0);
                // A rising sweep travels through both sides of the stage. Large outlines
                // are spaced between small glints rather than an indiscriminate particle spray.
                double phase=i/17d,x=ActualWidth*(.045+phase*.91),y=ActualHeight*(.64-.35*Math.Sin(phase*Math.PI));
                Canvas.SetLeft(star,x);Canvas.SetTop(star,y);burst.Children.Add(star);
                ElementCompositionPreview.SetIsTranslationEnabled(star,true);var v=ElementCompositionPreview.GetElementVisual(star);v.Opacity=0;
                using var move=v.Compositor.CreateVector3KeyFrameAnimation();move.Duration=TimeSpan.FromMilliseconds(1100+i*17);move.InsertKeyFrame(0,new Vector3(-12,34,0));move.InsertKeyFrame(1,new Vector3(22+i%3*8,-64-i%4*12,0));
                using var fade=v.Compositor.CreateScalarKeyFrameAnimation();fade.Duration=move.Duration;float delay=(float)(i/17d*.12);fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(delay,0);fade.InsertKeyFrame(.19f+delay,.78f);fade.InsertKeyFrame(.66f,.38f);fade.InsertKeyFrame(1,0);
                v.StartAnimation("Translation",move);v.StartAnimation("Opacity",fade);
            }
            await Task.Delay(1450);
        }
        catch(Exception error){if(!disposed)Storage.Log(error);}
        finally{if(artistBurst==burst&&artistMotionVersion==version)RemoveArtistBurst();}
    }

    void RemoveArtistBurst()
    {
        if(artistBurst==null)return;
        foreach(var child in artistBurst.Children){ElementCompositionPreview.SetIsTranslationEnabled(child,true);var v=ElementCompositionPreview.GetElementVisual(child);v.StopAnimation("Translation");v.StopAnimation("Opacity");}
        artistLayer?.Children.Remove(artistBurst);artistBurst.Children.Clear();artistBurst=null;
    }

    void CloseArtistMotion()
    {
        artistEffectsClosed=true;++backgroundMotionVersion;RemoveArtistBurst();StopArtistLoops();
        if(artistLayer!=null){artistLayer.Children.Clear();Root.Children.Remove(artistLayer);artistLayer=null;}
        if(previousBackground!=null){ElementCompositionPreview.GetElementVisual(previousBackground).StopAnimation("Opacity");previousBackground.Source=null;Root.Children.Remove(previousBackground);previousBackground=null;}
        artistStars.Clear();artistConstellation=null;
    }
}
