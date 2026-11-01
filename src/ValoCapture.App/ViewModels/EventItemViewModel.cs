using CommunityToolkit.Mvvm.ComponentModel;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.ViewModels;

/// <summary>イベント一覧・タイムライン上の 1 イベント（動画内にあるものだけ）。</summary>
/// <param name="streak">連続キルの先頭なら連続数（2 以上）。</param>
public sealed partial class EventItemViewModel(MatchEvent e, bool isRoundBased, int streak = 0) : ObservableObject
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

    // 再生位置との関係（一覧の左のバー・進みのバー・薄い表示に使う）
    /// <summary>再生位置の直前に起きたイベント（今再生しているところ）か。</summary>
    [ObservableProperty] private bool _isCurrent;
    /// <summary>再生位置より前に過ぎたイベント（再生中のものは除く）か。</summary>
    [ObservableProperty] private bool _isPast;
    /// <summary>再生中のイベントから次のイベント（なければ動画の終わり）までの進み（0〜1）。</summary>
    [ObservableProperty] private double _progress;
}
