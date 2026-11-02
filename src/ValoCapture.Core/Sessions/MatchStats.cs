using System.Text.Json;
using ValoCapture.Core.Riot;

namespace ValoCapture.Core.Sessions;

/// <summary>スコアボードの 1 行（1 人ぶんの試合の成績）。</summary>
/// <param name="Rounds">プレイしたラウンド数（ACS・ADR・KAST の分母）。</param>
/// <param name="Score">戦闘スコアの合計（ACS = Score / Rounds）。</param>
/// <param name="Damage">相手に与えたダメージの合計。</param>
/// <param name="KastRounds">キル・アシスト・生存・トレードのいずれかがあったラウンド数。</param>
/// <param name="FirstKills">ラウンドの最初のキルを取った回数。</param>
/// <param name="FirstDeaths">ラウンドの最初に倒された回数。</param>
/// <param name="MultiKills">1 ラウンドのキル数（2 以上）→ そのラウンドの数。</param>
public sealed record PlayerStats(
    string Puuid,
    string TeamId,
    string? AgentId,
    bool IsMe,
    bool IsAlly,
    bool IsTeamMvp,
    int Rounds,
    int Score,
    int Kills,
    int Deaths,
    int Assists,
    int Damage,
    int Headshots,
    int Bodyshots,
    int Legshots,
    int KastRounds,
    int FirstKills,
    int FirstDeaths,
    IReadOnlyDictionary<int, int> MultiKills)
{
    public int Acs => Rounds > 0 ? (int)Math.Round((double)Score / Rounds, MidpointRounding.AwayFromZero) : 0;
    public int Adr => Rounds > 0 ? (int)Math.Round((double)Damage / Rounds, MidpointRounding.AwayFromZero) : 0;

    /// <summary>当たった弾のうち頭の割合。ダメージの記録がなければ null。</summary>
    public int? HeadshotPercent => Headshots + Bodyshots + Legshots is var n && n > 0 ? (int)Math.Round(100.0 * Headshots / n, MidpointRounding.AwayFromZero) : null;

    public int KastPercent => Rounds > 0 ? (int)Math.Round(100.0 * KastRounds / Rounds, MidpointRounding.AwayFromZero) : 0;
}

public enum RoundEventType { Kill, Death, Plant, Defuse, Detonate }

/// <param name="RoundTimeMs">戦闘フェーズの開始からの経過時間。</param>
/// <param name="VideoTimeMs">動画の先頭からの位置。録画範囲外・不明なら null。</param>
/// <param name="AgentId">キルなら倒した相手、デスなら倒された相手、設置・解除ならした人のエージェント ID。</param>
/// <param name="DamageItem">キル・デスの武器 ID（アビリティならスロット名）。</param>
public sealed record RoundEvent(RoundEventType Type, long RoundTimeMs, long? VideoTimeMs, string? AgentId = null, string? DamageItem = null);

/// <summary>1 ラウンドで相手 1 人に与えたダメージ。</summary>
public sealed record DamageDealt(string? AgentId, int Damage, int Headshots, int Bodyshots, int Legshots);

/// <param name="Number">ラウンド番号（0 始まり）。</param>
/// <param name="ResultCode">決着の仕方（Elimination / Detonate / Defuse / Surrendered など）。</param>
/// <param name="Ceremony">CeremonyAce / CeremonyClutch / CeremonyFlawless などの演出。なければ CeremonyDefault か空。</param>
/// <param name="Attacking">自分が攻め側だったか。分からなければ null。</param>
/// <param name="MyScore">このラウンドが終わった時点の自チームの取得ラウンド数。</param>
/// <param name="LoadoutValue">購入フェーズが終わった時点の装備の価値。</param>
/// <param name="StartVideoMs">戦闘フェーズの開始の動画上の位置。キルがない・録画範囲外なら null。</param>
public sealed record RoundStats(
    int Number,
    bool Won,
    string ResultCode,
    string Ceremony,
    bool? Attacking,
    int MyScore,
    int OpponentScore,
    int Kills,
    bool Died,
    int Damage,
    int LoadoutValue,
    int Spent,
    int Remaining,
    string? Weapon,
    string? Armor,
    string? PlantSite,
    long? StartVideoMs,
    IReadOnlyList<DamageDealt> DamageDealt,
    IReadOnlyList<RoundEvent> Events);

