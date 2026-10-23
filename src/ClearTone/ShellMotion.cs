using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace ClearTone;

public sealed partial class MainPage
{
    readonly Windows.UI.ViewManagement.UISettings motionSettings=new();
    bool navigating; string? pendingPage;
    int shellTransitions; Canvas? magicTrail;
    long suppressShellClickUntil;float shellDragOffset;
    bool SuppressShellClick=>Environment.TickCount64<suppressShellClickUntil;
    bool ShellMotionEnabled=>IsLoaded&&!disposed&&motionSettings.AnimationsEnabled&&(App.Main?.IsVisibleForAnimation??true);

    // Serialize transitions, keeping the latest destination when navigation is clicked rapidly.
    // Only visual transforms are animated. Audio, clocks and saved state have no animation dependency.
    async Task Navigate(string target){
        if(target is not ("now" or "discover" or "library" or "practice"))return;
        pendingPage=target;if(navigating)return;
        navigating=true;
        try{
            while(pendingPage is string next&&!disposed){
                pendingPage=null;if(next==page)continue;
                string previous=page;bool player=previous=="now"||next=="now";
                bool animate=ShellMotionEnabled;
                var outgoing=previous=="now"?body:workspace;
                float distance=(float)Math.Clamp(ActualHeight*.42,160,380);
                if(animate){
                    if(previous=="now")_=MagicTrail(false);
                    await MoveShell(outgoing,new(0,shellDragOffset,0),previous=="now"?new(0,Math.Max(distance,shellDragOffset+100),0):new(-16,0,0),1,0,previous=="now"?280:100);
                }
                shellDragOffset=0;
                await NavigateCore(next);
                ResetShell(outgoing);
                var incoming=next=="now"?body:workspace;
                if(animate&&ShellMotionEnabled){
                    if(next=="now")_=MagicTrail(true);
                    await MoveShell(incoming,next=="now"?new(0,distance,0):new(player?0:18,player?12:0,0),Vector3.Zero,0,1,next=="now"?380:180);
                    shellTransitions++;
                }
                ResetShell(incoming);
            }
        }catch(Exception e){Storage.Log(e);ResetShell(body);ResetShell(workspace);}
        finally{navigating=false;}
    }

    void HookShellDrag(Button handle,bool rising){
        double? origin=null;bool moved=false;float offset=0;
        UIElement MovingElement()=>rising?handle:body;
        handle.AddHandler(PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>{
            if(navigating||!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)return;
            origin=e.GetCurrentPoint(Root).Position.Y;moved=false;offset=0;
        }),true);
        handle.AddHandler(PointerMovedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>{
            if(origin is not double start||navigating)return;
            double delta=e.GetCurrentPoint(Root).Position.Y-start;
            double directed=rising?-delta:delta;if(directed<8&&!moved)return;
            moved=true;suppressShellClickUntil=Environment.TickCount64+700;offset=(float)(Math.Clamp(directed,0,rising?80:280)*(rising?-1:1));
            if(ShellMotionEnabled){ElementCompositionPreview.SetIsTranslationEnabled(MovingElement(),true);ElementCompositionPreview.GetElementVisual(MovingElement()).Properties.InsertVector3("Translation",new(0,offset,0));}
            e.Handled=true;
        }),true);
        handle.AddHandler(PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler(async(_,e)=>{
            if(origin==null)return;origin=null;if(!moved)return;
            suppressShellClickUntil=Environment.TickCount64+400;e.Handled=true;
            if(Math.Abs(offset)>=44){if(rising)ResetShell(handle);else shellDragOffset=ShellMotionEnabled?offset:0;await Navigate(rising?"now":lastHomePage);}
            else{if(ShellMotionEnabled)await MoveShell(MovingElement(),new(0,offset,0),Vector3.Zero,1,1,160);ResetShell(MovingElement());}
            moved=false;
        }),true);
        handle.PointerCaptureLost+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{if(origin!=null){origin=null;moved=false;ResetShell(MovingElement());}});
    }

    static void ResetShell(UIElement element){
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var visual=ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Translation");visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation",Vector3.Zero);visual.Opacity=1;
    }

    static async Task MoveShell(UIElement element,Vector3 from,Vector3 to,float opacityFrom,float opacityTo,int milliseconds){
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var visual=ElementCompositionPreview.GetElementVisual(element);var compositor=visual.Compositor;
        using var batch=compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var done=new TaskCompletionSource();batch.Completed+=(_,_)=>done.TrySetResult();
        using var easing=compositor.CreateCubicBezierEasingFunction(new(.2f,.8f),new(.2f,1));
        using var move=compositor.CreateVector3KeyFrameAnimation();move.Duration=TimeSpan.FromMilliseconds(milliseconds);
        move.InsertKeyFrame(0,from);move.InsertKeyFrame(1,to,easing);
        using var fade=compositor.CreateScalarKeyFrameAnimation();fade.Duration=move.Duration;
        fade.InsertKeyFrame(0,opacityFrom);fade.InsertKeyFrame(1,opacityTo,easing);
        visual.StartAnimation("Translation",move);visual.StartAnimation("Opacity",fade);batch.End();
        await done.Task;
    }

    async Task MagicTrail(bool rising){
        // Ten tiny vector stars, alive for one transition. No blur, textures, rendering timer or idle loop.
        var layer=new Canvas{IsHitTestVisible=false};magicTrail=layer;Root.Children.Add(layer);
        double width=ActualWidth,height=ActualHeight;
        layer.Clip=new RectangleGeometry{Rect=new Rect(0,0,width,height)};
        try{
            var compositor=ElementCompositionPreview.GetElementVisual(layer).Compositor;
            using var batch=compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            var done=new TaskCompletionSource();batch.Completed+=(_,_)=>done.TrySetResult();
            using var easing=compositor.CreateCubicBezierEasingFunction(new(.15f,.7f),new(.25f,1));
            for(int i=0;i<10;i++){
                double size=i%3==0?23:12;double x=width*(.06+i*.098);double y=rising?height*.79:height*.48;
                var star=new Polygon{Fill=new SolidColorBrush(i%2==0?Color.FromArgb(255,244,184,207):Color.FromArgb(255,255,238,215)),Points=new PointCollection{new(size/2,0),new(size*.63,size*.37),new(size,size/2),new(size*.63,size*.63),new(size/2,size),new(size*.37,size*.63),new(0,size/2),new(size*.37,size*.37)}};
                Canvas.SetLeft(star,x);Canvas.SetTop(star,y);layer.Children.Add(star);
                ElementCompositionPreview.SetIsTranslationEnabled(star,true);var v=ElementCompositionPreview.GetElementVisual(star);
                using var move=compositor.CreateVector3KeyFrameAnimation();move.Duration=TimeSpan.FromMilliseconds(700+i*14);
                move.InsertKeyFrame(0,Vector3.Zero);move.InsertKeyFrame(1,new Vector3((i-4)*5,(float)((rising?-1:1)*(80+(i%4)*23)),0),easing);
                using var fade=compositor.CreateScalarKeyFrameAnimation();fade.Duration=move.Duration;
                fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(.12f+i*.025f,.92f);fade.InsertKeyFrame(.52f,.68f);fade.InsertKeyFrame(1,0);
                v.StartAnimation("Translation",move);v.StartAnimation("Opacity",fade);
            }
            batch.End();await done.Task;
        }catch(Exception e){Storage.Log(e);}
        finally{Root.Children.Remove(layer);if(magicTrail==layer)magicTrail=null;}
    }
}
