using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValoCapture.Core.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionStatus { Recording, FetchingDetails, Ready, Failed }

/// <summary>
/// 1 試合ぶんの録画セッション。セッションフォルダの session.json に保存する。
/// フォルダ構成: session.json / video.mp4 / match.json（試合詳細）/ timeline.json（イベント）
/// </summary>
public sealed class MatchSession
{
    public const string SessionFile = "session.json";
    public const string VideoFile = "video.mp4";
    public const string MatchFile = "match.json";
    public const string TimelineFile = "timeline.json";
    /// <summary>
    /// ブックマークの目印（中身は空）。録画中・イベント取得中は録画側が session.json を書き直すので、
    /// session.json には入れず別ファイルにする。
    /// </summary>
    public const string BookmarkFile = "bookmark";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [JsonIgnore] public string Directory { get; set; } = "";

    public string QueueId { get; set; } = "";
    public string MapId { get; set; } = "";
    public string? MatchId { get; set; }
    public string? Puuid { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? RecordStartedAt { get; set; }
    public DateTimeOffset? RecordStoppedAt { get; set; }
    /// <summary>動画とイベントの時刻ずれの手動補正（ms）。</summary>
    public long OffsetMs { get; set; }
    public SessionStatus Status { get; set; }
    public string? Error { get; set; }

    [JsonIgnore] public string VideoPath => Path.Combine(Directory, VideoFile);

    public void Save() =>
        File.WriteAllText(Path.Combine(Directory, SessionFile), JsonSerializer.Serialize(this, JsonOptions));

    public void SaveJson(string fileName, string json) =>
        File.WriteAllText(Path.Combine(Directory, fileName), json);

    public void SaveTimeline(MatchTimeline timeline) =>
        File.WriteAllText(Path.Combine(Directory, TimelineFile), JsonSerializer.Serialize(timeline, JsonOptions));

    public static MatchSession Load(string directory)
    {
        var session = JsonSerializer.Deserialize<MatchSession>(File.ReadAllText(Path.Combine(directory, SessionFile)))
            ?? throw new InvalidDataException("session.json が空です");
        session.Directory = directory;
        return session;
    }

    /// <summary>録画フォルダ配下のセッションを新しい順に読む。壊れたものは飛ばす。</summary>
    public static IReadOnlyList<MatchSession> LoadAll(string root)
    {
        if (!System.IO.Directory.Exists(root)) return [];

        var sessions = new List<MatchSession>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories(root))
        {
            if (!File.Exists(Path.Combine(dir, SessionFile))) continue;
            try { sessions.Add(Load(dir)); }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { }
        }
        return sessions.OrderByDescending(s => s.DetectedAt).ToList();
    }

    /// <summary>timeline.json。まだ作られていなければ null。古い形式なら match.json から作り直す。</summary>
    public MatchTimeline? LoadTimeline()
    {
        var path = Path.Combine(Directory, TimelineFile);
        if (!File.Exists(path)) return null;

        var timeline = JsonSerializer.Deserialize<MatchTimeline>(File.ReadAllText(path));
        if (timeline is null || timeline.Version >= MatchTimeline.CurrentVersion || !HasMatchDetails || Puuid is null)
            return timeline;

        try { return RebuildTimeline(OffsetMs); }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return timeline;
        }
    }

    /// <summary>補正値を変えて match.json からタイムラインを作り直し、保存する。</summary>
    public MatchTimeline RebuildTimeline(long offsetMs)
    {
        OffsetMs = offsetMs;
        var timeline = SessionOrchestrator.BuildTimeline(this, File.ReadAllText(Path.Combine(Directory, MatchFile)));
        SaveTimeline(timeline);
        Save();
        return timeline;
    }

    /// <summary>ブックマークした試合は容量制限による自動削除の対象にしない。</summary>
    [JsonIgnore] public bool IsBookmarked => File.Exists(Path.Combine(Directory, BookmarkFile));

    public void SetBookmarked(bool bookmarked)
    {
        var path = Path.Combine(Directory, BookmarkFile);
        if (bookmarked) File.WriteAllBytes(path, []);
        else File.Delete(path);
    }

    [JsonIgnore] public bool HasMatchDetails => File.Exists(Path.Combine(Directory, MatchFile));
}