public sealed record MatchStats(IReadOnlyList<PlayerStats> Players, IReadOnlyList<RoundStats> Rounds)
{
    public PlayerStats? Me => Players.FirstOrDefault(p => p.IsMe);
}

/// <summary>
/// 試合詳細（pd match-details）の JSON から、スコアボードとラウンドごとの成績を求める。
/// </summary>
public static class MatchStatsBuilder
{
    /// <summary>倒されてからこの時間内に、倒した相手が味方に倒されたらトレードとみなす（KAST の T）。</summary>
    public const long TradeWindowMs = 5_000;

    /// <summary>スパイクの設置から爆発まで。</summary>
    public const long SpikeTimerMs = 45_000;

    /// <param name="recordStartedAt">録画が始まった時刻。null なら動画上の位置はすべて null。</param>
    /// <param name="recordLength">録画の長さ。これを超える位置は録画範囲外とする。</param>
    /// <param name="offsetMs">時刻補正（<see cref="MatchTimelineBuilder.Build"/> と同じ値）。</param>
    public static MatchStats Build(
        string matchDetailsJson,
        string myPuuid,
        DateTimeOffset? recordStartedAt,
        TimeSpan? recordLength,
        long offsetMs = 0)
    {
        using var doc = JsonDocument.Parse(matchDetailsJson);
        var root = doc.RootElement;
        var origin = MatchTimelineBuilder.Origin(root);
        long? ToVideoTime(long gameTime) => MatchTimelineBuilder.VideoTime(origin, gameTime, recordStartedAt, recordLength, offsetMs);

        var agents = MatchTimelineBuilder.ReadAgents(root);
        var teamOf = ReadTeams(root);
        var myTeam = teamOf.GetValueOrDefault(myPuuid);
        bool AreAllies(string a, string b) => teamOf.TryGetValue(a, out var ta) && teamOf.TryGetValue(b, out var tb) && ta == tb;

        var kills = MatchTimelineBuilder.EnumerateKills(root).ToList();
        var killsByRound = kills.GroupBy(k => k.Round).ToDictionary(g => g.Key, g => g.OrderBy(k => k.GameTime).ToList());
        var roundStarts = MatchTimelineBuilder.RoundStarts(kills).ToDictionary(r => r.Round, r => r.Start);
        var roundResults = root.TryGetProperty("roundResults", out var rr) && rr.ValueKind == JsonValueKind.Array
            ? rr.EnumerateArray().ToList()
            : [];

        // ---- ラウンドごとの成績（全員ぶん集計してスコアボードに使い、自分のぶんはラウンドの流れに使う） ----
        var acc = new Dictionary<string, Accumulator>();
        Accumulator Acc(string puuid) => acc.TryGetValue(puuid, out var a) ? a : acc[puuid] = new Accumulator();

        var rounds = new List<RoundStats>();
        int myScore = 0, opponentScore = 0;
        foreach (var round in roundResults)
        {
            var number = round.GetInt("roundNum");
            var roundKills = killsByRound.GetValueOrDefault(number) ?? [];
            var players = round.TryGetProperty("playerStats", out var ps) && ps.ValueKind == JsonValueKind.Array
                ? ps.EnumerateArray().ToList()
                : [];

            foreach (var p in players)
            {
                var subject = p.GetStringOrEmpty("subject");
                if (subject.Length == 0) continue;
                var a = Acc(subject);
                if (p.TryGetProperty("damage", out var damage) && damage.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in damage.EnumerateArray())
                    {
                        if (AreAllies(subject, d.GetStringOrEmpty("receiver"))) continue;
                        a.Damage += d.GetInt("damage");
                        a.Headshots += d.GetInt("headshots");
                        a.Bodyshots += d.GetInt("bodyshots");
                        a.Legshots += d.GetInt("legshots");
                    }
                }
                if (IsKast(subject, roundKills)) a.KastRounds++;
                var roundKillCount = roundKills.Count(k => k.Killer == subject && !AreAllies(k.Killer, k.Victim));
                if (roundKillCount >= 2) a.MultiKills[roundKillCount] = a.MultiKills.GetValueOrDefault(roundKillCount) + 1;
            }
            if (roundKills.Count > 0)
            {
                Acc(roundKills[0].Killer).FirstKills++;
                Acc(roundKills[0].Victim).FirstDeaths++;
            }

            var winner = round.GetStringOrEmpty("winningTeam");
            var won = myTeam is not null && winner == myTeam;
            if (won) myScore++; else if (winner.Length > 0) opponentScore++;
            var winnerRole = round.GetStringOrEmpty("winningTeamRole");
            bool? attacking = winnerRole switch
            {
                "Attacker" => won,
                "Defender" => !won,
                _ => null,
            };

            var mine = players.FirstOrDefault(p => p.GetStringOrEmpty("subject") == myPuuid);
            var economy = mine.ValueKind == JsonValueKind.Object && mine.TryGetProperty("economy", out var e) && e.ValueKind == JsonValueKind.Object
                ? e
                : default;
            var dealt = new List<DamageDealt>();
            if (mine.ValueKind == JsonValueKind.Object && mine.TryGetProperty("damage", out var myDamage) && myDamage.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in myDamage.EnumerateArray())
                {
                    var receiver = d.GetStringOrEmpty("receiver");
                    if (AreAllies(myPuuid, receiver)) continue;
                    dealt.Add(new DamageDealt(agents.GetValueOrDefault(receiver), d.GetInt("damage"),
                        d.GetInt("headshots"), d.GetInt("bodyshots"), d.GetInt("legshots")));
                }
            }

