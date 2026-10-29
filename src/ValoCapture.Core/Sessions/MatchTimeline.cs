using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValoCapture.Core.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchEventType { RoundStart, Kill, Death, Assist }

/// <param name="GameTimeMs">試合開始（gameStartMillis）からの経過時間。</param>
/// <param name="VideoTimeMs">動画の先頭からの位置。録画範囲外なら null。</param>
/// <param name="KillerAgent">キルしたプレイヤーのエージェント ID（characterId）。</param>
/// <param name="VictimAgent">キルされたプレイヤーのエージェント ID（characterId）。</param>
public sealed record MatchEvent(
    MatchEventType Type,
    int Round,
    long GameTimeMs,
    long? VideoTimeMs,
    string? Killer = null,
    string? Victim = null,
    string? DamageItem = null,
    string? KillerAgent = null,
    string? VictimAgent = null);

public sealed record MatchTimeline(
    string MatchId,
    string QueueId,
    string MapId,
    long GameStartMillis,
    long GameLengthMillis,
    int MyKills,
    int MyDeaths,
    int MyAssists,
    IReadOnlyList<MatchEvent> Events,
    int Version = 0)
{
    /// <summary>timeline.json の形式。上げると、古い timeline.json は読み込み時に match.json から作り直される。</summary>
    /// <remarks>2: エージェント ID を追加</remarks>
    public const int CurrentVersion = 2;
}

/// <summary>
/// 試合詳細（pd match-details）の JSON から、自分に関係するイベントを動画上の時刻付きで取り出す。
/// </summary>
public static class MatchTimelineBuilder
{
    /// <param name="recordStartedAt">録画が始まった時刻。null なら VideoTimeMs はすべて null。</param>
    /// <param name="recordLength">録画の長さ。これを超えるイベントは録画範囲外とする。</param>
    /// <param name="offsetMs">手動補正値（動画上の位置にそのまま足す）。</param>
    public static MatchTimeline Build(
        string matchDetailsJson,
        string myPuuid,
        DateTimeOffset? recordStartedAt,
        TimeSpan? recordLength,
        long offsetMs = 0)
    {
        using var doc = JsonDocument.Parse(matchDetailsJson);
        var root = doc.RootElement;
        var info = root.GetProperty("matchInfo");
        var gameStart = info.GetProperty("gameStartMillis").GetInt64();

        long? ToVideoTime(long gameTime)
        {
            if (recordStartedAt is null) return null;
            var t = gameStart + gameTime - recordStartedAt.Value.ToUnixTimeMilliseconds() + offsetMs;
            if (t < 0) return null;
            if (recordLength is { } len && t > (long)len.TotalMilliseconds) return null;
            return t;
        }

        var agents = ReadAgents(root);
        string? AgentOf(string puuid) => agents.GetValueOrDefault(puuid);

        var kills = EnumerateKills(root).ToList();
        var events = new List<MatchEvent>();

        // ラウンド開始時刻 = そのラウンド内のキルの gameTime − roundTime
        foreach (var group in kills.GroupBy(k => k.Round))
        {
            var k = group.First();
            var start = k.GameTime - k.RoundTime;
            events.Add(new MatchEvent(MatchEventType.RoundStart, group.Key, start, ToVideoTime(start)));
        }

        int myKills = 0, myDeaths = 0, myAssists = 0;
        foreach (var k in kills)
        {
            MatchEventType? type =
                k.Killer == myPuuid ? MatchEventType.Kill
                : k.Victim == myPuuid ? MatchEventType.Death
                : k.Assistants.Contains(myPuuid) ? MatchEventType.Assist
                : null;
            if (type is null) continue;

            switch (type)
            {
                case MatchEventType.Kill: myKills++; break;
                case MatchEventType.Death: myDeaths++; break;
                case MatchEventType.Assist: myAssists++; break;
            }
            events.Add(new MatchEvent(type.Value, k.Round, k.GameTime, ToVideoTime(k.GameTime), k.Killer, k.Victim, k.DamageItem,
                AgentOf(k.Killer), AgentOf(k.Victim)));
        }

        return new MatchTimeline(
            info.GetProperty("matchId").GetString() ?? "",
            info.GetProperty("queueID").GetString() ?? "",
            info.GetProperty("mapId").GetString() ?? "",
            gameStart,
            info.GetProperty("gameLengthMillis").GetInt64(),
            myKills,
            myDeaths,
            myAssists,
            events.OrderBy(e => e.GameTimeMs).ThenBy(e => e.Type).ToList(),
            MatchTimeline.CurrentVersion);
    }

    /// <summary>players[] の puuid（subject）→ エージェント ID（characterId）。</summary>
    private static Dictionary<string, string> ReadAgents(JsonElement root)
    {
        var result = new Dictionary<string, string>();
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return result;
        foreach (var p in players.EnumerateArray())
        {
            if (p.TryGetProperty("subject", out var s) && s.GetString() is { } subject
                && p.TryGetProperty("characterId", out var c) && c.GetString() is { Length: > 0 } characterId)
                result[subject] = characterId;
        }
        return result;
    }

    private sealed record Kill(int Round, long GameTime, long RoundTime, string Killer, string Victim, string[] Assistants, string? DamageItem);

    // デスマッチ等はトップレベルの kills、ラウンド制のモードは roundResults[].playerStats[].kills に入っている
    private static IEnumerable<Kill> EnumerateKills(JsonElement root)
    {
        if (root.TryGetProperty("kills", out var top) && top.ValueKind == JsonValueKind.Array && top.GetArrayLength() > 0)
        {
            foreach (var k in top.EnumerateArray()) yield return ParseKill(k, null);
            yield break;
        }

        if (!root.TryGetProperty("roundResults", out var rounds) || rounds.ValueKind != JsonValueKind.Array) yield break;
        foreach (var round in rounds.EnumerateArray())
        {
            var roundNum = round.GetProperty("roundNum").GetInt32();
            if (!round.TryGetProperty("playerStats", out var stats) || stats.ValueKind != JsonValueKind.Array) continue;
            foreach (var ps in stats.EnumerateArray())
            {
                if (!ps.TryGetProperty("kills", out var ks) || ks.ValueKind != JsonValueKind.Array) continue;
                foreach (var k in ks.EnumerateArray()) yield return ParseKill(k, roundNum);
            }
        }
    }

    private static Kill ParseKill(JsonElement k, int? round)
    {
        var assistants = k.TryGetProperty("assistants", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : [];
        var damageItem = k.TryGetProperty("finishingDamage", out var fd) && fd.ValueKind == JsonValueKind.Object
            && fd.TryGetProperty("damageItem", out var di) ? di.GetString() : null;

        return new Kill(
            round ?? (k.TryGetProperty("round", out var r) ? r.GetInt32() : 0),
            k.GetProperty("gameTime").GetInt64(),
            k.TryGetProperty("roundTime", out var rt) ? rt.GetInt64() : 0,
            k.GetProperty("killer").GetString() ?? "",
            k.GetProperty("victim").GetString() ?? "",
            assistants,
            damageItem);
    }
}
