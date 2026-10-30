using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValoCapture.Core.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchEventType { RoundStart, Kill, Death, Assist }

/// <param name="GameTimeMs">試合開始（gameStartMillis）からの経過時間。</param>
/// <param name="VideoTimeMs">動画の先頭からの位置。録画範囲外なら null。</param>
public sealed record MatchEvent(
    MatchEventType Type,
    int Round,
    long GameTimeMs,
    long? VideoTimeMs,
    string? Killer = null,
    string? Victim = null,
    string? DamageItem = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchOutcome { Win, Loss, Draw }

/// <param name="MyScore">自チームの取得ラウンド数（デスマッチ等はポイント）。</param>
/// <param name="OpponentScore">相手チーム（複数なら最多）の取得ラウンド数・ポイント。</param>
public sealed record MatchResult(MatchOutcome Outcome, int MyScore, int OpponentScore);

/// <param name="Result">勝敗。この項目を追加する前に作った timeline.json や、判定できない試合では null。</param>
/// <param name="GameTimeOrigin">gameTime の 0 とみなした時刻（Unix ms）。</param>
/// <param name="Version">作成したときの <see cref="MatchTimelineBuilder.CurrentVersion"/>。古ければ作り直す。</param>
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
    MatchResult? Result = null,
    long GameTimeOrigin = 0,
    int Version = 0);

/// <summary>
/// 試合詳細（pd match-details）の JSON から、自分に関係するイベントを動画上の時刻付きで取り出す。
/// </summary>
public static class MatchTimelineBuilder
{
    /// <summary>
    /// 動画上の時刻の計算方法の版。1: gameTime の基準を gameStartMillis から参加開始時刻に変更。
    /// これより古い timeline.json は <see cref="MatchSession.LoadTimeline"/> で作り直す。
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// gameTime の 0 は、最初のプレイヤーがゲームサーバーに参加した時刻（participationPeriods の sessionStartTime）の約 4.7 秒前。
    /// デスマッチでは gameStartMillis とほぼ同じ（2026-10-28 の 3 試合で 4.4〜4.9 秒差）だが、
    /// コンペティティブの gameStartMillis はエージェント選択の開始時刻で、gameTime の 0 より約 105 秒早い。
    /// </summary>
    public const long JoinLeadMs = 4_700;

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
        var origin = FirstJoinMillis(root) is { } join ? join - JoinLeadMs : gameStart;

        long? ToVideoTime(long gameTime)
        {
            if (recordStartedAt is null) return null;
            var t = origin + gameTime - recordStartedAt.Value.ToUnixTimeMilliseconds() + offsetMs;
            if (t < 0) return null;
            if (recordLength is { } len && t > (long)len.TotalMilliseconds) return null;
            return t;
        }

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
            events.Add(new MatchEvent(type.Value, k.Round, k.GameTime, ToVideoTime(k.GameTime), k.Killer, k.Victim, k.DamageItem));
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
            ReadResult(root, myPuuid),
            origin,
            CurrentVersion);
    }

    // 全プレイヤーの participationPeriods[].sessionStartTime の最小値（自分の参加が遅れたり再接続したりしても影響しない）
    private static long? FirstJoinMillis(JsonElement root)
    {
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return null;
        long? first = null;
        foreach (var p in players.EnumerateArray())
        {
            if (!p.TryGetProperty("participationPeriods", out var periods) || periods.ValueKind != JsonValueKind.Array) continue;
            foreach (var period in periods.EnumerateArray())
            {
                if (period.TryGetProperty("sessionStartTime", out var st) && st.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(st.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                {
                    var ms = at.ToUnixTimeMilliseconds();
                    if (first is null || ms < first) first = ms;
                }
            }
        }
        return first;
    }

    /// <summary>試合詳細の JSON から自分のチームの勝敗を読む。判定できなければ null。</summary>
    public static MatchResult? ReadResult(string matchDetailsJson, string myPuuid)
    {
        using var doc = JsonDocument.Parse(matchDetailsJson);
        return ReadResult(doc.RootElement, myPuuid);
    }

    // players[].teamId で自分のチームを特定し、teams[] の won とスコアを見る。
    // ラウンド制は numPoints = roundsWon。デスマッチは teams がプレイヤーごと（teamId = puuid）で、
    // numPoints がキル数、roundsWon は勝者だけ 1 なので numPoints を使う
    private static MatchResult? ReadResult(JsonElement root, string myPuuid)
    {
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return null;
        if (!root.TryGetProperty("teams", out var teams) || teams.ValueKind != JsonValueKind.Array) return null;

        var myTeamId = players.EnumerateArray()
            .Where(p => p.TryGetProperty("subject", out var s) && s.GetString() == myPuuid)
            .Select(p => p.TryGetProperty("teamId", out var t) ? t.GetString() : null)
            .FirstOrDefault();
        if (myTeamId is null) return null;

        var list = teams.EnumerateArray().Select(t => (
            Id: t.TryGetProperty("teamId", out var id) ? id.GetString() : null,
            Won: t.TryGetProperty("won", out var w) && w.ValueKind == JsonValueKind.True,
            Rounds: t.TryGetProperty("roundsWon", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0,
            Points: t.TryGetProperty("numPoints", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0)).ToList();

        var mine = list.FirstOrDefault(t => t.Id == myTeamId);
        if (mine.Id is null) return null;
        var others = list.Where(t => t.Id != myTeamId).ToList();

        var usePoints = list.Any(t => t.Points > 0);
        int Score((string? Id, bool Won, int Rounds, int Points) t) => usePoints ? t.Points : t.Rounds;

        var outcome = mine.Won ? MatchOutcome.Win
            : others.Any(t => t.Won) ? MatchOutcome.Loss
            : MatchOutcome.Draw;
        return new MatchResult(outcome, Score(mine), others.Count > 0 ? others.Max(Score) : 0);
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
