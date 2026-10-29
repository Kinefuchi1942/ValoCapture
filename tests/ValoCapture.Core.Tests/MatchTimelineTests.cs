using System.Text.Json;
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

    private static string Match(object[]? topKills = null, object[]? rounds = null) => JsonSerializer.Serialize(new
    {
        matchInfo = new { matchId = "m1", queueID = "deathmatch", mapId = "/Game/Maps/Triad/Triad", gameStartMillis = GameStart, gameLengthMillis = 300_000 },
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
}
