using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValoCapture.Core.Riot;

public sealed record EntitlementsToken(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("subject")] string Puuid);

/// <summary>
/// Riot Client のローカル API（https://127.0.0.1:{port}）へのクライアント。
/// </summary>
public sealed class RiotLocalClient : IDisposable
{
    private readonly HttpClient _http;

    public Lockfile Lockfile { get; }

    public RiotLocalClient(Lockfile lockfile)
    {
        Lockfile = lockfile;

        // ローカル API は自己署名証明書。接続先は 127.0.0.1 固定なので検証を省略する
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{lockfile.Port}/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{lockfile.Password}")));
    }

    public async Task<EntitlementsToken> GetEntitlementsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<EntitlementsToken>("entitlements/v1/token", ct)
        ?? throw new InvalidOperationException("entitlements が空です");

    /// <summary>/chat/v4/presences の生 JSON。</summary>
    public async Task<JsonDocument> GetPresencesAsync(CancellationToken ct = default)
    {
        await using var stream = await _http.GetStreamAsync("chat/v4/presences", ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    public void Dispose() => _http.Dispose();
}
