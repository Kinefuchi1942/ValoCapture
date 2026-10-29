using System.Text;
using System.Text.Json;
using ValoCapture.Core.Game;
using ValoCapture.Core.Riot;

namespace ValoCapture.Core.Tests;

public class PresenceTests
{
    private const string Puuid = "11111111-2222-3333-4444-555555555555";

    // 旧形式: キーがトップレベル
    private const string LegacyPrivate = """
        {"isValid":true,"sessionLoopState":"INGAME","partyOwnerSessionLoopState":"INGAME",
         "matchMap":"/Game/Maps/Ascent/Ascent","provisioningFlow":"Matchmaking","queueId":"competitive"}
        """;

    // 新形式: matchPresenceData / partyPresenceData にネスト
    private const string NestedPrivate = """
        {"isValid":true,
         "matchPresenceData":{"sessionLoopState":"PREGAME","provisioningFlow":"Matchmaking","matchMap":"/Game/Maps/Bonsai/Bonsai","queueId":""},
         "partyPresenceData":{"partyId":"x","queueEntryNotification":{"queueId":"competitive"}}}
        """;

    [Fact]
    public void Parse_LegacyFormat()
    {
        var p = ValorantPresence.Parse(LegacyPrivate);
        Assert.Equal("INGAME", p.SessionLoopState);
        Assert.Equal("competitive", p.QueueId);
        Assert.Equal("/Game/Maps/Ascent/Ascent", p.MapId);
    }

    [Fact]
    public void Parse_NestedFormat_FallsBackToQueueIdElsewhere()
    {
        var p = ValorantPresence.Parse(NestedPrivate);
        Assert.Equal("PREGAME", p.SessionLoopState);
        Assert.Equal("competitive", p.QueueId);
        Assert.Equal("Matchmaking", p.ProvisioningFlow);
    }

    [Fact]
    public void FindOwn_PicksOwnValorantPresence()
    {
        using var doc = JsonDocument.Parse(Presences(
            ("league_of_legends", Puuid, "{}"),
            ("valorant", "someone-else", LegacyPrivate.Replace("competitive", "unrated")),
            ("valorant", Puuid, LegacyPrivate)));

        var p = ValorantPresence.FindOwn(doc, Puuid);

        Assert.NotNull(p);
        Assert.Equal("competitive", p.QueueId);
    }

    [Fact]
    public void FindOwn_ReturnsNullWhenValorantNotRunning()
    {
        using var doc = JsonDocument.Parse(Presences(("league_of_legends", Puuid, "{}")));
        Assert.Null(ValorantPresence.FindOwn(doc, Puuid));
    }

    [Theory]
    [InlineData("MENUS", GameState.Menus)]
    [InlineData("PREGAME", GameState.Pregame)]
    [InlineData("INGAME", GameState.InGame)]
    [InlineData("", GameState.Unknown)]
    public void MapLoopState(string raw, GameState expected) =>
        Assert.Equal(expected, GameSnapshot.MapLoopState(raw));

    [Fact]
    public void MatchTarget_DefaultIsRankedOnly()
    {
        Assert.True(MatchTarget.Default.Matches(Snap(QueueIds.Competitive)));
        Assert.False(MatchTarget.Default.Matches(Snap(QueueIds.Deathmatch)));
    }

    [Fact]
    public void MatchTarget_ParsesAliases()
    {
        var target = new MatchTarget(QueueIds.Parse("ranked, DM"));
        Assert.True(target.Matches(Snap(QueueIds.Competitive)));
        Assert.True(target.Matches(Snap(QueueIds.Deathmatch)));
        Assert.False(target.Matches(Snap(QueueIds.Unrated)));
        Assert.False(target.Matches(Snap("")));
    }

    private static GameSnapshot Snap(string queueId) => new(GameState.InGame, DateTimeOffset.UtcNow, queueId);

    [Fact]
    public void Lockfile_Parses()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Riot Client:1234:56789:secret:https");
            var lf = Lockfile.TryRead(path);
            Assert.Equal(new Lockfile("Riot Client", 1234, 56789, "secret", "https"), lf);
        }
        finally { File.Delete(path); }
    }

    private static string Presences(params (string Product, string Puuid, string PrivateJson)[] items) =>
        JsonSerializer.Serialize(new
        {
            presences = items.Select(i => new
            {
                product = i.Product,
                puuid = i.Puuid,
                @private = Convert.ToBase64String(Encoding.UTF8.GetBytes(i.PrivateJson)),
            }),
        });
}
