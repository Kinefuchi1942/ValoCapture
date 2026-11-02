using System.Text.Json;
using ValoCapture.Core.Game;
using ValoCapture.Core.Riot;
using ValoCapture.Core.Sessions;

namespace ValoCapture.Core.Tests;

public class MatchStatsTests
{
    private const string Me = "me", Ally = "ally", Foe1 = "foe1", Foe2 = "foe2";
    private const long GameStart = 1_800_000_000_000;
    private const string Vandal = "9C82E19D-4575-0200-1A81-3EACF00CF872";

    private static object Kill(int round, long gameTime, long roundTime, string killer, string victim, params string[] assistants) => new
    {
        round,
        gameTime,
        roundTime,
        killer,
        victim,
        assistants,
        finishingDamage = new { damageType = "Weapon", damageItem = Vandal },
    };

    private static object Damage(string receiver, int damage, int headshots, int bodyshots, int legshots) =>
        new { receiver, damage, headshots, bodyshots, legshots };

    private static object Stats(string subject, object[] kills, object[]? damage = null, int loadoutValue = 0) => new
    {
        subject,
        kills,
        damage = damage ?? [],
        economy = new { loadoutValue, weapon = loadoutValue > 0 ? Vandal : "", armor = "", remaining = 100, spent = loadoutValue },
    };

    private static object Player(string subject, string teamId, int score, int kills, int deaths, int assists) => new
    {
        subject,
        teamId,
        characterId = subject == Foe1 ? "a3bfb853-43b2-7238-a4f1-ad90e9e46bcc" : "add6443a-41bd-e414-f6ad-e58d267f4e95",
        stats = new { score, roundsPlayed = 2, kills, deaths, assists },
    };

    // ラウンド 0（自分たちが守りで勝ち）: 自分が 2 キル（1 本目がファーストブラッド）。
    // ラウンド 1（攻めで負け・爆破）: 自分が先に倒され、4 秒後に味方が倒した相手を倒す（トレード）。
    private static string Match() => JsonSerializer.Serialize(new
    {
        matchInfo = new { matchId = "m1", queueID = "competitive", mapId = "/Game/Maps/Ascent/Ascent", gameStartMillis = GameStart, gameLengthMillis = 300_000 },
        players = new[]
        {
            Player(Me, "Red", 500, 2, 1, 0),
            Player(Ally, "Red", 300, 1, 0, 1),
            Player(Foe1, "Blue", 200, 1, 1, 0),
            Player(Foe2, "Blue", 100, 0, 2, 0),
        },
        teams = new[]
        {
            new { teamId = "Red", won = true, roundsWon = 1, numPoints = 1, mvp = Me },
            new { teamId = "Blue", won = false, roundsWon = 1, numPoints = 1, mvp = Foe1 },
        },
        roundResults = new object[]
        {
            new
            {
                roundNum = 0, winningTeam = "Red", winningTeamRole = "Defender", roundResultCode = "Elimination", roundCeremony = "CeremonyFlawless",
                plantRoundTime = 0, plantSite = "", defuseRoundTime = 0,
                playerStats = new[]
                {
                    Stats(Me, [Kill(0, 50_000, 10_000, Me, Foe1), Kill(0, 52_000, 12_000, Me, Foe2, Ally)],
                        [Damage(Foe1, 150, 1, 1, 0), Damage(Foe2, 140, 0, 3, 1), Damage(Ally, 20, 0, 1, 0)], 800),
                    Stats(Ally, []),
                    Stats(Foe1, []),
                    Stats(Foe2, []),
                },
            },
            new
            {
                roundNum = 1, winningTeam = "Blue", winningTeamRole = "Defender", roundResultCode = "Detonate", roundCeremony = "CeremonyDefault",
                bombPlanter = Ally, plantRoundTime = 30_000, plantSite = "B", defuseRoundTime = 0,
                playerStats = new[]
                {
                    Stats(Me, [], [Damage(Foe1, 60, 0, 2, 0)], 3900),
                    Stats(Ally, [Kill(1, 144_000, 24_000, Ally, Foe1)]),
                    Stats(Foe1, [Kill(1, 140_000, 20_000, Foe1, Me)]),
                    Stats(Foe2, []),
                },
            },
        },
    });

    private static MatchStats Build(DateTimeOffset? recordStart = null) =>
        MatchStatsBuilder.Build(Match(), Me, recordStart, null);

