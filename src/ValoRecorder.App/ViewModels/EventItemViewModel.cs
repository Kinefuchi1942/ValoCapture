using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.ViewModels;

/// <summary>イベント一覧・タイムライン上の 1 イベント（動画内にあるものだけ）。</summary>
/// <param name="streak">連続キルの先頭なら連続数（2 以上）。</param>
public sealed class EventItemViewModel(MatchEvent e, bool isRoundBased, int streak = 0)
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

    /// <summary>右端の補足（連続キル、ラウンド番号）。</summary>
    public string Note =>
        streak >= 2 ? $"{streak}連続"
        : isRoundBased && Type != MatchEventType.RoundStart ? $"R{Event.Round + 1}"
        : "";

    public bool IsStreak => streak >= 2;
}
