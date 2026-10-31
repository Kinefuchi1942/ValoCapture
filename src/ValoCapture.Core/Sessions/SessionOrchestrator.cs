using System.Text.Json;
using ValoCapture.Core.Game;
using ValoCapture.Core.Recording;
using ValoCapture.Core.Riot;

namespace ValoCapture.Core.Sessions;

/// <summary>
/// 状態遷移を受けて「対象試合に入ったら録画開始 → 抜けたら停止 → 試合詳細を取得してタイムライン作成」を行う。
/// </summary>
public sealed class SessionOrchestrator
{
    private readonly GameStateMonitor _monitor;
    private readonly IScreenRecorder _recorder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Task> _background = [];
    private MatchSession? _current;

    public SessionOrchestrator(GameStateMonitor monitor, IScreenRecorder recorder, MatchTarget target, string outputRoot)
    {
        _monitor = monitor;
        _recorder = recorder;
        Target = target;
        OutputRoot = outputRoot;
        _monitor.StateChanged += (prev, next) => Track(HandleStateChangedAsync(next));
    }

    public event Action<string>? Log;

    /// <summary>セッションの状態が変わって保存されたとき（録画開始・停止・タイムライン作成・失敗など）。任意のスレッドから呼ばれる。</summary>
    public event Action<MatchSession>? SessionChanged;

    /// <summary>録画対象のキュー。次の試合から反映される。</summary>
    public MatchTarget Target { get; set; }

    /// <summary>録画の保存先。次の試合から反映される。</summary>
    public string OutputRoot { get; set; }

    /// <summary>キルフィードとの照合で求めた時刻補正（<see cref="DefaultOffsetMs"/> の既定値）。</summary>
    public const long CalibratedOffsetMs = 6_850;

    /// <summary>試合詳細の取得を試す間隔と回数（試合終了から数分かかる。途中退出なら試合が終わるまで待つ）。</summary>
    public TimeSpan DetailsRetryInterval { get; init; } = TimeSpan.FromSeconds(60);
    public int DetailsRetryCount { get; init; } = 20;

    /// <summary>
    /// 新しいセッションの時刻補正の初期値。gameTime の基準（<see cref="MatchTimelineBuilder.JoinLeadMs"/>）+ gameTime は
    /// キルフィード表示より 6〜7.5 秒早い。試合内のずれは一定（キルは ±0.05 秒）だが、試合ごとに基準が約 1 秒ばらつく
    /// （2026-10-28 のデスマッチ 2 試合で 6.33 秒 / 7.38 秒、2026-10-30 のコンペティティブで 6.34 秒。
    /// gameTime の 0 はクライアントの接続前のサーバー時刻で、ローカルのログ時刻とも一定の関係にない）。
    /// 中間値を使い、±0.5 秒程度の誤差は許容する。
    /// ラウンド制のモードでは、試合詳細を取得したときにゲームのログのラウンド開始時刻から試合ごとの値を求めて置き換える
    /// （<see cref="MatchTimelineBuilder.EstimateOffsetMs"/>）。既定値のまま使うのはデスマッチやログが残っていない場合。
    /// 試合ごとの正確な値は tools/calibrate_offset.py で測れる。
    /// </summary>
    public long DefaultOffsetMs { get; init; } = CalibratedOffsetMs;

    /// <summary>ラウンドの時刻を読む VALORANT のログ。</summary>
    public string GameLogPath { get; init; } = ShooterGameLog.DefaultPath;

    public MatchSession? Current => _current;

