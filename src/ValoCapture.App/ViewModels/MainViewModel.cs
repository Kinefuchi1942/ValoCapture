using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ValoCapture.App.Services;
using ValoCapture.Core.Recording;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>イベントへジャンプするとき、何秒前から再生するか。</summary>
    public static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(3);

    /// <summary>この間隔以内にデスを挟まず続いたキルを「連続キル」とする。</summary>
    private const long StreakGapMs = 6_000;

    /// <summary>イベントからクリップを作るとき、何秒前から何秒後までにするか。</summary>
    private static readonly TimeSpan EventClipBefore = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EventClipAfter = TimeSpan.FromSeconds(3);

    public MainViewModel(string recordingsRoot, double volume)
    {
        _recordingsRoot = recordingsRoot;
        _volume = volume;
        _toastTimer.Tick += (_, _) => _ = CommitPendingDeleteAsync();
        Reload();
    }

    /// <summary>動画の再生位置を変えてほしいとき（View が MediaElement に反映する）。</summary>
    public event Action<TimeSpan>? SeekRequested;

    /// <summary>設定画面を開いてほしいとき（App が開く）。</summary>
    public event Action? SettingsRequested;

    /// <summary>試合をごみ箱へ移したとき（失敗したときは例外付き）。</summary>
    public event Action<MatchSession, Exception?>? SessionDeleted;

    /// <summary>クリップを書き出したとき（クリップのパス。失敗したときは例外付き）。</summary>
    public event Action<string, Exception?>? ClipSaved;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    /// <summary>フィルター適用後のイベント（一覧とタイムラインの両方に使う）。</summary>
    public ObservableCollection<EventItemViewModel> Events { get; } = [];

    [ObservableProperty] private string _recordingsRoot;
    [ObservableProperty] private SessionItemViewModel? _selectedSession;
    [ObservableProperty] private EventItemViewModel? _selectedEvent;
    [ObservableProperty] private bool _showKills = true;
    [ObservableProperty] private bool _showDeaths = true;
    [ObservableProperty] private bool _showAssists;
    [ObservableProperty] private bool _showRoundStarts = true;
    [ObservableProperty] private string _offsetText = "";

    /// <summary>ラウンド制のモード（ランク等）か。デスマッチ等ではラウンドの絞り込みを出さない。</summary>
    [ObservableProperty] private bool _isRoundBased;

    /// <summary>音量（0〜1）。</summary>
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _isMuted;

    // 録画の状態（左上のカード）
    [ObservableProperty] private string _statusTitle = "";
    [ObservableProperty] private string _statusTime = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _isRecording;

    // 削除
    /// <summary>削除を確認している試合（確認ダイアログを出す）。</summary>
    [ObservableProperty] private SessionItemViewModel? _deleteCandidate;
    /// <summary>確認ダイアログに出すモード・日時・長さ・容量。</summary>
    [ObservableProperty] private string _deleteCandidateDetail = "";
    /// <summary>画面下の通知。空なら出さない。</summary>
    [ObservableProperty] private string _toastText = "";
    /// <summary>通知の右のボタン（「元に戻す」など）。空なら出さない。</summary>
    [ObservableProperty] private string _toastActionText = "";
    private Action? _toastAction;

    /// <summary>削除した直後の試合。通知を出している間は「元に戻す」で戻せるよう、ごみ箱へ移すのを待つ。</summary>
    private SessionItemViewModel? _pendingDelete;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    public string? PendingDeleteDirectory => _pendingDelete?.Session.Directory;

    // クリップ
    /// <summary>クリップの範囲（動画内の時刻）。I / O キーかタイムラインの下のボタンで決める。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipRangeText))]
    [NotifyCanExecuteChangedFor(nameof(SaveClipCommand), nameof(ClearClipCommand))]
    private TimeSpan? _clipStart;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipRangeText))]
    [NotifyCanExecuteChangedFor(nameof(SaveClipCommand), nameof(ClearClipCommand))]
    private TimeSpan? _clipEnd;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveClipCommand), nameof(SaveEventClipCommand))]
    private bool _isSavingClip;

    /// <summary>「01:23 – 01:41（18 秒）」。決めていない側は「--:--」。</summary>
    public string ClipRangeText =>
        ClipStart is null && ClipEnd is null ? "範囲を決めていません"
        : $"{ClipTime(ClipStart)} – {ClipTime(ClipEnd)}" +
          (ClipStart is { } s && ClipEnd is { } e ? $"（{(e - s).TotalSeconds:0} 秒）" : "");

    private static string ClipTime(TimeSpan? t) => t is { } v ? v.ToString(@"mm\:ss") : "--:--";

    partial void OnSelectedSessionChanged(SessionItemViewModel? value)
    {
        RequestDeleteCommand.NotifyCanExecuteChanged();
        ClipStart = ClipEnd = null;
        RefreshEvents();
    }
    partial void OnShowKillsChanged(bool value) => RefreshEvents();
    partial void OnShowDeathsChanged(bool value) => RefreshEvents();
    partial void OnShowAssistsChanged(bool value) => RefreshEvents();
    partial void OnShowRoundStartsChanged(bool value) => RefreshEvents();
    partial void OnRecordingsRootChanged(string value) => Reload();
    partial void OnVolumeChanged(double value) { if (value > 0) IsMuted = false; }

    /// <summary>
    /// 録画フォルダを読み直す。既存の行は同じインスタンスのまま中身だけ更新するので、
    /// 再生中の動画は止まらない。
    /// </summary>
    [RelayCommand]
    public void Reload()
    {
        var loaded = MatchSession.LoadAll(RecordingsRoot).Where(s => s.Directory != PendingDeleteDirectory).ToList();
        var existing = Sessions.ToDictionary(s => s.Session.Directory);

        for (var i = Sessions.Count - 1; i >= 0; i--)
            if (!loaded.Any(s => s.Directory == Sessions[i].Session.Directory)) Sessions.RemoveAt(i);

        for (var i = 0; i < loaded.Count; i++)
        {
            if (existing.TryGetValue(loaded[i].Directory, out var item))
            {
                item.Refresh(loaded[i]);
                var current = Sessions.IndexOf(item);
                if (current != i) Sessions.Move(current, i);
            }
            else
            {
                Sessions.Insert(i, new SessionItemViewModel(loaded[i]));
            }
        }

        if (SelectedSession is null || !Sessions.Contains(SelectedSession))
            SelectedSession = Sessions.FirstOrDefault();
        else
            RefreshEvents();
        // 録画が終わるなどして削除できるようになったかもしれない
        RequestDeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void RequestDelete()
    {
        if (!CanDelete() || SelectedSession is not { } item) return;
        var size = RecordingStorage.GetSize(item.Session);
        var sizeText = size >= RecordingStorage.BytesPerGb
            ? $"{(double)size / RecordingStorage.BytesPerGb:0.0} GB"
            : $"{size / (1024 * 1024)} MB";
        DeleteCandidateDetail = string.Join(" · ", new[] { item.QueueName, item.DateText, item.DurationText, sizeText }.Where(s => s.Length > 0));
        DeleteCandidate = item;
    }

    private bool CanDelete() => SelectedSession is { } s && !s.Session.IsBusy(DateTimeOffset.UtcNow);

    [RelayCommand]
    private void CancelDelete() => DeleteCandidate = null;

    /// <summary>一覧から消し、通知を出す。ごみ箱へ移すのは通知が消えてから。</summary>
    [RelayCommand]
    private async Task ConfirmDelete()
    {
        if (DeleteCandidate is not { } item) return;
        DeleteCandidate = null;
        // 前に削除した試合を先にごみ箱へ移す
        await CommitPendingDeleteAsync();

        _pendingDelete = item;
        // 消した試合の次（なければ前）を選ぶ。選択を外すと再生中の動画が閉じられ、ファイルを移せるようになる
        var index = Sessions.IndexOf(item);
        SelectedSession = Sessions.ElementAtOrDefault(index + 1) ?? Sessions.ElementAtOrDefault(index - 1);
        Sessions.Remove(item);
        ShowToast($"「{item.MapName} {item.DateText}」をごみ箱に移動しました", "元に戻す", UndoDelete);
    }

    private void UndoDelete()
    {
        HideToast();
        if (_pendingDelete is not { } item) return;
        _pendingDelete = null;
        Reload();
        SelectedSession = Sessions.FirstOrDefault(s => s.Session.Directory == item.Session.Directory) ?? SelectedSession;
    }

    /// <summary>削除を待っている試合をごみ箱へ移す（通知が消えたときと終了するとき）。</summary>
    public async Task CommitPendingDeleteAsync()
    {
        HideToast();
        if (_pendingDelete is not { } item) return;
        _pendingDelete = null;

        try
        {
            await Task.Run(() => RecycleBin.MoveSession(item.Session));
            SessionDeleted?.Invoke(item.Session, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            SessionDeleted?.Invoke(item.Session, ex);
            Reload();
            ShowToast($"「{item.MapName} {item.DateText}」を削除できませんでした: {ex.Message}");
        }
    }

    private void ShowToast(string text, string actionText = "", Action? action = null)
    {
        ToastText = text;
        ToastActionText = action is null ? "" : actionText;
        _toastAction = action;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        ToastText = "";
        ToastActionText = "";
        _toastAction = null;
    }

    [RelayCommand]
    private void ToastAction() => _toastAction?.Invoke();

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedSession is not { } s) return;
        if (s.HasVideo) ShowInExplorer(s.VideoPath);
        else Process.Start(new ProcessStartInfo("explorer.exe", $"\"{s.Session.Directory}\"") { UseShellExecute = true });
    }

    private static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    /// <summary>クリップの始まりを決める。終わりより後なら終わりを決め直してもらう。</summary>
    public void SetClipStart(TimeSpan position)
    {
        if (SelectedSession?.HasVideo != true) return;
        ClipStart = position;
        if (ClipEnd <= position) ClipEnd = null;
    }

    /// <summary>クリップの終わりを決める。始まりより前なら始まりを決め直してもらう。</summary>
    public void SetClipEnd(TimeSpan position)
    {
        if (SelectedSession?.HasVideo != true) return;
        ClipEnd = position;
        if (ClipStart >= position) ClipStart = null;
    }

    [RelayCommand(CanExecute = nameof(HasClipRange))]
    private void ClearClip() => ClipStart = ClipEnd = null;

    private bool HasClipRange() => ClipStart is not null || ClipEnd is not null;

    [RelayCommand(CanExecute = nameof(CanSaveClip))]
    private Task SaveClip() => SaveClipAsync(ClipStart!.Value, ClipEnd!.Value);

    private bool CanSaveClip() => !IsSavingClip && ClipStart is { } s && ClipEnd is { } e && e > s;

    /// <summary>イベントの少し前から少し後までをクリップにする。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveEventClip))]
    private Task SaveEventClip(EventItemViewModel? item) =>
        item is null ? Task.CompletedTask : SaveClipAsync(item.VideoTime - EventClipBefore, item.VideoTime + EventClipAfter);

    private bool CanSaveEventClip(EventItemViewModel? _) => !IsSavingClip;

    /// <summary>選んでいる試合の動画から範囲を切り出し、録画フォルダの clips に書く。</summary>
    private async Task SaveClipAsync(TimeSpan start, TimeSpan end)
    {
        if (IsSavingClip || SelectedSession is not { HasVideo: true } item) return;
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;

        IsSavingClip = true;
        // 前に削除した試合を先にごみ箱へ移す（通知を置き換えると「元に戻す」が押せなくなる）
        await CommitPendingDeleteAsync();
        ShowToast("クリップを書き出しています…");

        var path = ClipStorage.NewPath(RecordingsRoot, item.Session, start, end);
        try
        {
            var range = await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return Mp4Remuxer.Clip(item.VideoPath, path, start, end);
            });
            ClipSaved?.Invoke(path, null);
            ShowToast($"クリップを保存しました（{(range.End - range.Start).TotalSeconds:0} 秒）", "表示", () => ShowInExplorer(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            ClipSaved?.Invoke(path, ex);
            ShowToast($"クリップを保存できませんでした: {ex.Message}");
        }
        finally
        {
            IsSavingClip = false;
        }
    }

    [RelayCommand]
    private void JumpTo(EventItemViewModel? item)
    {
        if (item is null) return;
        SelectedEvent = item;
        var t = item.VideoTime - PreRoll;
        SeekRequested?.Invoke(t < TimeSpan.Zero ? TimeSpan.Zero : t);
    }

    /// <summary>時刻補正を変えてタイムラインを作り直す（ms 単位の差分）。</summary>
    [RelayCommand(CanExecute = nameof(CanAdjustOffset))]
    private void AdjustOffset(string deltaMs)
    {
        var item = SelectedSession!;
        item.Timeline = item.Session.RebuildTimeline(item.Session.OffsetMs + long.Parse(deltaMs));
        RefreshEvents();
    }

    private bool CanAdjustOffset(string _) => SelectedSession?.Session.HasMatchDetails == true;

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    /// <summary>現在位置より後の最初のイベント（なければ null）。</summary>
    public EventItemViewModel? NextEvent(TimeSpan position) =>
        Events.FirstOrDefault(e => e.VideoTime - PreRoll > position + TimeSpan.FromSeconds(0.5));

    /// <summary>現在位置より前のイベント（直前に飛んだものは飛ばす）。</summary>
    public EventItemViewModel? PreviousEvent(TimeSpan position) =>
        Events.LastOrDefault(e => e.VideoTime - PreRoll < position - TimeSpan.FromSeconds(1));

    private void RefreshEvents()
    {
        Events.Clear();
        SelectedEvent = null;
        AdjustOffsetCommand.NotifyCanExecuteChanged();

        if (SelectedSession?.Timeline is not { } timeline)
        {
            OffsetText = "";
            IsRoundBased = false;
            return;
        }

        OffsetText = $"{SelectedSession.Session.OffsetMs / 1000.0:+0.0;-0.0} 秒";
        var isRoundBased = IsRoundBased = timeline.Events.Select(e => e.Round).Distinct().Count() > 1;
        var streaks = FindStreaks(timeline.Events);

        foreach (var e in timeline.Events)
        {
            if (e.VideoTimeMs is null) continue;
            var visible = e.Type switch
            {
                MatchEventType.Kill => ShowKills,
                MatchEventType.Death => ShowDeaths,
                MatchEventType.Assist => ShowAssists,
                // デスマッチ等の 1 ラウンドだけのモードでは意味がないので出さない
                MatchEventType.RoundStart => ShowRoundStarts && isRoundBased,
                _ => false,
            };
            if (visible) Events.Add(new EventItemViewModel(e, isRoundBased, streaks.GetValueOrDefault(e)));
        }
    }

    /// <summary>連続キルの先頭のキル → 連続数。</summary>
    private static Dictionary<MatchEvent, int> FindStreaks(IReadOnlyList<MatchEvent> events)
    {
        var result = new Dictionary<MatchEvent, int>();
        MatchEvent? head = null;
        MatchEvent? last = null;
        var count = 0;

        foreach (var e in events.Where(e => e.Type is MatchEventType.Kill or MatchEventType.Death))
        {
            if (e.Type == MatchEventType.Kill && last is { Type: MatchEventType.Kill } && e.GameTimeMs - last.GameTimeMs <= StreakGapMs)
            {
                count++;
            }
            else
            {
                if (head is not null && count >= 2) result[head] = count;
                head = e.Type == MatchEventType.Kill ? e : null;
                count = e.Type == MatchEventType.Kill ? 1 : 0;
            }
            last = e;
        }
        if (head is not null && count >= 2) result[head] = count;
        return result;
    }
}