    [Fact]
    public void Scoreboard_ComputesPerRoundAverages()
    {
        var me = Build().Me!;

        Assert.Equal((250, 2, 1, 0), (me.Acs, me.Kills, me.Deaths, me.Assists));
        // 味方へのダメージ（20）は数えない: (150 + 140 + 60) / 2
        Assert.Equal(175, me.Adr);
        // 頭 1 / 体 6 / 脚 1
        Assert.Equal(13, me.HeadshotPercent);
        Assert.True(me.IsTeamMvp);
        Assert.Equal((1, 1), (me.FirstKills, me.FirstDeaths));
        Assert.Equal(1, me.MultiKills[2]);
    }

    [Fact]
    public void Kast_CountsTradedDeath()
    {
        var stats = Build();

        // 自分: ラウンド 0 はキル、ラウンド 1 は 4 秒後にトレードされた
        Assert.Equal(100, stats.Me!.KastPercent);
        // foe2: ラウンド 0 で倒され（トレードなし）、ラウンド 1 は生存
        Assert.Equal(50, stats.Players.Single(p => p.Puuid == Foe2).KastPercent);
    }

    [Fact]
    public void Players_AreMarkedAllyOrOpponent()
    {
        var players = Build().Players;

        Assert.Equal([Me, Ally], players.Where(p => p.IsAlly).Select(p => p.Puuid));
        Assert.Equal("Reyna", DisplayNames.Agent(players.Single(p => p.Puuid == Foe1).AgentId));
    }

    [Fact]
    public void Rounds_HaveResultSideScoreAndEconomy()
    {
        var rounds = Build().Rounds;

        Assert.Equal(2, rounds.Count);
        var (r0, r1) = (rounds[0], rounds[1]);
        Assert.Equal((true, "Elimination", false, 1, 0), (r0.Won, r0.ResultCode, r0.Attacking, r0.MyScore, r0.OpponentScore));
        Assert.Equal((false, "Detonate", true, 1, 1), (r1.Won, r1.ResultCode, r1.Attacking, r1.MyScore, r1.OpponentScore));
        Assert.Equal((2, false, 290), (r0.Kills, r0.Died, r0.Damage));
        Assert.Equal((0, true, 3900, "B"), (r1.Kills, r1.Died, r1.LoadoutValue, r1.PlantSite));
        Assert.Equal("Vandal", DisplayNames.Weapon(r1.Weapon));
        Assert.Equal([150, 140], r0.DamageDealt.Select(d => d.Damage));
    }

    [Fact]
    public void RoundEvents_IncludeSpikeAndConvertToVideoTime()
    {
        // gameTime の 0 の 10 秒後に録画開始
        var stats = Build(DateTimeOffset.FromUnixTimeMilliseconds(GameStart + 10_000));
        var r1 = stats.Rounds[1];

        // ラウンド 1 の戦闘開始は gameTime 120 秒
        Assert.Equal(110_000, r1.StartVideoMs);
        Assert.Equal(
            [(RoundEventType.Death, 130_000L), (RoundEventType.Plant, 140_000L), (RoundEventType.Detonate, 185_000L)],
            r1.Events.Select(e => (e.Type, e.VideoTimeMs!.Value)));
        Assert.Equal("Vandal", DisplayNames.Weapon(r1.Events[0].DamageItem));
    }

    [Fact]
    public void Deathmatch_HasNoRoundsAndNoHeadshotRate()
    {
        var json = JsonSerializer.Serialize(new
        {
            matchInfo = new { matchId = "m1", queueID = "deathmatch", mapId = "", gameStartMillis = GameStart, gameLengthMillis = 300_000 },
            players = new[] { new { subject = Me, teamId = Me, characterId = "", stats = new { score = 1000, roundsPlayed = 1, kills = 10, deaths = 5, assists = 1 } } },
            teams = Array.Empty<object>(),
            kills = new[] { Kill(0, 10_000, 9_000, Me, "x") },
            roundResults = Array.Empty<object>(),
        });

        var stats = MatchStatsBuilder.Build(json, Me, null, null);

        Assert.Empty(stats.Rounds);
        Assert.Null(stats.Me!.HeadshotPercent);
        Assert.Equal(1000, stats.Me.Acs);
    }

    [Fact]
    public void CompetitiveUpdates_MapsMatchToRankedRating()
    {
        const string json = """
            {"Version":1,"Subject":"me","Matches":[
              {"MatchID":"a","RankedRatingEarned":21,"TierAfterUpdate":19},
              {"MatchID":"b","RankedRatingEarned":-16},
              {"MatchID":"c"}]}
            """;

        var earned = RiotRemoteClient.ReadRankedRatingEarned(json);

        Assert.Equal(new Dictionary<string, int> { ["a"] = 21, ["b"] = -16 }, earned);
    }
}
