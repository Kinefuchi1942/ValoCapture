using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ValoRecorder.App.Services;
using ValoRecorder.App.ViewModels;
using ValoRecorder.Core.Game;
using ValoRecorder.Core.Sessions;
using ValoRecorder.Core.Settings;

namespace ValoRecorder.App;

/// <summary>
/// タスクトレイに常駐して対象試合を自動録画し、録画をビューアで見られるようにする。
/// 起動オプション: --minimized（ウィンドウを開かずにトレイだけで起動）
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppSettings _settings = new();
    private RecorderHost? _host;
    private TrayIcon? _tray;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _exiting;
    private readonly HashSet<string> _notifiedStarts = [];
    // 音量スライダーを動かしている間に何度も保存しないよう、止まってから保存する
    private readonly DispatcherTimer _volumeSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private static string LogPath => Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath)!, "app.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 2 つ起動すると二重に録画するので防ぐ
        _singleInstance = new Mutex(true, @"Local\ValoRecorder.App", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("ValoRecorder はすでに起動しています（タスクトレイを確認してください）。", "ValoRecorder");
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _settings = AppSettings.Load();
        _settings.Save(); // 初回起動時に既定値のファイルを作っておく（手で編集できるように）

        _vm = new MainViewModel(_settings.RecordingsRoot, _settings.Volume);
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _volumeSaveTimer.Tick += (_, _) => { _volumeSaveTimer.Stop(); _settings.Save(); };

        _host = new RecorderHost(_settings);
        _host.Log += WriteLog;
        _host.StateChanged += _ => Dispatcher.BeginInvoke(UpdateStatus);
        _host.SessionChanged += s => Dispatcher.BeginInvoke(() => OnSessionChanged(s));

        _tray = new TrayIcon(ShowViewer, OpenRecordingsFolder, () => _ = ExitAsync(), IsQueueSelected, ToggleQueue);

        _window = new MainWindow(_vm);
        _window.Closing += OnWindowClosing;
        if (!e.Args.Contains("--minimized")) _window.Show();

        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();

        _host.Start();
        WriteLog($"起動: 録画先={_settings.RecordingsRoot} 対象={string.Join(",", _settings.TargetQueues)}");
    }

    private void OnSessionChanged(MatchSession session)
    {
        _vm!.Reload();
        UpdateStatus();

        var title = $"{DisplayNames.Map(session.MapId)}（{DisplayNames.Queue(session.QueueId)}）";
        switch (session.Status)
        {
            // Recording は MatchID 取得後にも保存されるので、通知は最初の 1 回だけ
            case SessionStatus.Recording when _notifiedStarts.Add(session.Directory):
                _tray!.Notify("録画を開始しました", title);
                break;
            case SessionStatus.FetchingDetails:
                _tray!.Notify("録画を停止しました", $"{title} — 試合が終わるとイベントが追加されます");
                break;
            case SessionStatus.Ready:
                _tray!.Notify("イベントを記録しました", title);
                break;
            case SessionStatus.Failed:
                _tray!.Notify("イベントを取得できませんでした", $"{title}: {session.Error}");
                break;
        }
    }

    private void UpdateStatus()
    {
        if (_host is null || _vm is null || _tray is null) return;

        var recording = _host.CurrentSession;
        var targets = "録画対象: " + string.Join("・", _host.Target.Queues.Select(DisplayNames.Queue));
        string tray;
        if (recording is not null)
        {
            var elapsed = recording.RecordStartedAt is { } start ? DateTimeOffset.UtcNow - start : TimeSpan.Zero;
            var map = recording.MapId.Length > 0 ? DisplayNames.Map(recording.MapId) : "マップ確認中";
            _vm.StatusTitle = "録画中";
            _vm.StatusTime = elapsed.ToString(@"mm\:ss");
            _vm.StatusDetail = $"{DisplayNames.Queue(recording.QueueId)} · {map}";
            tray = $"録画中 {map} {_vm.StatusTime}";
        }
        else
        {
            var s = _host.State;
            _vm.StatusTitle = s.State switch
            {
                GameState.RiotClientClosed or GameState.ValorantClosed => "VALORANT 未起動",
                GameState.RiotClientUnavailable => "Riot Client に接続できません",
                GameState.InGame => "録画対象外の試合中",
                _ => "待機中",
            };
            _vm.StatusTime = "";
            _vm.StatusDetail = s.State == GameState.InGame ? $"{DisplayNames.Queue(s.QueueId)} · {targets}" : targets;
            tray = _vm.StatusTitle;
        }

        _vm.IsRecording = recording is not null;
        _tray.Update(tray, recording is not null);
    }

    private bool IsQueueSelected(string queueId) => _host!.Target.Queues.Contains(queueId);

    private void ToggleQueue(string queueId, bool include)
    {
        var queues = _host!.Target.Queues.ToHashSet();
        if (include) queues.Add(queueId); else queues.Remove(queueId);

        _host.Target = new MatchTarget(queues);
        _settings.TargetQueues = [.. queues];
        _settings.Save();
        UpdateStatus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.RecordingsRoot):
                _settings.RecordingsRoot = _vm!.RecordingsRoot;
                _settings.Save();
                _host!.OutputRoot = _vm.RecordingsRoot;
                break;
            case nameof(MainViewModel.Volume):
                _settings.Volume = _vm!.Volume;
                _volumeSaveTimer.Stop();
                _volumeSaveTimer.Start();
                break;
        }
    }

    private void ShowViewer()
    {
        _window!.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OpenRecordingsFolder()
    {
        Directory.CreateDirectory(_settings.RecordingsRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.RecordingsRoot}\"") { UseShellExecute = true });
    }

    // ×ボタンではトレイに隠すだけにする（録画を続けるため）
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        _window!.PauseVideo();
        _window.Hide();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        if (_host?.CurrentSession is not null &&
            MessageBox.Show("録画中です。録画を停止して終了しますか？", "ValoRecorder", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        _exiting = true;
        _statusTimer.Stop();
        if (_host is not null) await _host.DisposeAsync();
        _tray?.Dispose();
        _window?.Close();
        WriteLog("終了");
        Shutdown();
    }

    private static void WriteLog(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
