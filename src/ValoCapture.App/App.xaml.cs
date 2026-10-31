using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ValoCapture.App.Services;
using ValoCapture.App.ViewModels;
using ValoCapture.Core.Game;
using ValoCapture.Core.Sessions;
using ValoCapture.Core.Settings;

namespace ValoCapture.App;

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
    private SettingsWindow? _settingsWindow;
    private int _cleanupRunning;
    // 音量スライダーを動かしている間に何度も保存しないよう、止まってから保存する
    private readonly DispatcherTimer _volumeSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly AppUpdater _updater = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromDays(1) };
    private bool _checkingUpdate;
    /// <summary>ダウンロード済みで、再起動すれば適用できる新しい版。</summary>
    private Velopack.VelopackAsset? _readyUpdate;

    private static string LogPath => Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath)!, "app.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 2 つ起動すると二重に録画するので防ぐ
        _singleInstance = new Mutex(true, @"Local\ValoCapture.App", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("ValoCapture はすでに起動しています（タスクトレイを確認してください）。", "ValoCapture");
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _settings = AppSettings.Load();
        _settings.Save(); // 初回起動時に既定値のファイルを作っておく（手で編集できるように）

        _vm = new MainViewModel(_settings.RecordingsRoot, _settings.Volume);
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.SettingsRequested += OpenSettings;
        _vm.SessionDeleted += (s, ex) => WriteLog(ex is null ? $"ごみ箱へ移動: {s.Directory}" : $"ごみ箱へ移動できません: {s.Directory} {ex.Message}");
        _volumeSaveTimer.Tick += (_, _) => { _volumeSaveTimer.Stop(); _settings.Save(); };

        _host = new RecorderHost(_settings);
        _host.Log += WriteLog;
        _host.StateChanged += _ => Dispatcher.BeginInvoke(UpdateStatus);
        _host.SessionChanged += s => Dispatcher.BeginInvoke(() => OnSessionChanged(s));

        _tray = new TrayIcon(ShowViewer, OpenRecordingsFolder, OpenSettings, () => _ = ExitAsync(), IsQueueSelected, ToggleQueue,
            () => _ = ApplyUpdateAsync());

        _window = new MainWindow(_vm);
        _window.Closing += OnWindowClosing;
        if (!e.Args.Contains("--minimized")) _window.Show();

        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();

        _host.Start();
        StartupRegistration.RefreshIfEnabled();
        WriteLog($"起動: バージョン={AppVersion} 録画先={_settings.RecordingsRoot} 対象={string.Join(",", _settings.TargetQueues)}");
        EnforceStorageLimit();
        StartUpdateChecks();
    }

    /// <summary>今のバージョン（1.2.3 の形）。</summary>
    public static string AppVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";

    private void StartUpdateChecks()
    {
        if (!_updater.IsInstalled) return;
        _updateTimer.Tick += (_, _) => { if (_settings.CheckForUpdates) _ = CheckForUpdatesAsync(); };
        _updateTimer.Start();
        // 起動直後は録画の準備などと重ならないよう少し待ってから確認する
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            if (_settings.CheckForUpdates) await CheckForUpdatesAsync();
        });
    }

    /// <summary>新しい版を確認し、あればダウンロードしてトレイで知らせる。結果を設定画面に出す文で返す。</summary>
    private async Task<string> CheckForUpdatesAsync()
    {
        if (!_updater.IsInstalled) return "インストール版ではないため、自動更新は使えません";
        if (_checkingUpdate) return "確認中です";

        _checkingUpdate = true;
        try
        {
            var update = await _updater.CheckAndDownloadAsync();
            if (update is null) return $"最新のバージョンです（{AppVersion}）";

            var version = update.Version.ToString();
            if (_readyUpdate?.Version.ToString() != version)
            {
                _readyUpdate = update;
                WriteLog($"新しいバージョンをダウンロード: {version}");
                _tray!.ShowUpdate(version);
                _tray.Notify($"バージョン {version} に更新できます", "タスクトレイのメニューの「更新して再起動」から更新できます");
            }
            return $"バージョン {version} をダウンロードしました。タスクトレイのメニューの「更新して再起動」から更新できます";
        }
        catch (Exception ex)
        {
            WriteLog($"更新の確認に失敗: {ex.Message}");
            return $"更新を確認できませんでした: {ex.Message}";
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    private async Task ApplyUpdateAsync()
    {
        if (_readyUpdate is not { } update) return;
        if (_host?.CurrentSession is not null)
        {
            MessageBox.Show("録画中は更新できません。試合が終わってからもう一度お試しください。", "ValoCapture");
            return;
        }
        WriteLog($"バージョン {update.Version} に更新して再起動");
        await ExitAsync(update);
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
                EnforceStorageLimit();
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

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, CheckForUpdatesAsync);
        if (_window!.IsVisible) _settingsWindow.Owner = _window;
        else _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var saved = _settingsWindow.ShowDialog() == true;
        _settingsWindow = null;
        if (!saved) return;

        _settings.Save();
        _host!.ApplySettings(_settings);
        // 一覧の読み直しと保存先の反映（OnViewModelPropertyChanged）
        _vm!.RecordingsRoot = _settings.RecordingsRoot;
        UpdateStatus();
        WriteLog($"設定を変更: 録画先={_settings.RecordingsRoot} 対象={string.Join(",", _settings.TargetQueues)} 出力={_settings.OutputDeviceId ?? "既定"} " +
                 $"マイク={(_settings.CaptureMicrophone ? _settings.InputDeviceId ?? "既定" : "なし")} 上限={_settings.StorageLimitGb}GB");
        EnforceStorageLimit();
    }

    /// <summary>
    /// 録画の合計が上限を超えていれば、古い試合から削除する。
    /// 録画中・イベント取得中の試合と、ビューアで開いている試合は残す。
    /// </summary>
    private void EnforceStorageLimit()
    {
        if (_settings.StorageLimitGb <= 0) return;
        // 前回の削除がまだ終わっていなければ重ねて走らせない
        if (Interlocked.Exchange(ref _cleanupRunning, 1) == 1) return;

        var root = _settings.RecordingsRoot;
        var maxBytes = _settings.StorageLimitGb * RecordingStorage.BytesPerGb;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_host?.CurrentSession?.Directory is { } recording) keep.Add(recording);
        if (_vm?.SelectedSession?.Session.Directory is { } viewing) keep.Add(viewing);
        // ごみ箱への移動を待っている試合（「元に戻す」で戻せるように残す）
        if (_vm?.PendingDeleteDirectory is { } pending) keep.Add(pending);
        var now = DateTimeOffset.UtcNow;

        Task.Run(() =>
        {
            try
            {
                return RecordingStorage.EnforceLimit(root, maxBytes, s => !keep.Contains(s.Directory) && !s.IsBusy(now));
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
            }
        }).ContinueWith(t =>
        {
            if (t.Exception is { } ex)
            {
                WriteLog($"容量制限による削除に失敗: {ex.InnerException?.Message}");
                return;
            }
            if (t.Result.Count == 0) return;

            foreach (var s in t.Result) WriteLog($"容量制限により削除: {s.Directory}");
            _vm!.Reload();
            _tray!.Notify("古い録画を削除しました", $"容量の上限（{_settings.StorageLimitGb} GB）を超えたため {t.Result.Count} 試合を削除しました");
        }, TaskScheduler.FromCurrentSynchronizationContext());
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

    /// <param name="update">指定すると、終了後にこの版へ更新して起動し直す。</param>
    private async Task ExitAsync(Velopack.VelopackAsset? update = null)
    {
        if (_exiting) return;
        if (update is null && _host?.CurrentSession is not null &&
            MessageBox.Show("録画中です。録画を停止して終了しますか？", "ValoCapture", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        _exiting = true;
        _statusTimer.Stop();
        _updateTimer.Stop();
        if (_vm is not null) await _vm.CommitPendingDeleteAsync();
        if (_host is not null) await _host.DisposeAsync();
        _tray?.Dispose();
        _window?.Close();
        WriteLog("終了");
        // 更新プログラムはこのプロセスの終了を待ってから入れ替えるので、録画の後始末が済んでから起動する
        if (update is not null)
        {
            try { _updater.ApplyAfterExit(update); }
            catch (Exception ex) { WriteLog($"更新を開始できません: {ex.Message}"); }
        }
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
