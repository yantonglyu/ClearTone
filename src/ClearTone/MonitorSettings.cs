using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace ClearTone;

public sealed partial class MainPage
{
    readonly MicrophoneMonitor microphoneMonitor=new();
    bool monitorEvents;
    Action? refreshMonitorSettings;
    UIElement MonitorSettings(){
        var body=new StackPanel{Spacing=16};
        var status=Text(microphoneMonitor.Active?"耳返已开启 · 所有页面生效":"已关闭",11,.6);
        if(!monitorEvents){monitorEvents=true;microphoneMonitor.Failed+=error=>DispatcherQueue.TryEnqueue(()=>{if(!disposed){refreshMonitorSettings?.Invoke();ShowError(error);}});}
        var inputs=MicrophoneMonitor.Inputs();
        int selected=inputs.ToList().FindIndex(d=>d.Id==state.MonitorInput);
        var toggle=AsyncButton(microphoneMonitor.Active?"关闭耳返":"开启耳返",async()=>{});
        async Task Change(bool enabled){
            toggle.IsEnabled=false;body.IsHitTestVisible=false;
            try{
                if(enabled&&state.ExclusiveOutput){
                    await audio.SelectOutput(state.OutputDeviceId,false);
                    state.ExclusiveOutput=false;Storage.Save(state);
                }
                microphoneMonitor.SetVolume(state.MonitorVolume);
                await microphoneMonitor.Configure(enabled,state.MonitorInput,state.OutputDeviceId);
                status.Text=enabled?"耳返已开启 · 所有页面生效":"已关闭";
            }catch(Exception e){status.Text="无法开启耳返，请检查麦克风权限与设备";ShowError(e);}
            finally{toggle.Content=microphoneMonitor.Active?"关闭耳返":"开启耳返";toggle.IsEnabled=true;body.IsHitTestVisible=true;}
        }
        refreshMonitorSettings=()=>{status.Text="耳返已停止，请重新开启";toggle.Content="开启耳返";};
        toggle.Click+=async(_,_)=>await Change(!microphoneMonitor.Active);
        body.Children.Add(toggle);
        body.Children.Add(Text("麦克风",12,.8));
        body.Children.Add(Choice("耳返麦克风",inputs.Select(d=>d.Name).ToArray(),Math.Max(0,selected),async i=>{state.MonitorInput=inputs[i].Id;Storage.Save(state);if(microphoneMonitor.Active)await Change(true);}));
        var slider=new Slider{Minimum=0,Maximum=1,StepFrequency=.01,Value=Math.Clamp(state.MonitorVolume,0,1),Height=30};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,"耳返音量");
        slider.ValueChanged+=(_,e)=>{state.MonitorVolume=e.NewValue;microphoneMonitor.SetVolume(e.NewValue);SavePreferencesSoon();};
        body.Children.Add(Column(Text("耳返音量",12,.8),slider));body.Children.Add(status);
        body.Children.Add(Text("请戴耳机使用，避免扬声器啸叫。\n声音送往当前音乐输出设备。开启时切到共享输出。\n仅实时监听，不保存或上传录音，重启后保持关闭。\n有线耳机延迟通常更低，蓝牙耳机可能有明显延迟。",11,.55));
        return body;
    }
}


