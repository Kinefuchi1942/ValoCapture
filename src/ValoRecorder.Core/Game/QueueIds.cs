namespace ValoRecorder.Core.Game;

/// <summary>presence / 試合詳細に現れる queueId の値。</summary>
public static class QueueIds
{
    public const string Competitive = "competitive";
    public const string Unrated = "unrated";
    public const string Swiftplay = "swiftplay";
    public const string SpikeRush = "spikerush";
    public const string Deathmatch = "deathmatch";
    public const string TeamDeathmatch = "hurm";
    public const string Premier = "premier";

    /// <summary>"competitive,deathmatch" のようなカンマ区切りを集合にする。別名（ranked / dm / tdm）も受け付ける。</summary>
    public static IReadOnlySet<string> Parse(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(q => q.ToLowerInvariant() switch
            {
                "ranked" => Competitive,
                "dm" => Deathmatch,
                "tdm" => TeamDeathmatch,
                var other => other,
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>録画・記録の対象とするキュー。既定はランクマッチのみ。</summary>
public sealed class MatchTarget
{
    public static MatchTarget Default { get; } = new([QueueIds.Competitive]);

    public IReadOnlySet<string> Queues { get; }

    public MatchTarget(IEnumerable<string> queues) =>
        Queues = queues.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool Matches(GameSnapshot snapshot) =>
        snapshot.QueueId.Length > 0 && Queues.Contains(snapshot.QueueId);

    public override string ToString() => string.Join(",", Queues);
}
