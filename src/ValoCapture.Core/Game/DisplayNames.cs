namespace ValoCapture.Core.Game;

/// <summary>マップ ID・キュー ID・エージェント ID の表示名。</summary>
public static class DisplayNames
{
    // マップ ID はコードネーム（/Game/Maps/Triad/Triad など）
    private static readonly Dictionary<string, string> Maps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ascent"] = "アセント",
        ["Bonsai"] = "スプリット",
        ["Canyon"] = "フラクチャー",
        ["Duality"] = "バインド",
        ["Foxtrot"] = "ブリーズ",
        ["Infinity"] = "アビス",
        ["Jam"] = "ロータス",
        ["Juliett"] = "サンセット",
        ["Pitt"] = "パール",
        ["Port"] = "アイスボックス",
        ["Triad"] = "ヘイヴン",
    };

    private static readonly Dictionary<string, string> Queues = new(StringComparer.OrdinalIgnoreCase)
    {
        [QueueIds.Competitive] = "コンペティティブ",
        [QueueIds.Unrated] = "アンレート",
        [QueueIds.Swiftplay] = "スイフトプレイ",
        [QueueIds.SpikeRush] = "スパイクラッシュ",
        [QueueIds.Deathmatch] = "デスマッチ",
        [QueueIds.TeamDeathmatch] = "チームデスマッチ",
        [QueueIds.Premier] = "プレミア",
    };

    // エージェント ID（players[].characterId）。コメントは開発名。valorant-api.com の /v1/agents?language=en-US から作成（表示は英語名）
    private static readonly Dictionary<string, string> Agents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["e370fa57-4757-3604-3648-499e1f642d3f"] = "Gekko", // Aggrobot
        ["dade69b4-4f5a-8528-247b-219e5a1facd6"] = "Fade", // BountyHunter
        ["5f8d3a7f-467b-97f3-062c-13acf203c006"] = "Breach", // Breach
        ["cc8b64c8-4b25-4ff9-6e7f-37b4da43d235"] = "Deadlock", // Cable
        ["b444168c-4e35-8076-db47-ef9bf368f384"] = "Tejo", // Cashew
        ["f94c3b30-42be-e959-889c-5aa313dba261"] = "Raze", // Clay
        ["22697a3d-45bf-8dd7-4fec-84a9e28c69d7"] = "Chamber", // Deadeye
        ["601dbbe7-43ce-be57-2a40-4abd24953621"] = "KAY/O", // Grenadier
        ["6f2a04ca-43e0-be17-7f36-b3908627744d"] = "Skye", // Guide
        ["117ed9e3-49f3-6512-3ccf-0cada7e3823b"] = "Cypher", // Gumshoe
        ["320b2a48-4d9b-a075-30f1-1f93a9b638fa"] = "Sova", // Hunter
        ["7c8a4701-4de6-9355-b254-e09bc2a34b72"] = "Miks", // Iris
        ["1e58de9c-4950-5125-93e9-a0aee9f98746"] = "Killjoy", // Killjoy
        ["95b78ed7-4637-86d9-7e41-71ba8c293152"] = "Harbor", // Mage
        ["efba5359-4016-a1e5-7626-b1ae76895940"] = "Vyse", // Nox
        ["707eab51-4836-f488-046a-cda6bf494859"] = "Viper", // Pandemic
        ["ddd5a15c-4fbe-42fa-3789-11a55ca5ee04"] = "Hush", // Paprika
        ["eb93336a-449b-9c1b-0a54-a891f7921d69"] = "Phoenix", // Phoenix
        ["92eeef5d-43b5-1d4a-8d03-b3927a09034b"] = "Veto", // Pine
        ["41fb69c1-4189-7b37-f117-bcaf1e96f1bf"] = "Astra", // Rift
        ["9f0d8ba9-4140-b941-57d3-a7ad57c6b417"] = "Brimstone", // Sarge
        ["0e38b510-41a8-5780-5e8f-568b2a4f2d6c"] = "Iso", // Sequoia
        ["1dbf2edd-4729-0984-3115-daa5eed44993"] = "Clove", // Smonk
        ["bb2a4828-46eb-8cd1-e765-15848195d751"] = "Neon", // Sprinter
        ["7f94d92c-4234-0a36-9646-3a87eb8b5c89"] = "Yoru", // Stealth
        ["df1cb487-4902-002e-5c17-d28e83e78588"] = "Waylay", // Terra
        ["569fdd95-4d10-43ab-ca70-79becc718b46"] = "Sage", // Thorne
        ["a3bfb853-43b2-7238-a4f1-ad90e9e46bcc"] = "Reyna", // Vampire
        ["8e253930-4c05-31dd-1b6c-968525494517"] = "Omen", // Wraith
        ["add6443a-41bd-e414-f6ad-e58d267f4e95"] = "Jett", // Wushu
    };

    public static string Map(string mapId)
    {
        var code = mapId.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return Maps.TryGetValue(code, out var name) ? name : code.Length > 0 ? code : "不明なマップ";
    }

    public static string Queue(string queueId) =>
        Queues.TryGetValue(queueId, out var name) ? name : queueId;

    /// <summary>エージェントの表示名。ID が不明（新しいエージェントなど）なら null。</summary>
    public static string? Agent(string? characterId) =>
        characterId is not null && Agents.TryGetValue(characterId, out var name) ? name : null;
}