            long? start = roundStarts.TryGetValue(number, out var startGameTime) ? startGameTime : null;
            var plantTime = round.GetInt("plantRoundTime");
            var defuseTime = round.GetInt("defuseRoundTime");
            var resultCode = round.GetStringOrEmpty("roundResultCode");
            long? AtRoundTime(long roundTime) => start is { } t ? ToVideoTime(t + roundTime) : null;

            var events = new List<RoundEvent>();
            foreach (var k in roundKills)
            {
                if (k.Killer == myPuuid && k.Victim != myPuuid)
                    events.Add(new RoundEvent(RoundEventType.Kill, k.RoundTime, ToVideoTime(k.GameTime), agents.GetValueOrDefault(k.Victim), k.DamageItem));
                else if (k.Victim == myPuuid)
                    events.Add(new RoundEvent(RoundEventType.Death, k.RoundTime, ToVideoTime(k.GameTime),
                        k.Killer == myPuuid ? null : agents.GetValueOrDefault(k.Killer), k.DamageItem));
            }
            if (plantTime > 0)
            {
                events.Add(new RoundEvent(RoundEventType.Plant, plantTime, AtRoundTime(plantTime), agents.GetValueOrDefault(round.GetStringOrEmpty("bombPlanter"))));
                if (resultCode == "Detonate")
                    events.Add(new RoundEvent(RoundEventType.Detonate, plantTime + SpikeTimerMs, AtRoundTime(plantTime + SpikeTimerMs)));
            }
            if (defuseTime > 0)
                events.Add(new RoundEvent(RoundEventType.Defuse, defuseTime, AtRoundTime(defuseTime), agents.GetValueOrDefault(round.GetStringOrEmpty("bombDefuser"))));

