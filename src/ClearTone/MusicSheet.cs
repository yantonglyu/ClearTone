using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
namespace ClearTone;

public sealed class MusicSheet
{
    readonly Grid root,veil=new();
    readonly Border panel=new();
    readonly TaskCompletionSource done=new();
    public string Title {get;}
    public event EventHandler? Closed;
    bool hidden;
    public MusicSheet(Grid host,string title,UIElement content){
        root=host;Title=title;
        veil.Background=new SolidColorBrush(Windows.UI.Color.FromArgb(90,5,9,15));
        var dismiss=new Button{HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0)};
        dismiss.Click+=(_,_)=>Hide();veil.Children.Add(dismiss);
        var body=new Grid{RowSpacing=20,Padding=new Thickness(28)};
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var heading=new Grid();heading.Children.Add(new TextBlock{Text=title,FontSize=25,FontWeight=new Windows.UI.Text.FontWeight{Weight=600},Foreground=new SolidColorBrush(Colors.White),VerticalAlignment=VerticalAlignment.Center});
        var close=new Button{Content="×",FontSize=22,Padding=new Thickness(0),Width=34,Height=34,HorizontalAlignment=HorizontalAlignment.Right};
        close.Loaded+=(_,_)=>close.Focus(FocusState.Programmatic);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close,"关闭");close.Click+=(_,_)=>Hide();heading.Children.Add(close);body.Children.Add(heading);
        var scroll=new ScrollViewer{Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetRow(scroll,1);body.Children.Add(scroll);
        panel.Child=body;panel.CornerRadius=new CornerRadius(22);panel.BorderThickness=new Thickness(1);
        panel.BorderBrush=new SolidColorBrush(Windows.UI.Color.FromArgb(28,255,255,255));
        panel.Background=new SolidColorBrush(Windows.UI.Color.FromArgb(247,32,38,48));
        panel.HorizontalAlignment=HorizontalAlignment.Center;panel.VerticalAlignment=VerticalAlignment.Center;
        panel.Resources[typeof(Button)]=XamlReader.Load("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Setter Property="Foreground" Value="#F5F3F8"/><Setter Property="FontSize" Value="13"/>
          <Setter Property="Padding" Value="14,9"/><Setter Property="Background" Value="#10FFFFFF"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="Surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}">
              <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                <VisualState x:Name="Normal"/><VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Surface.Background" Value="#25FFFFFF"/></VisualState.Setters></VisualState>
                <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Surface.Opacity" Value=".65"/></VisualState.Setters></VisualState>
                <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Surface.Opacity" Value=".35"/></VisualState.Setters></VisualState>
              </VisualStateGroup></VisualStateManager.VisualStateGroups>
              <ContentPresenter Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        veil.Children.Add(panel);veil.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape){Hide();e.Handled=true;}};
        root.SizeChanged+=Resize;Resize(null,null);
    }
    void Resize(object? sender,SizeChangedEventArgs? args){panel.MaxHeight=Math.Max(220,root.ActualHeight-48);panel.Width=Math.Max(280,Math.Min(600,root.ActualWidth-48));}
    public Task ShowAsync(){root.Children.Add(veil);panel.Opacity=0;var animation=new DoubleAnimation{From=0,To=1,Duration=new Duration(TimeSpan.FromMilliseconds(160))};Storyboard.SetTarget(animation,panel);Storyboard.SetTargetProperty(animation,"Opacity");var story=new Storyboard();story.Children.Add(animation);story.Begin();return done.Task;}
    public void Hide(){if(hidden)return;hidden=true;root.SizeChanged-=Resize;root.Children.Remove(veil);Closed?.Invoke(this,EventArgs.Empty);done.TrySetResult();}
}
