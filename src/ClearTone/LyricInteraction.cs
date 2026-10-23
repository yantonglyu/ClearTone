using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
namespace ClearTone;

public sealed partial class MainPage
{
    bool lyricPressed,lyricDragged,lyricBrowsingVisual,lyricRendering;
    double lyricPressY,lyricPressScroll,lyricTarget;
    long lyricLastFrame;
    DateTime lyricManualUntil=DateTime.MinValue;
    readonly Queue<double> lyricFrameIntervals=new(),lyricDrawTimes=new();
    int lyricAnimationFrames;
    void SetupLyricInteraction(){
        lyricCanvas.PointerPressed+=(_,e)=>{
            var point=e.GetCurrentPoint(lyricCanvas);
            if(!point.Properties.IsLeftButtonPressed&&point.PointerDeviceType==Microsoft.UI.Input.PointerDeviceType.Mouse)return;
            BeginLyricDrag(point.Position.Y);lyricCanvas.CapturePointer(e.Pointer);e.Handled=true;
        };
        lyricCanvas.PointerMoved+=(_,e)=>{if(!lyricPressed)return;MoveLyricDrag(e.GetCurrentPoint(lyricCanvas).Position.Y);e.Handled=true;};
        lyricCanvas.PointerReleased+=(_,e)=>{if(!lyricPressed)return;EndLyricDrag(e.GetCurrentPoint(lyricCanvas).Position.Y);lyricCanvas.ReleasePointerCapture(e.Pointer);e.Handled=true;};
        lyricCanvas.PointerCaptureLost+=(_,_)=>{lyricPressed=false;};
        lyricCanvas.PointerCanceled+=(_,_)=>{lyricPressed=false;};
        lyricCanvas.PointerWheelChanged+=(_,e)=>{BrowseLyrics(-e.GetCurrentPoint(lyricCanvas).Properties.MouseWheelDelta*.65);e.Handled=true;};
    }
    void StopLyricAnimation(){if(!lyricRendering)return;CompositionTarget.Rendering-=AnimateLyrics;lyricRendering=false;lyricLastFrame=0;}
    void WakeLyricAnimation(){
        if(page!="now"||disposed||!(App.Main?.IsVisibleForAnimation??true)||lyricPositions.Count==0){StopLyricAnimation();return;}
        bool browsing=lyricPressed||DateTime.Now<lyricManualUntil;
        var visibility=browsing?Visibility.Visible:Visibility.Collapsed;
        if(follow.Visibility!=visibility)follow.Visibility=visibility;
        if(browsing!=lyricBrowsingVisual){lyricBrowsingVisual=browsing;lyricCanvas.Invalidate();}
        if(!browsing)lyricTarget=lyricPositions[Math.Clamp(active,0,lyricPositions.Count-1)];
        if(lyricPressed||Math.Abs(lyricTarget-scrollIndex)<.15){StopLyricAnimation();return;}
        if(!lyricRendering){lyricRendering=true;lyricLastFrame=0;CompositionTarget.Rendering+=AnimateLyrics;}
    }
    double ClampLyricScroll(double value)=>Math.Clamp(value,0,lyricPositions.Count==0?0:lyricPositions[^1]);
    void BeginLyricDrag(double y){
        lyricPressed=true;lyricDragged=false;lyricPressY=y;lyricPressScroll=Math.Max(0,scrollIndex);
        lyricTarget=lyricPressScroll;lyricManualUntil=DateTime.Now.AddSeconds(5);
        WakeLyricAnimation();
    }
    void MoveLyricDrag(double y){
        if(!lyricPressed)return;
        if(Math.Abs(y-lyricPressY)>5)lyricDragged=true;
        if(!lyricDragged)return;
        scrollIndex=lyricTarget=ClampLyricScroll(lyricPressScroll+lyricPressY-y);
        lyricManualUntil=DateTime.Now.AddSeconds(5);lyricCanvas.Invalidate();
    }
    void EndLyricDrag(double y){
        bool click=!lyricDragged;lyricPressed=false;lyricManualUntil=DateTime.Now.AddSeconds(5);
        if(!click){WakeLyricAnimation();return;}
        double position=scrollIndex+y-lyricCanvas.ActualHeight*.35;
        int i=lyricPositions.FindLastIndex(p=>p<=position);
        if(i>=0&&i<lyrics.Count&&position<lyricPositions[i]+lyricHeights[i]){
            SeekAudio(lyrics[i].Time);lyricManualUntil=DateTime.MinValue;
        }
        WakeLyricAnimation();
    }
    void BrowseLyrics(double delta){
        lyricTarget=ClampLyricScroll((DateTime.Now<lyricManualUntil?lyricTarget:Math.Max(0,scrollIndex))+delta);
        lyricManualUntil=DateTime.Now.AddSeconds(5);
        WakeLyricAnimation();
    }
    void ReturnToLyrics(){lyricPressed=false;lyricManualUntil=DateTime.MinValue;follow.Visibility=Visibility.Collapsed;if(lyricPositions.Count>0)lyricTarget=lyricPositions[Math.Clamp(active,0,lyricPositions.Count-1)];lyricCanvas.Invalidate();WakeLyricAnimation();}
    void AnimateLyrics(object? sender,object e){
        long now=Stopwatch.GetTimestamp();
        double dt=lyricLastFrame==0?1d/60:(double)(now-lyricLastFrame)/Stopwatch.Frequency;
        if(dt<1d/60-.001)return;
        lyricLastFrame=now;
        if(page!="now"||disposed||!(App.Main?.IsVisibleForAnimation??true)||lyricPositions.Count==0){StopLyricAnimation();return;}
        bool browsing=lyricPressed||DateTime.Now<lyricManualUntil;
        var visibility=browsing?Visibility.Visible:Visibility.Collapsed;if(follow.Visibility!=visibility)follow.Visibility=visibility;
        if(browsing!=lyricBrowsingVisual){lyricBrowsingVisual=browsing;lyricCanvas.Invalidate();}
        if(lyricPressed){StopLyricAnimation();return;}
        if(DateTime.Now>=lyricManualUntil)lyricTarget=lyricPositions[Math.Clamp(active,0,lyricPositions.Count-1)];
        double difference=lyricTarget-scrollIndex;
        if(Math.Abs(difference)<.15){StopLyricAnimation();return;}
        // Frame-time based easing, independent of audio polling and display refresh rate.
        scrollIndex+=difference*(1-Math.Exp(-12*Math.Min(dt,.05)));
        lyricCanvas.Invalidate();
        if(testControl){lyricAnimationFrames++;lyricFrameIntervals.Enqueue(dt*1000);while(lyricFrameIntervals.Count>600)lyricFrameIntervals.Dequeue();}
    }
}

