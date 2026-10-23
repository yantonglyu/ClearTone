using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System.Numerics;
using Windows.Foundation;
namespace ClearTone;

public sealed partial class MainPage {
    static float EggEase(float x){x=Math.Clamp(x,0,1);return x*x*(3-2*x);}
    void DrawSongEgg(CanvasControl sender,CanvasDrawEventArgs args){
        var ds=args.DrawingSession;float w=(float)sender.ActualWidth,h=(float)sender.ActualHeight;
        if(w<=0||h<=0||eggGlow==null)return;
        float a=(float)eggFrame.Opacity,t=(float)eggFrame.Motion,pre=(float)eggFrame.Preheat;
        bool reduced=!motionSettings.AnimationsEnabled;
        // A half-beat of darkness creates room for the audience's coordinated ignition.
        if(pre>0)ds.FillRectangle(0,0,w,h,EggColor(.7*EggEase(pre),5,3,9));
        ds.FillRectangle(0,0,w,h,EggColor(.105*a,255,131,26));
        eggGlow.Center=new(w*.5f,h*.72f);eggGlow.RadiusX=w*.8f;eggGlow.RadiusY=h;eggGlow.Opacity=.3f*a;
        ds.FillRectangle(0,0,w,h,eggGlow);
        if(!reduced&&eggFrame.BurstAge is >=0 and <2.8)DrawOrangeIgnition(ds,w,h,(float)eggFrame.BurstAge);
        if(reduced)return;
        // Sharp crystalline silhouettes, three light depth layers and a very slow tumble.
        for(int i=0;i<54;i++){
            int depth=i%3;
            float phase=(i*.6180339f+t*(.011f+depth*.005f))%1;
            float x=w*((i*.7548776f)%1)+MathF.Sin(t*.19f+i*2.4f)*(14+depth*8);
            float y=h*(1.12f-phase*1.25f),radius=(depth==0?5:depth==1?10:17)+(i*7%8);
            float edge=Math.Clamp(Math.Min(y+radius,h-y+radius)/70,0,1);
            float alpha=a*edge*(depth==0?.3f:depth==1?.57f:.8f);
            var transform=ds.Transform;
            ds.Transform=Matrix3x2.CreateScale(.8f+.2f*MathF.Sin(t*.33f+i),1,new(x,y))*Matrix3x2.CreateRotation(t*(i%2==0?.08f:-.065f)+i,new(x,y));
            if(eggSnowSprites.Count==3)ds.DrawImage(eggSnowSprites[depth],new Rect(x-radius*1.35f,y-radius*1.35f,radius*2.7f,radius*2.7f),new Rect(0,0,128,128),alpha);
            ds.Transform=transform;
            if(i%3==0)ds.FillCircle(x+radius*1.8f,y-radius*2,1,EggColor(alpha*.65,255,229,169));
        }
    }
    void DrawOrangeIgnition(CanvasDrawingSession ds,float w,float h,float age){
        if(eggGlow==null)return;
        var center=new Vector2(w*.5f,h*.53f);
        float diagonal=Vector2.Distance(center,new(w,h));
        float release=1-EggEase((age-.85f)/1.5f);
        // A dense, staggered field is illuminated by distance, not an expanding outline.
        // Subtle irregularity keeps it like thousands of hands instead of a perfect grid.
        for(int row=0;row<22;row++)for(int col=0;col<40;col++){
            int i=row*40+col;
            float x=(col+.5f+.22f*MathF.Sin(i*2.71f))*w/40;
            float y=(row+.5f+.23f*MathF.Cos(i*1.31f))*h/22;
            float distance=Vector2.Distance(center,new(x,y))/diagonal;
            float arrival=distance*.68f+.025f*MathF.Sin(i*1.7f),local=age-arrival;
            if(local<0)continue;
            float glow=EggEase(local/.055f)*(.24f+.76f*MathF.Exp(-local*4))*release;
            float len=(5+i%6)*Math.Clamp(h/720,.8f,1.6f);
            float lean=MathF.Sin(i*2.3f)*len*.3f;
            ds.DrawLine(x,y,x+lean,y-len,EggColor(glow*.12,255,128,12),15);
            ds.DrawLine(x,y,x+lean,y-len,EggColor(glow*.38,255,166,37),5);
            ds.DrawLine(x,y,x+lean,y-len,EggColor(glow*.95,255,239,174),1.5f);
        }
        // Volumetric shafts break the leading edge into a luminous, irregular fan.
        float wave=EggEase(age/.82f);
        for(int i=0;i<38;i++){
            float angle=i*2.399963f,reach=diagonal*wave*(.55f+.45f*((i*.618f)%1));
            var direction=new Vector2(MathF.Cos(angle),MathF.Sin(angle));
            float strength=MathF.Sin(Math.Clamp(age/.85f,0,1)*MathF.PI)*release;
            var from=center+direction*reach*.25f;var to=center+direction*reach;
            ds.DrawLine(from,to,EggColor(strength*.035,255,169,59),18+i%5*4);
            ds.DrawLine(from,to,EggColor(strength*.11,255,217,131),1.2f);
        }
        eggGlow.Center=center;eggGlow.RadiusX=Math.Max(25,w*wave);eggGlow.RadiusY=Math.Max(25,h*wave);eggGlow.Opacity=.72f*release;
        ds.FillRectangle(0,0,w,h,eggGlow);
        // A brief, deliberate full-screen orange crest after the scan reaches the edges.
        float crest=EggEase((age-.62f)/.16f)*(1-EggEase((age-.94f)/.65f));
        ds.FillRectangle(0,0,w,h,EggColor(.96*crest,255,155,24));
        float afterglow=EggEase((age-.7f)/.3f)*(1-EggEase((age-1.05f)/1.65f));
        ds.FillRectangle(0,0,w,h,EggColor(.24*afterglow,255,172,49));
        // Fine embers carry the release outwards; never a hard geometric shock ring.
        for(int i=0;i<65;i++){
            float angle=i*2.399963f,r=diagonal*(.05f+MathF.Min(age,1.8f)*(.22f+(i%9)*.045f));
            var d=new Vector2(MathF.Cos(angle),MathF.Sin(angle));var p=center+d*r;
            ds.DrawLine(p,p-d*(3+12*release),EggColor(.6*release,255,225,151),i%4==0?2:1);
        }
    }
    void BuildEggSnowSprites(){
        foreach(var image in eggSnowSprites)image.Dispose();eggSnowSprites.Clear();
        for(int depth=0;depth<3;depth++){
            using var raw=new CanvasRenderTarget(eggCanvas,128,128,96);
            using(var ds=raw.CreateDrawingSession()){
                ds.Clear(Microsoft.UI.Colors.Transparent);
                for(int arm=0;arm<6;arm++){
                    float angle=arm*MathF.PI/3-MathF.PI/2;var direction=new Vector2(MathF.Cos(angle),MathF.Sin(angle));var side=new Vector2(-direction.Y,direction.X);var c=new Vector2(64,64);
                    void CrystalLine(Vector2 from,Vector2 to,float width){ds.DrawLine(from,to,EggColor(.13,255,160,35),width+4);ds.DrawLine(from,to,EggColor(.92,255,211,115),width);ds.DrawLine(from,to,EggColor(.8,255,246,203),width*.42f);}
                    CrystalLine(c,c+direction*44,1.7f);
                    for(int branch=0;branch<3;branch++){
                        float radius=18+branch*9,length=12-branch*2;
                        var joint=c+direction*radius;
                        CrystalLine(joint,joint-direction*length*.6f+side*length,1.25f);
                        CrystalLine(joint,joint-direction*length*.6f-side*length,1.25f);
                    }
                    if(depth==2){var tip=c+direction*44;CrystalLine(tip,tip-direction*6+side*3,1);CrystalLine(tip,tip-direction*6-side*3,1);}
                }
                ds.FillCircle(64,64,2,EggColor(.85,255,243,188));
            }
            var sprite=new CanvasRenderTarget(eggCanvas,128,128,96);
            using(var ds=sprite.CreateDrawingSession()){ds.Clear(Microsoft.UI.Colors.Transparent);using var soft=new GaussianBlurEffect{Source=raw,BlurAmount=depth==0?.7f:.15f};ds.DrawImage(soft);}
            eggSnowSprites.Add(sprite);
        }
    }
}
