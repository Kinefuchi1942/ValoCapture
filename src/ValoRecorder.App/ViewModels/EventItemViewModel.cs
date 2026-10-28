using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.ViewModels;

/// <summary>イベント一覧・タイムライン上の 1 イベント（動画内にあるものだけ）。</summary>
public sealed class EventItemViewModel(MatchEvent e, bool isRoundBased)
{
    public MatchEvent Event { get; } = e;
    public MatchEventType Type => Event.Type;
    public TimeSpan VideoTime { get; } = TimeSpan.FromMilliseconds(e.VideoTimeMs ?? 0);

    public string TimeText => VideoTime.ToString(@"mm\:ss");

    public string Label => Type switch
    {
        MatchEventType.Kill => "キル",
        MatchEventType.Death => "デス",
        MatchEventType.Assist => "アシスト",
        MatchEventType.RoundStart => $"ラウンド {Event.Round + 1} 開始",
        _ => Type.ToString(),
    };

    public string RoundText => isRoundBased && Type != MatchEventType.RoundStart ? $"R{Event.Round + 1}" : "";
}