    private async Task HandleStateChangedAsync(GameSnapshot next)
    {
        await _gate.WaitAsync();
        try
        {
            if (_current is null && next.State == GameState.InGame && Target.Matches(next))
                await StartSessionAsync(next);
            else if (_current is not null && IsOutOfMatch(next.State))
                await EndSessionAsync();
            else if (_current is { MapId.Length: 0 } && next is { State: GameState.InGame, MapId.Length: > 0 })
            {
                // 試合に入った直後の presence にはまだマップが入っていないことがある
                _current.MapId = next.MapId;
                Persist(_current);
            }
        }
        catch (Exception ex)
        {
            Write($"エラー: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // API の一時的な不調（Unavailable / Unknown）では録画を止めない
    private static bool IsOutOfMatch(GameState state) =>
        state is GameState.Menus or GameState.Pregame or GameState.ValorantClosed or GameState.RiotClientClosed;

    private async Task StartSessionAsync(GameSnapshot snap)
    {
        // マップは試合開始直後だと未確定のことがあるのでフォルダ名には入れない（session.json に記録する）
        var dir = Path.Combine(OutputRoot, $"{snap.ObservedAt.ToLocalTime():yyyyMMdd-HHmmss}_{snap.QueueId}");
        Directory.CreateDirectory(dir);

        var session = new MatchSession
        {
            Directory = dir,
            QueueId = snap.QueueId,
            MapId = snap.MapId,
            Puuid = _monitor.Puuid,
            DetectedAt = snap.ObservedAt,
            OffsetMs = DefaultOffsetMs,
            Status = SessionStatus.Recording,
        };

        session.RecordStartedAt = await _recorder.StartAsync(session.VideoPath);
        Persist(session);
        _current = session;
        Write($"録画開始: {dir}");

        Track(ResolveMatchIdAsync(session));
    }

    private async Task ResolveMatchIdAsync(MatchSession session)
    {
        // 試合に入った直後は core-game がまだ 404 のことがあるので数回試す
        for (var i = 0; i < 10 && session.MatchId is null; i++)
        {
            try
            {
                using var remote = CreateRemoteClient();
                session.MatchId = await remote.GetCurrentMatchIdAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                Write($"MatchID 取得失敗（{i + 1} 回目）: {ex.Message}");
            }
            if (session.MatchId is null) await Task.Delay(TimeSpan.FromSeconds(3));
        }

        await _gate.WaitAsync();
        try { Persist(session); } finally { _gate.Release(); }
        Write(session.MatchId is null ? "MatchID を取得できませんでした" : $"MatchID: {session.MatchId}");
    }

    private async Task EndSessionAsync()
    {
        var session = _current!;
        _current = null;

        var result = await _recorder.StopAsync();
        session.RecordStoppedAt = result.StoppedAt;
        session.Status = SessionStatus.FetchingDetails;
        SaveGameLog(session);
        Persist(session);
        Write($"録画停止:{result.FilePath}（{(result.StoppedAt - result.StartedAt):mm\\:ss}）");

        Track(FinalizeAsync(session));
    }

    private async Task FinalizeAsync(MatchSession session)
    {
        // MatchID 解決が終わるまで少し待つ（試合が極端に短い場合）
        for (var i = 0; i < 30 && session.MatchId is null; i++) await Task.Delay(1000);

        if (session.MatchId is null || session.Puuid is null)
        {
            Fail(session, "MatchID または puuid が不明なため試合詳細を取得できません");
            return;
        }

        for (var attempt = 1; attempt <= DetailsRetryCount; attempt++)
        {
            await Task.Delay(DetailsRetryInterval);
            try
            {
                using var remote = CreateRemoteClient();
                var json = await remote.GetMatchDetailsJsonAsync(session.MatchId);
                if (json is null)
                {
                    Write($"試合詳細はまだありません（{attempt}/{DetailsRetryCount}）");
                    continue;
                }

                session.SaveJson(MatchSession.MatchFile, PrettyJson(json));
                ApplyGameLogOffset(session, json);
                var timeline = BuildTimeline(session, json);
                session.SaveTimeline(timeline);
                if (session.MapId.Length == 0) session.MapId = timeline.MapId;
                session.Status = SessionStatus.Ready;
                Persist(session);
                Write($"タイムライン作成: {timeline.MyKills}K / {timeline.MyDeaths}D / {timeline.MyAssists}A, イベント {timeline.Events.Count} 件 → {session.Directory}");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                Write($"試合詳細の取得に失敗（{attempt}/{DetailsRetryCount}）: {ex.Message}");
            }
        }

        Fail(session, "試合詳細を取得できませんでした（リトライ上限）");
    }

    // ゲームのログは次にゲームを起動すると上書きされるので、録画の範囲のラウンドの行をセッションに残す
    private void SaveGameLog(MatchSession session)
    {
        if (session.RecordStartedAt is not { } start || session.RecordStoppedAt is not { } stop) return;
        try
        {
            var text = ShooterGameLog.TryReadText(GameLogPath);
            if (text is null) return;
            var lines = ShooterGameLog.ExtractRoundLines(text, start, stop);
            if (lines.Count > 0) File.WriteAllLines(Path.Combine(session.Directory, MatchSession.GameLogFile), lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Write($"ゲームのログを保存できませんでした: {ex.Message}");
        }
    }

    private void ApplyGameLogOffset(MatchSession session, string matchJson)
    {
        var path = Path.Combine(session.Directory, MatchSession.GameLogFile);
        if (!File.Exists(path)) return;
        var starts = ShooterGameLog.ReadRoundStarts(File.ReadAllLines(path));
        if (MatchTimelineBuilder.EstimateOffsetMs(matchJson, starts) is not { } offset) return;
        session.OffsetMs = offset;
        Write($"時刻補正をゲームのログから求めました: {offset / 1000.0:+0.00;-0.00} 秒");
    }

    public static MatchTimeline BuildTimeline(MatchSession session, string matchJson) =>
        MatchTimelineBuilder.Build(
            matchJson,
            session.Puuid!,
            session.RecordStartedAt,
            session.RecordStoppedAt - session.RecordStartedAt,
            session.OffsetMs);

    /// <summary>録画中なら停止し、バックグラウンド処理（試合詳細の取得など）の完了を待つ。</summary>
    public async Task ShutdownAsync(bool waitForBackground)
    {
        await _gate.WaitAsync();
        try
        {
            if (_current is not null) await EndSessionAsync();
        }
        finally
        {
            _gate.Release();
        }

        if (waitForBackground)
        {
            Task[] pending;
            lock (_background) pending = [.. _background];
            await Task.WhenAll(pending);
        }
    }

    private void Fail(MatchSession session, string error)
    {
        session.Status = SessionStatus.Failed;
        session.Error = error;
        Persist(session);
        Write(error);
    }

    // 監視側のクライアントは Riot Client 再起動時に破棄されるため、毎回 lockfile から作り直す
    private static RiotRemoteClient CreateRemoteClient()
    {
        var lockfile = Lockfile.TryRead() ?? throw new InvalidOperationException("Riot Client が起動していません");
        var shard = ShooterGameLog.TryRead() ?? throw new InvalidOperationException("shard 情報が取れません");
        return new RiotRemoteClient(new RiotLocalClient(lockfile), shard, ownsLocalClient: true);
    }

    private void Track(Task task)
    {
        lock (_background)
        {
            _background.RemoveAll(t => t.IsCompleted);
            _background.Add(task);
        }
    }

    private void Persist(MatchSession session)
    {
        session.Save();
        SessionChanged?.Invoke(session);
    }

    private void Write(string message) => Log?.Invoke(message);

    private static string PrettyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }
}
