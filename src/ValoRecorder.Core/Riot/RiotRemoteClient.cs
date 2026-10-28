using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ValoRecorder.Core.Riot;

/// <summary>
/// Riot のゲームサーバー API（glz / pd）へのクライアント。非公式 API なので呼び出しはここに集約する。
/// </summary>
public sealed class RiotRemoteClient : IDisposable
{
    // X-Riot-ClientPlatform に渡す固定のプラットフォーム情報
    private static readonly string ClientPlatform = Convert.ToBase64String(Encoding.UTF8.GetBytes(
        """{"platformType":"PC","platformOS":"Windows","platformOSVersion":"10.0.19042.1.256.64bit","platformChipset":"Unknown"}"""));

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly RiotLocalClient _local;
    private readonly ShardInfo _shard;
    private readonly bool _ownsLocalClient;

    /// <param name="ownsLocalClient">true なら Dispose 時に local も破棄する。</param>
    public RiotRemoteClient(RiotLocalClient local, ShardInfo shard, bool ownsLocalClient = false)
    {
        _local = local;
        _shard = shard;
        _ownsLocalClient = ownsLocalClient;
    }

    private string GlzBase => $"https://glz-{_shard.Region}-1.{_shard.Shard}.a.pvp.net";
    private string PdBase => $"https://pd.{_shard.Shard}.a.pvp.net";

    /// <summary>進行中の試合の MatchID。試合中でなければ null。</summary>
    public async Task<string?> GetCurrentMatchIdAsync(CancellationToken ct = default)
    {
        var ent = await _local.GetEntitlementsAsync(ct);
        using var res = await SendAsync($"{GlzBase}/core-game/v1/players/{ent.Puuid}", ent, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetStringOrEmpty("MatchID") is { Length: > 0 } id ? id : null;
    }

    /// <summary>試合詳細の生 JSON。まだ集計されていなければ null。</summary>
    public async Task<string?> GetMatchDetailsJsonAsync(string matchId, CancellationToken ct = default)
    {
        var ent = await _local.GetEntitlementsAsync(ct);
        using var res = await SendAsync($"{PdBase}/match-details/v1/matches/{matchId}", ent, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    private async Task<HttpResponseMessage> SendAsync(string url, EntitlementsToken ent, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ent.AccessToken);
        req.Headers.Add("X-Riot-Entitlements-JWT", ent.Token);
        req.Headers.Add("X-Riot-ClientPlatform", ClientPlatform);
        req.Headers.Add("X-Riot-ClientVersion", await GetClientVersionAsync(ct));
        return await _http.SendAsync(req, ct);
    }

    private string? _clientVersion;

    // ログから取れなければ valorant-api.com の公開バージョン情報で補う
    private async Task<string> GetClientVersionAsync(CancellationToken ct)
    {
        if (_clientVersion is not null) return _clientVersion;
        if (_shard.ClientVersion is { Length: > 0 } v) return _clientVersion = v;

        using var doc = await _http.GetFromJsonAsync<JsonDocument>("https://valorant-api.com/v1/version", ct);
        return _clientVersion = doc!.RootElement.GetProperty("data").GetProperty("riotClientVersion").GetString()!;
    }

    public void Dispose()
    {
        _http.Dispose();
        if (_ownsLocalClient) _local.Dispose();
    }
}