            rounds.Add(new RoundStats(
                number,
                won,
                resultCode,
                round.GetStringOrEmpty("roundCeremony"),
                attacking,
                myScore,
                opponentScore,
                roundKills.Count(k => k.Killer == myPuuid && !AreAllies(k.Killer, k.Victim)),
                roundKills.Any(k => k.Victim == myPuuid),
                dealt.Sum(d => d.Damage),
                economy.GetInt("loadoutValue"),
                economy.GetInt("spent"),
                economy.GetInt("remaining"),
                NullIfEmpty(economy.GetStringOrEmpty("weapon")),
                NullIfEmpty(economy.GetStringOrEmpty("armor")),
                plantTime > 0 ? NullIfEmpty(round.GetStringOrEmpty("plantSite")) : null,
                AtRoundTime(0),
                dealt.OrderByDescending(d => d.Damage).ToList(),
                events.OrderBy(ev => ev.RoundTimeMs).ToList()));
        }

        // ---- スコアボード ----
        var teamMvps = root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array
            ? teams.EnumerateArray().Select(t => t.GetStringOrEmpty("mvp")).Where(m => m.Length > 0).ToHashSet()
            : [];
        var result = new List<PlayerStats>();
        if (root.TryGetProperty("players", out var playerList) && playerList.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in playerList.EnumerateArray())
            {
                if (p.TryGetProperty("isObserver", out var obs) && obs.ValueKind == JsonValueKind.True) continue;
                var subject = p.GetStringOrEmpty("subject");
                var stats = p.TryGetProperty("stats", out var st) && st.ValueKind == JsonValueKind.Object ? st : default;
                var a = Acc(subject);
                var teamId = p.GetStringOrEmpty("teamId");
                result.Add(new PlayerStats(
                    subject,
                    teamId,
                    NullIfEmpty(p.GetStringOrEmpty("characterId")),
                    subject == myPuuid,
                    myTeam is not null && teamId == myTeam,
                    teamMvps.Contains(subject),
                    stats.GetInt("roundsPlayed"),
                    stats.GetInt("score"),
                    stats.GetInt("kills"),
                    stats.GetInt("deaths"),
                    stats.GetInt("assists"),
                    a.Damage,
                    a.Headshots,
                    a.Bodyshots,
                    a.Legshots,
                    a.KastRounds,
                    a.FirstKills,
                    a.FirstDeaths,
                    a.MultiKills));
            }
        }

        return new MatchStats(result, rounds);
    }

    // キル・アシスト・生存・トレード（倒した相手が TradeWindowMs 以内に倒された）のいずれかがあったか
    private static bool IsKast(string puuid, List<MatchTimelineBuilder.Kill> roundKills)
    {
        if (roundKills.Any(k => k.Killer == puuid && k.Victim != puuid)) return true;
        if (roundKills.Any(k => k.Assistants.Contains(puuid))) return true;
        if (roundKills.FirstOrDefault(k => k.Victim == puuid) is not { } death) return true;
        return death.Killer != puuid && roundKills.Any(k =>
            k.Victim == death.Killer && k.GameTime >= death.GameTime && k.GameTime - death.GameTime <= TradeWindowMs);
    }

    // players[].subject → teamId
    private static Dictionary<string, string> ReadTeams(JsonElement root)
    {
        var teams = new Dictionary<string, string>();
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return teams;
        foreach (var p in players.EnumerateArray())
        {
            var subject = p.GetStringOrEmpty("subject");
            var teamId = p.GetStringOrEmpty("teamId");
            if (subject.Length > 0 && teamId.Length > 0) teams[subject] = teamId;
        }
        return teams;
    }

    private static string? NullIfEmpty(string s) => s.Length > 0 ? s : null;

    private static int GetInt(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : 0;

    private sealed class Accumulator
    {
        public int Damage, Headshots, Bodyshots, Legshots, KastRounds, FirstKills, FirstDeaths;
        public readonly Dictionary<int, int> MultiKills = [];
    }
}
