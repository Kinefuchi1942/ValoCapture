using System.IO;
using ValoCapture.Core.Recording;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace ValoCapture.App.Services;

/// <summary>
/// 録画の一部を、Discord のアップロード上限に収まるよう解像度とビットレートを下げて書き出す
/// （Windows 標準の MediaTranscoder。使える GPU があればハードウェアでエンコードする）。
/// 再エンコードするので、範囲は指定したとおりになる（キーフレームに丸めない）。
/// </summary>
public static class DiscordClipExporter
{
    /// <summary>上限を超えたときにビットレートを下げて書き直す回数。</summary>
    private const int MaxRetries = 2;

    /// <summary>上限に収まらない長さ・設定で書き出そうとしたとき。</summary>
    public sealed class TooLongException(TimeSpan max) : Exception($"長すぎます（{max.TotalSeconds:0} 秒まで）")
    {
        public TimeSpan Max { get; } = max;
    }

    /// <summary>書き出して、実際のサイズ（バイト）を返す。</summary>
    /// <param name="progress">0〜1 の進み具合（UI スレッドとは限らない）。</param>
    /// <exception cref="TooLongException">上限に収まらない長さ。</exception>
    /// <exception cref="IOException">書き出せなかった、または書き直しても上限に収まらなかった。</exception>
    public static async Task<long> ExportAsync(string sourcePath, string destinationPath, TimeSpan start, TimeSpan end,
        long limitBytes, IProgress<double>? progress = null)
    {
        var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(sourcePath));
        var sourceProfile = await MediaEncodingProfile.CreateFromFileAsync(source);
        var video = sourceProfile.Video ?? throw new IOException("動画に映像がありません");
        // 動画の終わり近くのイベントでは、終わりが動画の長さを超えることがある
        var sourceDuration = (await source.Properties.GetVideoPropertiesAsync()).Duration;
        if (sourceDuration > TimeSpan.Zero && end > sourceDuration) end = sourceDuration;
        var duration = end - start;
        if (duration <= TimeSpan.Zero) throw new IOException("範囲が動画の外です");

        var sourceFramerate = video.FrameRate.Denominator > 0 ? (int)Math.Round((double)video.FrameRate.Numerator / video.FrameRate.Denominator) : 60;
        var sourceKbps = (int)((video.Bitrate + (sourceProfile.Audio?.Bitrate ?? 0)) / 1000);
        var plan = DiscordEncodePlan.Create(limitBytes, duration, (int)video.Width, (int)video.Height, sourceFramerate, sourceKbps)
            ?? throw new TooLongException(DiscordEncodePlan.MaxDuration(limitBytes));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        for (var attempt = 0; ; attempt++)
        {
            await TranscodeAsync(source, destinationPath, start, end, plan, sourceProfile.Audio is not null,
                progress is null ? null : new Progress<double>(p => progress.Report((attempt + p) / (attempt + 1))));
            var size = new FileInfo(destinationPath).Length;
            if (size <= limitBytes) return size;
            if (attempt >= MaxRetries || plan.VideoKbps <= DiscordEncodePlan.MinVideoKbps)
                throw new IOException($"上限に収まりませんでした（{size / 1_000_000.0:0.0} MB）");
            plan = plan.Shrink(size, limitBytes);
        }
    }

    private static async Task TranscodeAsync(StorageFile source, string destinationPath, TimeSpan start, TimeSpan end,
        DiscordEncodePlan plan, bool hasAudio, IProgress<double>? progress)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)plan.Width;
        profile.Video.Height = (uint)plan.Height;
        profile.Video.Bitrate = (uint)plan.VideoKbps * 1000;
        profile.Video.FrameRate.Numerator = (uint)plan.Framerate;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Audio = hasAudio ? AudioEncodingProperties.CreateAac(48_000, 2, (uint)plan.AudioKbps * 1000) : null;

        // 書き出し先は空のファイルを先に作ってから渡す（StorageFile で開くため）
        await using (File.Create(destinationPath)) { }
        var destination = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(destinationPath));

        // ドキュメントでは TrimStopTime は「終わりから削る長さ」だが、実際には元の動画での終わりの時刻として効く
        var transcoder = new MediaTranscoder
        {
            HardwareAccelerationEnabled = true,
            TrimStartTime = start,
            TrimStopTime = end,
        };
        var prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile);
        if (!prepared.CanTranscode)
            throw new IOException($"書き出せません（{prepared.FailureReason}）");

        var operation = prepared.TranscodeAsync();
        if (progress is not null) operation.Progress = (_, percent) => progress.Report(Math.Clamp(percent / 100, 0, 1));
        await operation;
    }
}
