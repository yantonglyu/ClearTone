using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace ClearTone;

public sealed partial class MainPage
{
    void ApplyAudioProcessing()=>audio.ConfigureProcessing(new(state.DspBypass,state.PreampDb,state.PeakProtection,state.ReplayGainMode));
    UIElement AudioProcessingSettings(){
        var body=new StackPanel{Spacing=12,Visibility=Visibility.Collapsed,Margin=new(0,2,0,8)};
        var details=Text("",11,.7);
        void Refresh(){ApplyAudioProcessing();details.Text=audio.ProcessingDescription;SavePreferencesSoon();}
        body.Children.Add(SettingSwitch("音色处理旁路",state.DspBypass,value=>{state.DspBypass=value;Refresh();}));
        body.Children.Add(Text("旁路 EQ、预放大和 ReplayGain。仍保留音量、峰值保护与跟唱变速。",11,.6));
        var gainLabel=Text("预放大   "+state.PreampDb.ToString("+0.0;-0.0;0.0")+" dB",12,.8);
        var gain=new Slider{Minimum=-12,Maximum=12,StepFrequency=.5,Value=Math.Clamp(state.PreampDb,-12,12),Height=30};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(gain,"预放大分贝");
        gain.ValueChanged+=(_,e)=>{state.PreampDb=e.NewValue;gainLabel.Text="预放大   "+state.PreampDb.ToString("+0.0;-0.0;0.0")+" dB";Refresh();};body.Children.Add(Column(gainLabel,gain));
        body.Children.Add(Text("ReplayGain",12,.8));
        body.Children.Add(Choice("ReplayGain",["关闭","按歌曲均衡音量","保留专辑内相对音量"],state.ReplayGainMode=="track"?1:state.ReplayGainMode=="album"?2:0,i=>{state.ReplayGainMode=new[]{"off","track","album"}[i];Refresh();}));
        body.Children.Add(Text("读取本地 FLAC 的 ReplayGain 标签。没有标签时保持原音量，不进行后台响度扫描。",11,.6));
        body.Children.Add(SettingSwitch("采样峰值保护",state.PeakProtection,value=>{state.PeakProtection=value;Refresh();}));
        body.Children.Add(Text("限制超出满刻度的采样峰值。它不是响度压缩器，也不检测采样间真峰值。",11,.6));
        details.Text=audio.ProcessingDescription;body.Children.Add(details);
        var toggle=ActionButton("音量与处理",()=>body.Visibility=body.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible);toggle.HorizontalAlignment=HorizontalAlignment.Left;
        return Column(toggle,body);
    }
    UIElement AudioFormatSettings(){
        var details=Text("",11,.65);details.Visibility=Visibility.Collapsed;
        var toggle=ActionButton("查看音频通路",()=>{string dsd=current!=null&&DsdDecoder.IsDsd(current.Url)?"DSD → PCM 176.4 kHz 兼容播放\n":"";details.Text=dsd+audio.FormatDescription+"\n"+audio.OutputDescription+"\n共享输出可能由 Windows 重采样。独占或旁路均不代表已经验证 bit-perfect。";details.Visibility=details.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;});toggle.HorizontalAlignment=HorizontalAlignment.Left;return Column(toggle,details);
    }
}
