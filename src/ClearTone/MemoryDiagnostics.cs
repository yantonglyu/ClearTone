using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace ClearTone;

public sealed partial class MainPage
{
    // Synthetic fixtures only; never reads the personal account or library.
    async Task MemoryFixture(){
        if(!testControl||!File.Exists(Path.Combine(Storage.Data,".cleartone-test")))return;
        homeAccount=accountId;homeCachedAt=DateTime.Now;
        homeRecommendations=Enumerable.Range(0,30).Select(i=>new PlaylistItem(i.ToString(),"推荐歌单 "+(i+1),new Uri(Path.Combine(Storage.Data,"covers",i+".png")).AbsoluteUri,"20 首")).ToList();
        await Navigate("discover");BuildCatalog(true);
    }
    static IEnumerable<DependencyObject> MemoryDescendants(DependencyObject root){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in MemoryDescendants(child))yield return nested;}
    }
    long LyricTextureBytes=>lyricTextures.Values.Sum(t=>(long)t.SizeInPixels.Width*t.SizeInPixels.Height*4);
    object MemorySnapshot()=>new {textureCount=lyricTextures.Count,textureBytes=LyricTextureBytes,
        coverCount=MemoryDescendants(workspace).OfType<Image>().Count(i=>i.Source!=null),
        scrollOffset=MemoryDescendants(workspace).OfType<ScrollViewer>().Select(s=>s.VerticalOffset).DefaultIfEmpty().Max(),
        managedBytes=GC.GetTotalMemory(false),workingBytes=System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,
        privateBytes=System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64,
        drawP95=lyricDrawTimes.Count==0?0:lyricDrawTimes.Order().ElementAt((int)((lyricDrawTimes.Count-1)*.95))};
}
