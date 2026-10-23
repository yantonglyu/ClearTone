using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace ClearTone;

public sealed partial class MainPage
{
    static Button Primary(Button button){
        button.Background=new SolidColorBrush(Color.FromArgb(215,237,187,204));
        button.Foreground=new SolidColorBrush(Color.FromArgb(255,48,29,42));
        button.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;return button;
    }
    static Border ControlGroup(params UIElement[] controls){
        var row=Row(controls);row.Spacing=4;
        return new Border{Child=row,Padding=new(4),CornerRadius=new(13),Background=new SolidColorBrush(Color.FromArgb(13,255,255,255))};
    }
    static Button MoreActions(params Button[] buttons){
        var button=ActionButton("•••",()=>{});button.Content=new FontIcon{Glyph="\uE712",FontSize=18};button.Width=42;button.Padding=new(0);ToolTipService.SetToolTip(button,"更多操作");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,"更多操作");
        var content=new StackPanel{Spacing=4,MinWidth=180};
        foreach(var item in buttons){item.HorizontalAlignment=HorizontalAlignment.Stretch;item.HorizontalContentAlignment=HorizontalAlignment.Left;item.Background=new SolidColorBrush(Color.FromArgb(0,0,0,0));content.Children.Add(item);}
        var flyout=new Flyout{Content=content,Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight};
        foreach(var item in buttons)item.Click+=(_,_)=>flyout.Hide();button.Flyout=flyout;return button;
    }
    static UIElement LabeledControl(string label,Control control){
        control.Height=36;control.Width=double.NaN;control.HorizontalAlignment=HorizontalAlignment.Stretch;
        control.Background=new SolidColorBrush(Color.FromArgb(18,255,255,255));control.BorderThickness=new(0);
        var column=Column(Text(label,11,.55),control);column.Spacing=5;return column;
    }
    static Grid EqualColumns(params UIElement[] children){
        var grid=new Grid{ColumnSpacing=14};
        for(int i=0;i<children.Length;i++){grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.Children.Add(children[i]);Grid.SetColumn((FrameworkElement)children[i],i);}return grid;
    }
    static void DiscoveryCard(Button button,string glyph,string title,string description){
        button.Height=146;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new(20);button.CornerRadius=new(18);
        button.Background=new SolidColorBrush(Color.FromArgb(23,255,255,255));
        var icon=new FontIcon{Glyph=glyph,FontSize=24,HorizontalAlignment=HorizontalAlignment.Left,Foreground=new SolidColorBrush(Color.FromArgb(255,246,201,216)),Margin=new(0,0,0,14)};
        var label=Text(title,18);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        button.Content=Column(icon,label,Text(description,12,.55));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,title);
    }
}
