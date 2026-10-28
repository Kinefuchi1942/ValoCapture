// VALORANT の状態検知・録画を手元で確かめるためのコンソールツール。
//   dotnet run --project src/ValoRecorder.Probe                       状態遷移を表示
//   dotnet run --project src/ValoRecorder.Probe -- --dump             遷移ごとに presence の生 JSON も表示
//   dotnet run --project src/ValoRecorder.Probe -- --once             1 回だけ問い合わせて終了
//   dotnet run --project src/ValoRecorder.Probe -- --queue dm         デスマッチを対象にする（ranked,dm,unrated のように複数可。既定は ranked）
//   dotnet run --project src/ValoRecorder.Probe -- --record --queue dm   対象試合を自動録画し、試合後にタイムラインを作る
//   dotnet run --project src/ValoRecorder.Probe -- --record-test 10   ゲームなしで 10 秒だけ録画して録画機能を確認
//   dotnet run --project src/ValoRecorder.Probe -- --timeline <セッションフォルダ>   保存済みの match.json からタイムラインを作り直す
//   dotnet run --project src/ValoRecorder.Probe -- --match <MatchID>  試合詳細を取得して match-<id>.json に保存
//   --out <フォルダ>  録画の保存先（既定: recordings）
//   --engine-log      録画エンジンの内部ログを <保存先>\engine.log に出す（調査用）
//   --monitor         VALORANT のウィンドウではなくモニター全体を録画する
using System.Text.Json;
using ValoRecorder.Core.Game;
using ValoRecorder.Core.Recording;
using ValoRecorder.Core.Riot;
using ValoRecorder.Core.Sessions;
using ValoRecorder.Recording;

var dump = args.Contains("--dump");
var once = args.Contains("--once");
var record = args.Contains("--record");
var queueArg = Arg("--queue");
var target = queueArg is null ? MatchTarget.Default : new MatchTarget(QueueIds.Parse(queueArg));
var outputRoot = Path.GetFullPath(Arg("--out") ?? "recordings");
var settings = new RecorderSettings
{
    EngineLogPath = args.Contains("--engine-log") ? Path.Combine(outputRoot, "engine.log") : null,
    CaptureGameWindow = !args.Contains("--monitor"),
};

if (Arg("--match") is { } matchArg)
{
    await FetchMatchDetailsAsync(matchArg);
    return;
}
if (Arg("--record-test") is { } secondsArg)
{
    await RecordTestAsync(int.Parse(secondsArg));
    return;
}
if (args.Contains("--window-info"))
{
    Console.WriteLine(GameWindow.Find()?.ToString() ?? "VALORANT のウィンドウが見つかりません");
    return;
}
if (Arg("--timeline") is { } sessionDir)
{
    RebuildTimeline(sessionDir);
    return;
}

var logPath = Path.GetFullPath("probe-log.jsonl");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var monitor = new GameStateMonitor();

if (once)
{
    var snap = await monitor.PollAsync(cts.Token);
    Print(snap);
    if (snap.Presence is not null) Console.WriteLine(PrettyJson(snap.Presence.RawJson));
    Console.WriteLine($"shard: {ShooterGameLog.TryRead()}");
    return;
}

monitor.StateChanged += (prev, next) =>
{
    Console.Write($"{prev.State} -> ");
    Print(next);
    if (dump && next.Presence is not null) Console.WriteLine(PrettyJson(next.Presence.RawJson));

    File.AppendAllText(logPath, JsonSerializer.Serialize(new
    {
        at = next.ObservedAt,
        from = prev.State.ToString(),
        to = next.State.ToString(),
        next.QueueId,
        next.MapId,
        next.ProvisioningFlow,
        next.Error,
    }) + Environment.NewLine);
};

SessionOrchestrator? orchestrator = null;
if (record)
{
    orchestrator = new SessionOrchestrator(monitor, new ScreenRecorderLibRecorder(settings), target, outputRoot);
    orchestrator.Log += msg => Console.WriteLine($"  >>> [{DateTime.Now:HH:mm:ss}] {msg}");
}

Console.WriteLine($"監視を開始しました（2 秒間隔、対象キュー: {target}{(record ? $"、録画: {outputRoot}" : "")}）。Ctrl+C で終了。");
try
{
    await monitor.RunAsync(TimeSpan.FromSeconds(2), cts.Token);
}
catch (OperationCanceledException) { }

