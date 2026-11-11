namespace ValoCapture.Core.Recording;

/// <summary>
/// クリップを Discord のアップロード上限に収めて書き出すときの解像度・フレームレート・ビットレート。
/// 長さと上限から使えるビットレートを求め、ビットレートが低いほど解像度とフレームレートを下げる
/// （低いビットレートで高い解像度のままだと、動きの多い場面が崩れるため）。
/// </summary>
public sealed record DiscordEncodePlan(int Width, int Height, int Framerate, int VideoKbps, int AudioKbps)
{
    /// <summary>上限に対して狙うサイズの割合。エンコーダーがビットレートを少し超えることと MP4 の目次の分を見込む。</summary>
    public const double TargetRatio = 0.9;

    /// <summary>映像のビットレートの上限。これ以上上げても見た目はほとんど変わらない。</summary>
    public const int MaxVideoKbps = 12_000;

    /// <summary>映像のビットレートの下限。これを下回る長さは上限に収めない。</summary>
    public const int MinVideoKbps = 400;

    public const int AudioBitrateKbps = 128;

    /// <summary>映像のビットレートごとの高さ（ビットレートが足りれば上から選ぶ）。</summary>
    private static readonly (int MinKbps, int Height)[] HeightSteps = [(5_000, 1080), (2_500, 720), (1_200, 540), (0, 480)];

    /// <summary>映像のビットレートがこれを下回るときはフレームレートを 30 fps に下げる。</summary>
    private const int FullFramerateMinKbps = 1_500;

    /// <summary>上限のメガバイト数をバイト数にする（Discord の MB は 1,000,000 バイトとして、小さいほうに合わせる）。</summary>
    public static long LimitBytes(int limitMb) => limitMb * 1_000_000L;

    /// <summary>この上限に収められる最長の長さ。</summary>
    public static TimeSpan MaxDuration(long limitBytes) =>
        TimeSpan.FromSeconds(limitBytes * TargetRatio * 8 / 1000 / (MinVideoKbps + AudioBitrateKbps));

    /// <summary>
    /// 書き出しの設定を決める。上限に収まらない長さなら null。
    /// </summary>
    /// <param name="sourceKbps">元の動画の映像と音声を合わせたビットレート（0 なら不明）。これより上げても画質は良くならない。</param>
    public static DiscordEncodePlan? Create(long limitBytes, TimeSpan duration, int sourceWidth, int sourceHeight, int sourceFramerate, int sourceKbps = 0)
    {
        if (duration <= TimeSpan.Zero || sourceWidth <= 0 || sourceHeight <= 0) return null;

        var totalKbps = limitBytes * TargetRatio * 8 / 1000 / duration.TotalSeconds;
        var videoKbps = (int)Math.Min(totalKbps - AudioBitrateKbps, MaxVideoKbps);
        if (sourceKbps > 0) videoKbps = Math.Min(videoKbps, sourceKbps);
        if (videoKbps < MinVideoKbps) return null;

        var height = Math.Min(sourceHeight, HeightSteps.First(s => videoKbps >= s.MinKbps).Height);
        var width = Even((double)sourceWidth * height / sourceHeight);
        height = Even(height);

        var framerate = sourceFramerate > 0 ? sourceFramerate : 60;
        if (videoKbps < FullFramerateMinKbps) framerate = Math.Min(framerate, 30);

        return new DiscordEncodePlan(width, height, framerate, videoKbps, AudioBitrateKbps);
    }

    /// <summary>実際のサイズが上限を超えたとき、超えた割合だけビットレートを下げた設定。</summary>
    public DiscordEncodePlan Shrink(long actualBytes, long limitBytes)
    {
        var ratio = limitBytes * TargetRatio / actualBytes;
        return this with { VideoKbps = Math.Max(MinVideoKbps, (int)(VideoKbps * ratio)) };
    }

    // H.264 は幅と高さを偶数にする
    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}
