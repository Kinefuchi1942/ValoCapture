namespace ValoCapture.Core.Game;

/// <summary>マップ ID・キュー ID の表示名。</summary>
public static class DisplayNames
{
    // マップ ID はコードネーム（/Game/Maps/Triad/Triad など）
    private static readonly Dictionary<string, string> Maps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ascent"] = "アセント",
        ["Bonsai"] = "スプリット",
        ["Canyon"] = "フラクチャー",
        ["Duality"] = "バインド",
        ["Foxtrot"] = "ブリーズ",
        ["Infinity"] = "アビス",
        ["Jam"] = "ロータス",
        ["Juliett"] = "サンセット",
        ["Pitt"] = "パール",
        ["Port"] = "アイスボックス",
        ["Triad"] = "ヘイヴン",
    };

    private static readonly Dictionary<string, string> Queues = new(StringComparer.OrdinalIgnoreCase)
    {
        [QueueIds.Competitive] = "コンペティティブ",
        [QueueIds.Unrated] = "アンレート",
        [QueueIds.Swiftplay] = "スイフトプレイ",
        [QueueIds.SpikeRush] = "スパイクラッシュ",
        [QueueIds.Deathmatch] = "デスマッチ",
        [QueueIds.TeamDeathmatch] = "チームデスマッチ",
        [QueueIds.Premier] = "プレミア",
    };

    public static string Map(string mapId)
    {
        var code = mapId.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return Maps.TryGetValue(code, out var name) ? name : code.Length > 0 ? code : "不明なマップ";
    }

    public static string Queue(string queueId) =>
        Queues.TryGetValue(queueId, out var name) ? name : queueId;
}
