using ValoCapture.Core.Game;

namespace ValoCapture.Core.Sessions;

/// <summary>
/// 試合の録画から切り出したクリップの保存先。録画フォルダの clips に置く
/// （session.json がないので試合の一覧には出ず、容量制限で削除されない）。
/// </summary>
public static class ClipStorage
{
    public const string DirectoryName = "clips";

    public static string GetDirectory(string recordingsRoot) => Path.Combine(recordingsRoot, DirectoryName);

    /// <summary>
    /// 新しいクリップのパス。「アセント_20261031-2130_0123-0141.mp4」（マップ・試合の日時・動画内の範囲）。
    /// 同じ名前があれば「_2」「_3」… を付ける。
    /// </summary>
    public static string NewPath(string recordingsRoot, MatchSession session, TimeSpan start, TimeSpan end)
    {
        var map = string.Concat(DisplayNames.Map(session.MapId).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var name = $"{map}_{session.DetectedAt.ToLocalTime():yyyyMMdd-HHmm}_{Time(start)}-{Time(end)}";
        var directory = GetDirectory(recordingsRoot);

        var path = Path.Combine(directory, name + ".mp4");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{name}_{i}.mp4");
        return path;
    }

    // 1 時間を超えても分を繰り上げない（「6512」= 65 分 12 秒）
    private static string Time(TimeSpan t) => $"{(int)Math.Max(0, t.TotalMinutes):00}{Math.Max(0, t.Seconds):00}";
}
