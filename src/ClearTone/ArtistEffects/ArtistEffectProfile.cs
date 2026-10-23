using System.Text.Json;

namespace ClearTone;

// Small data-only profiles. Artist matching never depends on the song title.
public sealed record ArtistEffectProfile
{
    public string Id { get; init; } = "";
    public string[] Artists { get; init; } = [];
    public string[] Colors { get; init; } = [];
    public int Stars { get; init; } = 18;
    public double Opacity { get; init; } = .13;
    public string Pattern { get; init; } = "stars";

    public static ArtistEffectProfile[] Defaults =>
    [
        new() { Id="poppin-party", Artists=["Poppin'Party", "Poppin’Party", "Poppin Party", "ポッピンパーティー"], Colors=["#F1B4C9", "#F4D79C", "#F7DCE6"], Opacity=.23, Pattern="pop-stage" },
        new() { Id="liella", Artists=["Liella!", "Liella！"], Colors=["#D2B9E9", "#EECBB6", "#F4DFAB"], Opacity=.22, Pattern="starlines" }
    ];

    public bool IsValid => Id.Length is >0 and <=80 && Artists.Length is >0 and <=32 &&
        Artists.All(a=>a.Length is >0 and <=160) && Colors.Length is >0 and <=8 &&
        Colors.All(c=>c.Length==7 && c[0]=='#' && uint.TryParse(c.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out _)) &&
        Stars is >=4 and <=32 && double.IsFinite(Opacity) && Opacity is >=.02 and <=.25 && Pattern is "stars" or "pop-stage" or "starlines";

    // Upgrade only the exact previously shipped style. A user's edited opacity,
    // palette, aliases or count is intentional and must not be overwritten.
    public static ArtistEffectProfile UpgradeBuiltIn(ArtistEffectProfile profile)
    {
        var current=Defaults.FirstOrDefault(p=>p.Id==profile.Id);
        return current!=null && profile.Pattern=="stars" && profile.Stars==18 && profile.Opacity==.13 &&
            profile.Artists.SequenceEqual(current.Artists,StringComparer.Ordinal) && profile.Colors.SequenceEqual(current.Colors,StringComparer.Ordinal)
            ? current : profile;
    }

    public static string Normalize(string artist) => new(artist.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public bool Matches(string artist)
    {
        var names=artist.Split(['/', ';', '；', '|', '、'],StringSplitOptions.RemoveEmptyEntries).Append(artist).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        return Artists.Any(a=>names.Contains(Normalize(a)));
    }

    public static ArtistEffectProfile[] Load(string directory,Action<Exception>? log=null)
    {
        var defaults=Defaults;
        try
        {
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,"profiles.json");
            if(!File.Exists(path))File.WriteAllText(path,JsonSerializer.Serialize(defaults,new JsonSerializerOptions{WriteIndented=true}));
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("歌手动效配置过大，继续使用默认样式。");
            var profiles=JsonSerializer.Deserialize<ArtistEffectProfile[]>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[];
            if(profiles.Length>100 || profiles.Any(p=>p is null || !p.IsValid))throw new InvalidDataException("歌手动效配置无效，继续使用默认样式。");
            return profiles.Select(UpgradeBuiltIn).ToArray();
        }
        catch(Exception error){log?.Invoke(error);return defaults;}
    }
}