if (orchestrator is not null)
{
    // 録画中なら止めて保存する。試合詳細の取得待ちは打ち切る（後で --match / --timeline で補える）
    Console.WriteLine("終了処理中…");
    await orchestrator.ShutdownAsync(waitForBackground: false);
}

async Task RecordTestAsync(int seconds)
{
    var path = Path.Combine(outputRoot, $"test-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
    var recorder = new ScreenRecorderLibRecorder(settings);

    var startedAt = await recorder.StartAsync(path);
    Console.WriteLine($"録画開始: {startedAt.ToLocalTime():HH:mm:ss.fff} → {path}（対象: {recorder.CaptureSourceDescription}）");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    var result = await recorder.StopAsync();

    var size = new FileInfo(result.FilePath).Length;
    Console.WriteLine($"録画停止: {result.StoppedAt.ToLocalTime():HH:mm:ss.fff}（{(result.StoppedAt - result.StartedAt).TotalSeconds:F1} 秒, {size / 1024.0 / 1024.0:F1} MB）");
}

void RebuildTimeline(string dir)
{
    var session = MatchSession.Load(Path.GetFullPath(dir));
    var json = File.ReadAllText(Path.Combine(session.Directory, MatchSession.MatchFile));
    var timeline = SessionOrchestrator.BuildTimeline(session, json);
    session.SaveTimeline(timeline);
    PrintTimeline(timeline, session.RecordStartedAt);
}

async Task FetchMatchDetailsAsync(string matchId)
{
    var lockfile = Lockfile.TryRead() ?? throw new InvalidOperationException("Riot Client が起動していません");
    var shard = ShooterGameLog.TryRead() ?? throw new InvalidOperationException("shard 情報が取れません");
    using var remote = new RiotRemoteClient(new RiotLocalClient(lockfile), shard, ownsLocalClient: true);

    var json = await remote.GetMatchDetailsJsonAsync(matchId);
    if (json is null)
    {
        Console.WriteLine("試合詳細はまだありません（404）。試合が終わってから数分後に再実行してください。");
        return;
    }

    var path = Path.GetFullPath($"match-{matchId}.json");
    File.WriteAllText(path, PrettyJson(json));
    Console.WriteLine($"保存しました: {path}");

    using var local = new RiotLocalClient(lockfile);
    var me = (await local.GetEntitlementsAsync()).Puuid;
    PrintTimeline(MatchTimelineBuilder.Build(json, me, recordStartedAt: null, recordLength: null), null);
}

void PrintTimeline(MatchTimeline t, DateTimeOffset? recordStartedAt)
{
    var start = DateTimeOffset.FromUnixTimeMilliseconds(t.GameStartMillis).ToLocalTime();
    Console.WriteLine($"  queue={t.QueueId} map={t.MapId} gameStart={start:HH:mm:ss.fff} length={t.GameLengthMillis / 1000}s");
    if (recordStartedAt is { } rs)
        Console.WriteLine($"  recordStart={rs.ToLocalTime():HH:mm:ss.fff}（試合開始との差 {(rs - start).TotalSeconds:F1} 秒）");
    Console.WriteLine($"  自分: {t.MyKills}K / {t.MyDeaths}D / {t.MyAssists}A");
    foreach (var e in t.Events.Where(e => e.Type != MatchEventType.RoundStart || t.Events.Count(x => x.Type == MatchEventType.RoundStart) > 1))
    {
        var video = e.VideoTimeMs is { } v ? TimeSpan.FromMilliseconds(v).ToString(@"mm\:ss\.f") : "録画範囲外";
        Console.WriteLine($"    R{e.Round,-2} {e.Type,-10} game={e.GameTimeMs / 1000.0,6:F1}s  video={video}");
    }
}

void Print(GameSnapshot s)
{
    // メニューの queueId は「選択中のキュー」なので、対象表示は試合中だけにする
    var mark = s.State == GameState.InGame && target.Matches(s) ? " [TARGET]" : "";
    var detail = s.State is GameState.Menus or GameState.Pregame or GameState.InGame or GameState.Unknown
        ? $" queue={s.QueueId} map={s.MapId} flow={s.ProvisioningFlow}"
        : "";
    var error = s.Error is null ? "" : $" error={s.Error}";
    Console.WriteLine($"[{s.ObservedAt.ToLocalTime():HH:mm:ss}] {s.State}{mark}{detail}{error}");
}

string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

static string PrettyJson(string json)
{
    using var doc = JsonDocument.Parse(json);
    return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
}
