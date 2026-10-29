using ValoCapture.Core.Riot;

namespace ValoCapture.Core.Game;

/// <summary>
/// lockfile とローカル API の presence を定期的にポーリングし、VALORANT の状態遷移を通知する。
/// </summary>
public sealed class GameStateMonitor : IDisposable
{
    private RiotLocalClient? _client;
    private string? _puuid;

    /// <summary>(直前, 現在) の状態。状態に差分があったときだけ発火する。</summary>
    public event Action<GameSnapshot, GameSnapshot>? StateChanged;

    public GameSnapshot Current { get; private set; } = new(GameState.RiotClientClosed, DateTimeOffset.MinValue);

    /// <summary>現在有効なローカル API クライアント（Riot Client 未起動なら null）。</summary>
    public RiotLocalClient? LocalClient => _client;

    public string? Puuid => _puuid;

    public async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            var next = await PollAsync(ct);
            var prev = Current;
            Current = next;
            if (!next.IsSameStateAs(prev)) StateChanged?.Invoke(prev, next);
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task<GameSnapshot> PollAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var lockfile = Lockfile.TryRead();
        if (lockfile is null)
        {
            ResetClient();
            return new(GameState.RiotClientClosed, now);
        }

        // Riot Client が再起動するとポートとパスワードが変わる
        if (_client is null || _client.Lockfile != lockfile)
        {
            ResetClient();
            _client = new RiotLocalClient(lockfile);
        }

        try
        {
            _puuid ??= (await _client.GetEntitlementsAsync(ct)).Puuid;

            using var presences = await _client.GetPresencesAsync(ct);
            var presence = ValorantPresence.FindOwn(presences, _puuid);
            if (presence is null) return new(GameState.ValorantClosed, now);

            return new(
                GameSnapshot.MapLoopState(presence.SessionLoopState),
                now,
                presence.QueueId,
                presence.MapId,
                presence.ProvisioningFlow,
                presence);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _puuid = null;
            // entitlements が 404 = Riot Client はバックグラウンドで動いているが未サインイン
            var error = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }
                ? "未サインイン（Riot Client のみ起動中）"
                : ex.Message;
            return new(GameState.RiotClientUnavailable, now, Error: error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            // presence の形式変更に備え、監視ループは止めずに Unknown として扱う
            return new(GameState.Unknown, now, Error: ex.Message);
        }
    }

    private void ResetClient()
    {
        _client?.Dispose();
        _client = null;
        _puuid = null;
    }

    public void Dispose() => ResetClient();
}
