using System.Text.Json;
using ValoCapture.Core.Game;
using ValoCapture.Core.Sessions;

namespace ValoCapture.Core.Tests;

public class MatchTimelineTests
{
    private const string Me = "me";
    private const long GameStart = 1_800_000_000_000;

    private static object Kill(long gameTime, long roundTime, string killer, string victim, int round = 0, params string[] assistants) => new
    {
        gameTime,
        roundTime,
        round,
        killer,
        victim,
        assistants,
        finishingDamage = new { damageType = "Weapon", damageItem = "VANDAL" },
    };

    private const string Jett = "add6443a-41bd-e414-f6ad-e58d267f4e95";
    private const string Sage = "569fdd95-4d10-43ab-ca70-79becc718b46";
    private const string Omen = "8e253930-4c05-31dd-1b6c-968525494517";

    private static string Match(object[]? topKills = null, object[]? rounds = null, object[]? players = null) => JsonSerializer.Serialize(new
    {
        matchInfo = new { matchId = "m1", queueID = "deathmatch", mapId = "/Game/Maps/Triad/Triad", gameStartMillis = GameStart, gameLengthMillis = 300_000 },
        players = players ?? [],
        kills = topKills,
        roundResults = rounds ?? [],
    });

    [Fact]
    public void Deathmatch_TopLevelKills_ConvertsToVideoTime()
    {
        var json = Match(topKills:
        [
            Kill(10_000, 9_000, "a", "b"),
            Kill(38_000, 37_000, "x", Me),
            Kill(42_000, 41_000, Me, "x"),
            Kill(50_000, 49_000, "y", "z", 0, Me),
        ]);
        // 試合開始の 5 秒後に録画開始
        var recordStart = DateTimeOffset.FromUnixTimeMilliseconds(GameStart + 5_000);

        var t = MatchTimelineBuilder.Build(json, Me, recordStart, TimeSpan.FromMinutes(5));

        Assert.Equal((1, 1, 1), (t.MyKills, t.MyDeaths, t.MyAssists));
        var death = t.Events.Single(e => e.Type == MatchEventType.Death);
        Assert.Equal(38_000, death.GameTimeMs);
        Assert.Equal(33_000, death.VideoTimeMs);
        Assert.Equal("VANDAL", death.DamageItem);
    }

    [Fact]
    public void EventsOutsideRecording_HaveNullVideoTime()
    {
        var json = Match(topKills:
        [
            Kill(2_000, 1_000, Me, "x"),     // 録画開始前
            Kill(100_000, 99_000, Me, "x"),  // 録画停止後（退出後）
        ]);
        var recordStart = DateTimeOffset.FromUnixTimeMilliseconds(GameStart + 5_000);

        var t = MatchTimelineBuilder.Build(json, Me, recordStart, TimeSpan.FromSeconds(60));

        Assert.All(t.Events.Where(e => e.Type == MatchEventType.Kill), e => Assert.Null(e.VideoTimeMs));
    }

    [Fact]
    public void RoundBased_ReadsKillsFromPlayerStats_AndDerivesRoundStarts()
    {
        var json = Match(rounds:
        [
            new { roundNum = 0, playerStats = new[] { new { kills = new[] { Kill(60_000, 20_000, Me, "x") } } } },
            new { roundNum = 1, playerStats = new[] { new { kills = new[] { Kill(150_000, 30_000, "x", Me) } } } },
        ]);

        var t = MatchTimelineBuilder.Build(json, Me, DateTimeOffset.FromUnixTimeMilliseconds(GameStart), null, offsetMs: 500);

        var starts = t.Events.Where(e => e.Type == MatchEventType.RoundStart).ToList();
        Assert.Equal([40_000L, 120_000L], starts.Select(e => e.GameTimeMs));
        Assert.Equal(1, t.Events.Single(e => e.Type == MatchEventType.Death).Round);
        Assert.Equal(60_500, t.Events.Single(e => e.Type == MatchEventType.Kill).VideoTimeMs);
    }

    [Fact]
    public void NoRecordStart_LeavesVideoTimeNull()
    {
        var json = Match(topKills: [Kill(10_000, 9_000, Me, "x")]);
        var t = MatchTimelineBuilder.Build(json, Me, null, null);
        Assert.All(t.Events, e => Assert.Null(e.VideoTimeMs));
    }

    [Fact]
    public void KillAndDeath_HaveAgentsOfBothPlayers()
    {
        var json = Match(
            topKills: [Kill(10_000, 9_000, Me, "x"), Kill(20_000, 19_000, "y", Me)],
            players:
            [
                new { subject = Me, characterId = Jett },
                new { subject = "x", characterId = Sage },
                new { subject = "y", characterId = Omen },
            ]);

        var t = MatchTimelineBuilder.Build(json, Me, null, null);

        var kill = t.Events.Single(e => e.Type == MatchEventType.Kill);
        Assert.Equal((Jett, Sage), (kill.KillerAgent, kill.VictimAgent));
        var death = t.Events.Single(e => e.Type == MatchEventType.Death);
        Assert.Equal((Omen, Jett), (death.KillerAgent, death.VictimAgent));
        Assert.Equal(MatchTimeline.CurrentVersion, t.Version);
    }

    [Fact]
    public void NoPlayers_LeavesAgentsNull()
    {
        var t = MatchTimelineBuilder.Build(Match(topKills: [Kill(10_000, 9_000, Me, "x")]), Me, null, null);
        var kill = t.Events.Single(e => e.Type == MatchEventType.Kill);
        Assert.Null(kill.KillerAgent);
        Assert.Null(kill.VictimAgent);
    }

    [Fact]
    public void AgentDisplayName()
    {
        Assert.Equal("ジェット", DisplayNames.Agent(Jett));
        Assert.Equal("ジェット", DisplayNames.Agent(Jett.ToUpperInvariant()));
        Assert.Null(DisplayNames.Agent("00000000-0000-0000-0000-000000000000"));
        Assert.Null(DisplayNames.Agent(null));
    }

    [Fact]
    public void LoadTimeline_RebuildsOldTimelineFromMatchDetails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ValoCaptureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var session = new MatchSession { Directory = dir, Puuid = Me, Status = SessionStatus.Ready };
            session.Save();
            session.SaveJson(MatchSession.MatchFile, Match(
                topKills: [Kill(10_000, 9_000, Me, "x")],
                players: [new { subject = Me, characterId = Jett }, new { subject = "x", characterId = Sage }]));
            // エージェント ID を持たない古い形式（Version なし）の timeline.json
            File.WriteAllText(Path.Combine(dir, MatchSession.TimelineFile), JsonSerializer.Serialize(new MatchTimeline(
                "m1", "deathmatch", "", GameStart, 300_000, 1, 0, 0,
                [new MatchEvent(MatchEventType.Kill, 0, 10_000, null, Me, "x")])).Replace(",\"Version\":0", ""));

            var t = MatchSession.Load(dir).LoadTimeline()!;

            Assert.Equal(MatchTimeline.CurrentVersion, t.Version);
            Assert.Equal(Sage, t.Events.Single(e => e.Type == MatchEventType.Kill).VictimAgent);
            // 作り直したものが保存されている
            Assert.Equal(MatchTimeline.CurrentVersion, MatchSession.Load(dir).LoadTimeline()!.Version);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
