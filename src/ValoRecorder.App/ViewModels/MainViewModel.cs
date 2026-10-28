using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>イベントへジャンプするとき、何秒前から再生するか。</summary>
    public static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(3);

    public MainViewModel(string recordingsRoot)
    {
        _recordingsRoot = recordingsRoot;
        Reload();
    }

    /// <summary>動画の再生位置を変えてほしいとき（View が MediaElement に反映する）。</summary>
    public event Action<TimeSpan>? SeekRequested;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    /// <summary>フィルター適用後のイベント（一覧とタイムラインの両方に使う）。</summary>
    public ObservableCollection<EventItemViewModel> Events { get; } = [];

    [ObservableProperty] private string _recordingsRoot;
    [ObservableProperty] private SessionItemViewModel? _selectedSession;
    [ObservableProperty] private bool _showKills = true;
    [ObservableProperty] private bool _showDeaths = true;
    [ObservableProperty] private bool _showAssists;
    [ObservableProperty] private bool _showRoundStarts = true;
    [ObservableProperty] private string _offsetText = "";

    partial void OnSelectedSessionChanged(SessionItemViewModel? value) => RefreshEvents();
    partial void OnShowKillsChanged(bool value) => RefreshEvents();
    partial void OnShowDeathsChanged(bool value) => RefreshEvents();
    partial void OnShowAssistsChanged(bool value) => RefreshEvents();
    partial void OnShowRoundStartsChanged(bool value) => RefreshEvents();
    partial void OnRecordingsRootChanged(string value) => Reload();

    /// <summary>録画の状態（例: 「● 録画中 サンセット 03:12」）。</summary>
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _targetText = "";

    /// <summary>
    /// 録画フォルダを読み直す。既存の行は同じインスタンスのまま中身だけ更新するので、
    /// 再生中の動画は止まらない。
    /// </summary>
    [RelayCommand]
    public void Reload()
    {
        var loaded = MatchSession.LoadAll(RecordingsRoot);
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
    }

    [RelayCommand]
    private void JumpTo(EventItemViewModel? item)
    {
        if (item is null) return;
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

    /// <summary>現在位置より後の最初のイベント（なければ null）。</summary>
    public EventItemViewModel? NextEvent(TimeSpan position) =>
        Events.FirstOrDefault(e => e.VideoTime - PreRoll > position + TimeSpan.FromSeconds(0.5));

    /// <summary>現在位置より前のイベント（直前に飛んだものは飛ばす）。</summary>
    public EventItemViewModel? PreviousEvent(TimeSpan position) =>
        Events.LastOrDefault(e => e.VideoTime - PreRoll < position - TimeSpan.FromSeconds(1));

    private void RefreshEvents()
    {
        Events.Clear();
        AdjustOffsetCommand.NotifyCanExecuteChanged();

        if (SelectedSession?.Timeline is not { } timeline)
        {
            OffsetText = "";
            return;
        }

        OffsetText = $"補正 {SelectedSession.Session.OffsetMs / 1000.0:+0.0;-0.0}秒";
        var isRoundBased = timeline.Events.Select(e => e.Round).Distinct().Count() > 1;
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
            if (visible) Events.Add(new EventItemViewModel(e, isRoundBased));
        }
    }
}
