using ClearTone;
using System.Text.Json;
var profile=JsonSerializer.Deserialize<SongEggProfile>(File.ReadAllText(args[0]))!;
var track=new Track("Snow halation","μ's","28768036");
void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
var cue=profile.Resolve(track,[new(51.9,"届けて"),new(190.61,"届けて")],259.16)!;
Check(cue.Start==190.61,"last chorus anchor, not first chorus");
Check(profile.Resolve(track,[],259.16)!.Start==190.61,"verified fallback");
Check(profile.Resolve(track,[],90)==null,"short versions excluded");
Check(profile.Resolve(new("Snow halation","unrelated cover",""),[],259)==null,"unregistered covers excluded");
var clock=new SongEggClock();double wall=0;
EggFrame Step(double pos,bool playing=true,bool visible=true,bool reduced=false,EggCue? c=null)=>clock.Update(c??cue,pos,playing,visible,reduced,wall+=.1);
Step(190.5);var ignition=Step(190.65);Check(ignition.Burst>.9&&clock.Bursts==1,"natural crossing ignites once");
for(int i=0;i<50;i++)Step(191,false);Check(Step(191,false).Burst==0,"paused ignition settles instead of freezing bright");
var still=Step(191,false);Check(Step(191,false).Motion==still.Motion,"pause freezes particles");
clock.Seek();Step(100);clock.Seek();Check(Step(200).Burst==0,"seek across does not flash");
Step(187);Step(190);Step(190.7);Check(clock.Bursts==2,"rewind and replay rearm");
for(int i=0;i<100;i++)Step(259);Check(!Step(259).Active,"natural end fully releases overlay");
clock.Reset();Step(190.5);Step(190.7);Check(clock.Bursts==3,"new play session replays Easter egg");
for(int i=0;i<100;i++)clock.Update(null,0,false,true,false,wall+=.1);Check(!clock.Update(null,0,false,true,false,wall+=.1).Active,"switching songs cannot leave overlay");
clock.Reset();Step(190.5);Check(Step(190.7,true,true,true).Burst==0,"reduced motion avoids ignition");
clock.Reset();Step(190.5);Check(Step(190.7,true,false).Burst==0,"hidden window never flashes");
clock.Reset();Step(189.9);Step(190.12);Check(Step(190.3).Preheat>0,"half-beat preheat begins before the lyric");
for(int i=0;i<30;i++)Step(190.3,false);Check(Step(190.3,false).Preheat==0,"pause cannot leave the screen dark");
clock.Reset();Step(190.2);Step(190.3);clock.Seek();for(int i=0;i<30;i++)Step(80);Check(!Step(80).Active,"seek away releases preheat");
clock.Reset();Step(190.5);Step(190.7);var peakAge=Step(190.8);Check(peakAge.BurstAge>=0,"scan has monotonic wall-clock age");
for(int i=0;i<40;i++)Step(190.8,false);Check(Step(190.8,false).BurstAge>3,"pause advances past orange crest");
Console.WriteLine("19 timing and lifecycle checks passed");
namespace ClearTone {public record Track(string Name,string Artist,string NetEaseId){public string Key=>NetEaseId.Length>0?NetEaseId:Name;}public record Lyric(double Time,string Text);}
