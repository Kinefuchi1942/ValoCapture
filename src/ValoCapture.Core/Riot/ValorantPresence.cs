using System.Text;
using System.Text.Json;

namespace ValoCapture.Core.Riot;

/// <summary>
/// presence の private フィールド（base64 JSON）から取り出した VALORANT の状態。
/// </summary>
public sealed record ValorantPresence(
    string SessionLoopState,
    string QueueId,
    string MapId,
    string ProvisioningFlow,
    string RawJson)
{
    /// <summary>
    /// presences 応答から自分（puuid）の VALORANT presence を探す。VALORANT が起動していなければ null。
    /// </summary>
    public static ValorantPresence? FindOwn(JsonDocument presences, string puuid)
    {
        if (!presences.RootElement.TryGetProperty("presences", out var list)) return null;

        foreach (var p in list.EnumerateArray())
        {
            if (p.GetStringOrEmpty("product") != "valorant") continue;
            if (p.GetStringOrEmpty("puuid") != puuid) continue;

            var encoded = p.GetStringOrEmpty("private");
            if (encoded.Length == 0) return null;

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return Parse(json);
        }
        return null;
    }

    /// <summary>
    /// private JSON を解析する。パッチで構造が変わってきた（旧: トップレベル、新: matchPresenceData 等にネスト）ため、
    /// matchPresenceData → トップレベル → 全体探索の順でキーを探す。
    /// </summary>
    public static ValorantPresence Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string Find(string key)
        {
            if (root.TryGetProperty("matchPresenceData", out var match) && match.GetStringOrEmpty(key) is { Length: > 0 } a) return a;
            if (root.GetStringOrEmpty(key) is { Length: > 0 } b) return b;
            return FindRecursive(root, key) ?? "";
        }

        return new ValorantPresence(
            Find("sessionLoopState"),
            Find("queueId"),
            Find("matchMap"),
            Find("provisioningFlow"),
            json);
    }

    private static string? FindRecursive(JsonElement e, string key)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Name == key && prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { Length: > 0 } s)
                        return s;
                    if (FindRecursive(prop.Value, key) is { } found) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (FindRecursive(item, key) is { } found) return found;
                break;
        }
        return null;
    }
}

internal static class JsonElementExtensions
{
    public static string GetStringOrEmpty(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
