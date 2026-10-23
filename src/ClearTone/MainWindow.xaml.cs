using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ClearTone;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public bool IsVisibleForAnimation {get;private set;}=true;
    bool fullscreen;
    bool closing,closeReady;
    public void ToggleFullscreen(){fullscreen=!fullscreen;AppWindow.SetPresenter(fullscreen?Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen:Microsoft.UI.Windowing.AppWindowPresenterKind.Default);}
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"));

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
        Title=Environment.GetEnvironmentVariable("CLEARTONE_PREVIEW")=="1"?"澄音 · 播放器预览":"澄音 · ClearTone";
        AppTitleBar.Title=Title;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,960));
        Activated+=(_,e)=>{(RootFrame.Content as MainPage)?.VisibilityChanged(e.WindowActivationState!=WindowActivationState.Deactivated);};
        AppWindow.Changed+=(_,_)=>{IsVisibleForAnimation=AppWindow.IsVisible&&!(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p&&p.State==Microsoft.UI.Windowing.OverlappedPresenterState.Minimized);(RootFrame.Content as MainPage)?.RefreshVisibility();};
        AppWindow.Closing+=async(_,e)=>{if(closeReady)return;e.Cancel=true;if(closing)return;closing=true;try{if(RootFrame.Content is MainPage page)await page.PrepareClose();}finally{closeReady=true;Close();}};
        Closed+=(_,_)=>{(RootFrame.Content as MainPage)?.Close();};
    }
}
