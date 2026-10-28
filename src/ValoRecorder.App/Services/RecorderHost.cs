using ValoRecorder.Core.Game;
using ValoRecorder.Core.Sessions;
using ValoRecorder.Core.Settings;
using ValoRecorder.Recording;

namespace ValoRecorder.App.Services;

/// <summary>
/// バックグラウンドで VALORANT を監視し、対象試合を自動録画する。イベントは任意のスレッドから発火する。
/// </summary>
public sealed class RecorderHost : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly GameStateMonitor _monitor = new();
    private readonly SessionOrchestrator _orchestrator;
    private readonly ScreenRecorderLibRecorder _recorder;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public RecorderHost(AppSettings settings)
    {
        _recorder = new ScreenRecorderLibRecorder(settings.ToRecorderSettings());
        _orchestrator = new SessionOrchestrator(_monitor, _recorder, new MatchTarget(settings.TargetQueues), settings.RecordingsRoot)
        {
            DefaultOffsetMs = settings.DefaultOffsetMs,
        };
        _orchestrator.Log += msg => Log?.Invoke(msg);
        _orchestrator.SessionChanged += s => SessionChanged?.Invoke(s);
        _monitor.StateChanged += (prev, next) =>
        {
            Log?.Invoke($"{prev.State} -> {next.State} queue={next.QueueId} map={next.MapId}{(next.Error is null ? "" : $" error={next.Error}")}");
            StateChanged?.Invoke(next);
        };
    }

    public event Action<GameSnapshot>? StateChanged;
    public event Action<MatchSession>? SessionChanged;
    public event Action<string>? Log;

    public GameSnapshot State => _monitor.Current;

    /// <summary>録画中のセッション（録画していなければ null）。</summary>
    public MatchSession? CurrentSession => _orchestrator.Current;

    public MatchTarget Target
    {
        get => _orchestrator.Target;
        set => _orchestrator.Target = value;
    }

    public string OutputRoot
    {
        get => _orchestrator.OutputRoot;
        set => _orchestrator.OutputRoot = value;
    }

    /// <summary>録画の設定（音声デバイスなど）と保存先を変える。録画中なら次の試合から反映される。</summary>
    public void ApplySettings(AppSettings settings)
    {
        _recorder.Settings = settings.ToRecorderSettings();
        OutputRoot = settings.RecordingsRoot;
    }

    public void Start() => _loop = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _monitor.RunAsync(PollInterval, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 想定外の例外で監視が止まらないようにする
                Log?.Invoke($"監視エラー: {ex}");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    /// <summary>監視を止め、録画中なら停止して保存する。</summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null) await _loop;
        await _orchestrator.ShutdownAsync(waitForBackground: false);
        _monitor.Dispose();
    }
}
