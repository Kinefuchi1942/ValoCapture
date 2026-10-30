using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ValoCapture.Core.Game;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.ViewModels;

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
    [NotifyPropertyChangedFor(nameof(HasScore), nameof(StripEvents), nameof(Result), nameof(ResultText))]
    private MatchTimeline? _timeline;

    partial void OnTimelineChanged(MatchTimeline? value) => _result = value?.Result ?? ReadResultFromMatchFile();

    private MatchResult? _result;

    /// <summary>勝敗。判定できなければ null。</summary>
    public MatchResult? Result => _result;

    /// <summary>一覧でモード名の前に付ける「勝利 13-7 」。後ろの空白はモード名との区切り。</summary>
    public string ResultText => Result is { } r
        ? $"{r.Outcome switch { MatchOutcome.Win => "勝利", MatchOutcome.Loss => "敗北", _ => "引分" }} {r.MyScore}-{r.OpponentScore} "
        : "";

    // 勝敗を記録する前に作った timeline.json には Result がないので match.json から読む
    private MatchResult? ReadResultFromMatchFile()
    {
        if (Timeline is null || Session.Puuid is not { } puuid || !Session.HasMatchDetails) return null;
        try { return MatchTimelineBuilder.ReadResult(File.ReadAllText(Path.Combine(Session.Directory, MatchSession.MatchFile)), puuid); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { return null; }
    }

    public string MapName => DisplayNames.Map(Session.MapId);
    public string QueueName => DisplayNames.Queue(Session.QueueId);
    public string DateText => Session.DetectedAt.ToLocalTime().ToString("M/d HH:mm");

    /// <summary>録画の長さ（録画中・不明なら 0）。</summary>
    public TimeSpan Duration =>
        Session.RecordStartedAt is { } start && Session.RecordStoppedAt is { } stop ? stop - start : TimeSpan.Zero;

    public string DurationText => Duration > TimeSpan.Zero ? Duration.ToString(@"m\:ss") : "";

    public bool HasScore => Timeline is not null;

    /// <summary>ミニタイムラインに描くキルとデス。</summary>
    public IReadOnlyList<MatchEvent> StripEvents =>
        Timeline?.Events.Where(e => e.VideoTimeMs is not null && e.Type is MatchEventType.Kill or MatchEventType.Death).ToList()
        ?? [];

    public string StatusText => Session.Status switch
    {
        SessionStatus.Recording => "録画中",
        SessionStatus.FetchingDetails => "イベント取得待ち",
        SessionStatus.Failed => "イベント取得失敗",
        _ => "",
    };

    public string VideoPath => Session.VideoPath;
    public bool HasVideo => File.Exists(VideoPath);

    /// <summary>ブックマークした試合は容量制限で自動削除されない。</summary>
    public bool IsBookmarked => Session.IsBookmarked;

    [RelayCommand]
    private void ToggleBookmark()
    {
        Session.SetBookmarked(!IsBookmarked);
        OnPropertyChanged(nameof(IsBookmarked));
    }
}
