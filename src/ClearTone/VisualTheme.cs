using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage
{
    public const string AppFont="ms-appx:///Assets/Fonts/NotoSansSC.ttf#Noto Sans SC";
    static readonly FontFamily UiFont=new(AppFont);
    static readonly string CanvasFont=new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","Fonts","NotoSansSC.ttf")).AbsoluteUri+"#Noto Sans SC";
    static Button Choice(string label,IReadOnlyList<string> options,int selected,Action<int> changed){
        var text=Text(options[Math.Clamp(selected,0,options.Count-1)],12,.86);text.MaxWidth=260;text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;
        var button=ActionButton("",()=>{});button.Content=Row(text,new FontIcon{Glyph="\uE70D",FontSize=9,Opacity=.55});
        var menu=new MenuFlyout();
        for(int i=0;i<options.Count;i++){int index=i;var item=new MenuFlyoutItem{Text=options[i]};item.Click+=(_,_)=>{text.Text=options[index];Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label+"："+text.Text);changed(index);};menu.Items.Add(item);}
        button.Flyout=menu;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label+"："+text.Text);ToolTipService.SetToolTip(button,label);return button;
    }
    void ApplyVisualTheme(){
        FontFamily=UiFont;
        foreach(var key in new[]{"ControlContentThemeFontFamily","ContentControlThemeFontFamily"})Resources[key]=UiFont;
        foreach(var key in new[]{"TextControlBackground","TextControlBackgroundPointerOver","TextControlBackgroundFocused","ComboBoxBackground","ComboBoxBackgroundPointerOver","ComboBoxBackgroundPressed"})Resources[key]=new SolidColorBrush(Color.FromArgb(18,255,255,255));
        foreach(var key in new[]{"TextControlBorderBrush","TextControlBorderBrushPointerOver","TextControlBorderBrushFocused","ComboBoxBorderBrush","ComboBoxBorderBrushPointerOver","ComboBoxBorderBrushPressed"})Resources[key]=new SolidColorBrush(Colors.Transparent);
        foreach(var key in new[]{"TextControlElevationBorderBrush","TextControlElevationBorderBrushFocused"})Resources[key]=new SolidColorBrush(Colors.Transparent);
        Resources["TextControlCornerRadius"]=new CornerRadius(12);
        Resources["ControlCornerRadius"]=new CornerRadius(10);
        Resources["TextControlBorderThemeThickness"]=new Thickness(0);
        Resources["TextControlBorderThemeThicknessFocused"]=new Thickness(0);
        Resources["TextControlForegroundFocused"]=new SolidColorBrush(Color.FromArgb(255,255,245,238));
    }
    static TextBox SearchInput(string placeholder){
        var input=new TextBox{PlaceholderText=placeholder,FontFamily=UiFont,FontSize=13,MinHeight=42,Padding=new(14,9,14,9),CornerRadius=new(12),Background=new SolidColorBrush(Color.FromArgb(16,255,255,255)),BorderThickness=new(0),VerticalContentAlignment=VerticalAlignment.Center};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,placeholder);return input;
    }
}
