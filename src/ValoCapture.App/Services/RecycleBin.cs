using System.IO;
using Microsoft.VisualBasic.FileIO;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.Services;

/// <summary>試合フォルダをごみ箱へ移す（手動の削除。ごみ箱から元に戻せる）。</summary>
public static class RecycleBin
{
    /// <exception cref="IOException">動画が使用中などで移せなかった。</exception>
    public static void MoveSession(MatchSession session)
    {
        // 動画が使用中だと動画だけ残ってフォルダの一部がごみ箱に入るので、先に開けるか確かめる
        if (File.Exists(session.VideoPath))
            using (File.Open(session.VideoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        FileSystem.DeleteDirectory(session.Directory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}
