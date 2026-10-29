namespace ValoCapture.Core.Sessions;

/// <summary>録画フォルダの容量の集計と、容量制限による古い試合の削除。</summary>
public static class RecordingStorage
{
    public const long BytesPerGb = 1024L * 1024 * 1024;

    /// <summary>試合フォルダの合計サイズ（バイト）。</summary>
    public static long GetSize(MatchSession session) =>
        Directory.Exists(session.Directory)
            ? new DirectoryInfo(session.Directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

    /// <summary>録画フォルダ配下の全試合の合計サイズ（バイト）。試合フォルダ以外のファイルは数えない。</summary>
    public static long GetTotalSize(string root) => MatchSession.LoadAll(root).Sum(GetSize);

    /// <summary>
    /// 合計サイズが上限以下になるまで、古い試合から順に試合フォルダごと削除する。
    /// ブックマークした試合と <paramref name="canDelete"/> が false の試合は残す。
    /// 削除できなかった試合（再生中で使用中など）は飛ばして次に進む。
    /// </summary>
    /// <returns>削除した試合。</returns>
    public static IReadOnlyList<MatchSession> EnforceLimit(string root, long maxBytes, Func<MatchSession, bool> canDelete)
    {
        var sessions = MatchSession.LoadAll(root);
        var sizes = sessions.ToDictionary(s => s, GetSize);
        var total = sizes.Values.Sum();
        var deleted = new List<MatchSession>();

        // LoadAll は新しい順
        foreach (var session in sessions.Reverse())
        {
            if (total <= maxBytes) break;
            if (session.IsBookmarked || !canDelete(session)) continue;
            if (!TryDelete(session)) continue;

            total -= sizes[session];
            deleted.Add(session);
        }
        return deleted;
    }

    private static bool TryDelete(MatchSession session)
    {
        try
        {
            // 動画を先に消す。使用中で消せなければ session.json を残したまま諦める
            // （先にフォルダごと消そうとすると、session.json だけ消えて一覧に出ない動画が残ることがある）
            File.Delete(session.VideoPath);
            Directory.Delete(session.Directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
