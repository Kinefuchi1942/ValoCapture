using System.Text.RegularExpressions;

namespace ValoCapture.Core.Riot;

/// <summary>
/// VALORANT のクライアントログから region / shard / クライアントバージョンを読む。
/// </summary>
public sealed record ShardInfo(string Region, string Shard, string? ClientVersion);

public static partial class ShooterGameLog
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VALORANT", "Saved", "Logs", "ShooterGame.log");

    [GeneratedRegex(@"https://glz-(?<region>[a-z0-9]+)-1\.(?<shard>[a-z0-9]+)\.a\.pvp\.net")]
    private static partial Regex GlzUrl();

    [GeneratedRegex(@"CI server version: (?<version>\S+)")]
    private static partial Regex CiVersion();

    public static ShardInfo? TryRead(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;

        string text;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            text = reader.ReadToEnd();
        }
        catch (IOException) { return null; }

        var glz = GlzUrl().Matches(text).LastOrDefault();
        if (glz is null) return null;

        var version = CiVersion().Matches(text).LastOrDefault()?.Groups["version"].Value;
        return new ShardInfo(glz.Groups["region"].Value, glz.Groups["shard"].Value, version);
    }
}
