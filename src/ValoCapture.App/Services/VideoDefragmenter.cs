using System.Diagnostics;
using System.IO;
using ValoCapture.Core.Recording;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.Services;

/// <summary>
/// 録画（断片化 MP4）を通常の MP4 に変換して置き換える。断片化 MP4 のままだと奥へのシークで違う場面が出る
/// （<see cref="Mp4Defragmenter"/>）。1 本ずつ順にバックグラウンドで変換し、変換中も元の動画は再生できる。
/// </summary>
public sealed class VideoDefragmenter
{
    /// <summary>変換中の書き出し先。途中で終了して残ったものは次の変換の前に消す。</summary>
    private const string TempFile = "video.mp4.converting";

    /// <summary>変換後のファイルと同じだけの空きに加えて残しておく容量。</summary>
    private const long ReservedBytes = 1024L * 1024 * 1024;

    private readonly Func<string, Func<Task>, Task> _replaceVideo;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _converting;

    /// <param name="replaceVideo">
    /// 試合フォルダと置き換えの処理を受け取り、UI スレッドで置き換えを実行する
    /// （ビューアで開いている動画なら、いったん閉じてから置き換えて開き直す）。
    /// </param>
    public VideoDefragmenter(Func<string, Func<Task>, Task> replaceVideo) => _replaceVideo = replaceVideo;

    public event Action<string>? Log;

    /// <summary>変換中の試合フォルダ（容量制限で削除しないように）。</summary>
    public string? ConvertingDirectory => _converting;

    /// <summary>試合の動画が断片化 MP4 なら変換する。同じ試合が待っていれば重ねない。</summary>
    public void Enqueue(MatchSession session)
    {
        lock (_queued)
            if (!_queued.Add(session.Directory)) return;

        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try
            {
                _converting = session.Directory;
                await ConvertAsync(session);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"動画の変換に失敗: {session.Directory} {ex.Message}");
            }
            finally
            {
                _converting = null;
                _gate.Release();
                lock (_queued) _queued.Remove(session.Directory);
            }
        });
    }

    private async Task ConvertAsync(MatchSession session)
    {
        var video = session.VideoPath;
        var temp = Path.Combine(session.Directory, TempFile);
        if (File.Exists(temp)) File.Delete(temp);
        if (!File.Exists(video) || !Mp4Defragmenter.IsFragmented(video)) return;

        var size = new FileInfo(video).Length;
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(video))!).AvailableFreeSpace < size + ReservedBytes)
        {
            Log?.Invoke($"空き容量が足りないため動画を変換しません: {session.Directory}");
            return;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            Mp4Defragmenter.Defragment(video, temp);
            await _replaceVideo(session.Directory, () => ReplaceAsync(temp, video));
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
        Log?.Invoke($"動画を通常の MP4 に変換: {session.Directory}（{watch.Elapsed:mm\\:ss}）");
    }

    // 再生を閉じた直後は再生エンジンがまだファイルを開いていることがあるので、少し待って試し直す
    private static async Task ReplaceAsync(string temp, string video)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, video, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
            {
                await Task.Delay(500);
            }
        }
    }
}
