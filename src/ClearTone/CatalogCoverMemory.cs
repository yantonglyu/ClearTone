using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
namespace ClearTone;

public sealed partial class MainPage
{
    static Image DeferredCatalogCover(string cover,double size){
        var image=new Image{Width=size,Height=size,Stretch=Stretch.UniformToFill};
        if(!Uri.TryCreate(cover,UriKind.Absolute,out var uri))return image;
        // Match the existing decode resolution, independent of later tile resizing.
        int pixels=(int)Math.Clamp(size*2,120,512);
        image.EffectiveViewportChanged+=(_,args)=>{
            var view=args.EffectiveViewport;
            // Preload about two rows. Hysteresis prevents reloads at the boundary.
            double margin=image.Source==null?480:640;
            bool nearby=!view.IsEmpty&&view.Width>0&&view.Height>0&&view.Bottom>=-margin&&view.Top<=image.ActualHeight+margin;
            if(nearby&&image.Source==null)image.Source=new BitmapImage(uri){DecodePixelWidth=pixels};
            else if(!nearby&&image.Source!=null)image.Source=null;
        };
        image.Unloaded+=(_,_)=>image.Source=null;
        return image;
    }
}
