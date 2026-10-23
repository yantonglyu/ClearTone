using ClearTone;
using System.Text.Json;

static void Check(bool result,string message){if(!result)throw new Exception(message);}
var profiles=ArtistEffectProfile.Defaults;
Check(profiles.All(p=>p.IsValid),"Shipped profiles must be valid.");
var popipa=profiles[0];var liella=profiles[1];
Check(popipa.Pattern=="pop-stage"&&liella.Pattern=="starlines","Bands should have distinct compositions.");
var oldPopipa=popipa with{Opacity=.13,Pattern="stars"};
var upgraded=ArtistEffectProfile.UpgradeBuiltIn(oldPopipa);
Check(upgraded.Pattern==popipa.Pattern&&upgraded.Opacity==popipa.Opacity,"Exact shipped legacy profile should acquire stronger defaults.");
foreach(var custom in new[]{oldPopipa with{Opacity=.12},oldPopipa with{Stars=12},oldPopipa with{Colors=["#AABBCC"]},oldPopipa with{Artists=["Poppin'Party"]}})
    Check(ArtistEffectProfile.UpgradeBuiltIn(custom)==custom,"A user's customized profile must remain unchanged.");
foreach(var artist in new[]{"Poppin'Party","Poppin’ Party","POPPIN’PARTY","Poppin'Party / Roselia"})
    Check(popipa.Matches(artist),$"Provider punctuation/collaboration variant should match: {artist}");
Check(!popipa.Matches("Poppin'Party tribute band"),"Unrelated tribute artist must not trigger the effect.");
Check(liella.Matches("Liella！"),"Full-width punctuation must match.");
Check(!liella.Matches("μ's"),"Snow Halation artist must not inherit Liella stars.");
var temp=Path.Combine(Path.GetTempPath(),"cleartone-artist-tests-"+Guid.NewGuid().ToString("N"));
try
{
    var first=ArtistEffectProfile.Load(temp);
    Check(first.Length==2&&File.Exists(Path.Combine(temp,"profiles.json")),"First launch must seed editable profiles.");
    var path=Path.Combine(temp,"profiles.json");
    var legacyJson=JsonSerializer.Serialize(new[]{oldPopipa});File.WriteAllText(path,legacyJson);
    Check(ArtistEffectProfile.Load(temp)[0].Pattern=="pop-stage","An installed old default should use the new composition.");
    Check(File.ReadAllText(path)==legacyJson,"Migration is in memory and must not overwrite the user's existing file.");
    File.WriteAllText(path,"[]");Check(ArtistEffectProfile.Load(temp).Length==0,"Empty configuration must disable all artist effects.");
    int failures=0;
    foreach(var invalid in new[]{"{not json}","[null]",JsonSerializer.Serialize(new[]{popipa with{Stars=900}}),JsonSerializer.Serialize(new[]{popipa with{Colors=["#broken"]}})})
    {
        File.WriteAllText(path,invalid);
        Check(ArtistEffectProfile.Load(temp,_=>failures++).Length==2,"Invalid optional config must fall back without crashing.");
    }
    Check(failures==4,"Each bad optional config must report one error.");
    File.WriteAllText(path,JsonSerializer.Serialize(new[]{popipa with{Id="custom",Artists=["My Artist"],Stars=8,Opacity=.09}}));
    var custom=ArtistEffectProfile.Load(temp);Check(custom.Length==1&&custom[0].Matches("MY ARTIST"),"Valid local profile must load.");
    Console.WriteLine("Artist effects: aliases, matching, distinct stages, legacy migration, custom preservation, editable profiles, disable, malformed config and resource bounds passed.");
}
finally
{
    var resolved=Path.GetFullPath(temp);var allowed=Path.GetFullPath(Path.GetTempPath());
    if(resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("cleartone-artist-tests-"))Directory.Delete(resolved,true);
}
