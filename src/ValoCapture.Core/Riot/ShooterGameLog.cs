using System.Globalization;
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

    // 行頭の時刻は UTC（ヘッダーの "Time Zone" はローカルのタイムゾーンを書いているだけ）
    [GeneratedRegex(@"^\[(?<time>\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}:\d{3})\]")]
    private static partial Regex LineTime();

    // ラウンドごとに購入フェーズの開始（server time 0）と戦闘の開始（server time 30 前後、1 ラウンド目は 45 前後）の 2 行が出る。
    // デスマッチには出ない
    [GeneratedRegex(@"Gameplay started at local time [\d.]+ \(server time (?<server>[\d.]+)\)")]
    private static partial Regex GameplayStarted();

    public static ShardInfo? TryRead(string? path = null)
    {
        var text = TryReadText(path ?? DefaultPath);
        if (text is null) return null;

        var glz = GlzUrl().Matches(text).LastOrDefault();
        if (glz is null) return null;

        var version = CiVersion().Matches(text).LastOrDefault()?.Groups["version"].Value;
        return new ShardInfo(glz.Groups["region"].Value, glz.Groups["shard"].Value, version);
    }

    /// <summary>VALORANT が書き込み中でも読めるように開く。読めなければ null。</summary>
    public static string? TryReadText(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// ログからラウンドの時刻に関係する行のうち、<paramref name="from"/>〜<paramref name="to"/> のものを取り出す。
    /// ゲームを起動するたびにログは上書きされるので、録画を止めたときにセッションフォルダへ残しておく。
    /// </summary>
    public static IReadOnlyList<string> ExtractRoundLines(string text, DateTimeOffset from, DateTimeOffset to) =>
        text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Contains("Gameplay started") || l.Contains("OnRoundEnded"))
            .Where(l => ParseTime(l) is { } t && t >= from && t <= to)
            .ToList();

    /// <summary>ラウンドの戦闘が始まった時刻（クライアントの時計）。</summary>
    public static IReadOnlyList<DateTimeOffset> ReadRoundStarts(IEnumerable<string> lines)
    {
        var starts = new List<DateTimeOffset>();
        foreach (var line in lines)
        {
            var m = GameplayStarted().Match(line);
            if (!m.Success || ParseTime(line) is not { } t) continue;
            // 購入フェーズの開始（server time 0）は除く
            if (double.Parse(m.Groups["server"].Value, CultureInfo.InvariantCulture) < 1) continue;
            starts.Add(t);
        }
        return starts;
    }

    private static DateTimeOffset? ParseTime(string line)
    {
        var m = LineTime().Match(line);
        return m.Success && DateTime.TryParseExact(m.Groups["time"].Value, "yyyy.MM.dd-HH.mm.ss:fff",
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? new DateTimeOffset(t, TimeSpan.Zero)
            : null;
    }
}
