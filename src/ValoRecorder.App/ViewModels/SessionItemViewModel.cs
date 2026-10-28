using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ValoRecorder.Core.Game;
using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.ViewModels;

/// <summary>試合一覧の 1 行。</summary>
public sealed partial class SessionItemViewModel : ObservableObject
{
    public SessionItemViewModel(MatchSession session)
    {
        _session = session;
        Timeline = session.LoadTimeline();
    }

    private MatchSession _session;

    public MatchSession Session => _session;

    /// <summary>保存し直されたセッションを反映する（同じフォルダの新しい内容）。</summary>
    public void Refresh(MatchSession session)
    {
        _session = session;
        Timeline = session.LoadTimeline();
        OnPropertyChanged(string.Empty);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Score))]
    private MatchTimeline? _timeline;

    public string MapName => DisplayNames.Map(Session.MapId);
    public string QueueName => DisplayNames.Queue(Session.QueueId);
    public string DateText => Session.DetectedAt.ToLocalTime().ToString("M/d HH:mm");

    public string DurationText =>
        Session.RecordStartedAt is { } start && Session.RecordStoppedAt is { } stop
            ? (stop - start).ToString(@"m\:ss")
            : "";

    public string Score => Timeline is { } t ? $"{t.MyKills} / {t.MyDeaths} / {t.MyAssists}" : StatusText;

    public string StatusText => Session.Status switch
    {
        SessionStatus.Recording => "録画中",
        SessionStatus.FetchingDetails => "試合詳細の取得待ち",
        SessionStatus.Failed => "イベント取得失敗",
        _ => "",
    };

    public string VideoPath => Session.VideoPath;
    public bool HasVideo => File.Exists(VideoPath);
}
