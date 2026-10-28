using ValoRecorder.Core.Riot;

namespace ValoRecorder.Core.Game;

public enum GameState
{
    /// <summary>Riot Client が起動していない。</summary>
    RiotClientClosed,
    /// <summary>Riot Client は起動しているが応答しない／未ログイン。</summary>
    RiotClientUnavailable,
    /// <summary>VALORANT が起動していない。</summary>
    ValorantClosed,
    Menus,
    Pregame,
    InGame,
    /// <summary>未知の sessionLoopState。</summary>
    Unknown,
}

public sealed record GameSnapshot(
    GameState State,
    DateTimeOffset ObservedAt,
    string QueueId = "",
    string MapId = "",
    string ProvisioningFlow = "",
    ValorantPresence? Presence = null,
    string? Error = null)
{
    /// <summary>状態遷移として扱うべき差分があるか（時刻や生 JSON は比較しない）。</summary>
    public bool IsSameStateAs(GameSnapshot other) =>
        State == other.State
        && QueueId == other.QueueId
        && MapId == other.MapId
        && ProvisioningFlow == other.ProvisioningFlow;

    public static GameState MapLoopState(string sessionLoopState) => sessionLoopState switch
    {
        "MENUS" => GameState.Menus,
        "PREGAME" => GameState.Pregame,
        "INGAME" => GameState.InGame,
        _ => GameState.Unknown,
    };
}
