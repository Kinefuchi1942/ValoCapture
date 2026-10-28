namespace ValoRecorder.Core.Recording;

/// <summary>画面録画エンジン。実装は ValoRecorder.Recording（Windows 専用）にある。</summary>
public interface IScreenRecorder
{
    /// <summary>録画を開始し、実際に録画が始まった時刻を返す（動画とイベントの時刻合わせの基準）。</summary>
    Task<DateTimeOffset> StartAsync(string outputPath, CancellationToken ct = default);

    /// <summary>録画を停止し、ファイルが閉じられるまで待つ。</summary>
    Task<RecordingResult> StopAsync(CancellationToken ct = default);

    bool IsRecording { get; }
}

public sealed record RecordingResult(string FilePath, DateTimeOffset StartedAt, DateTimeOffset StoppedAt);

public sealed record RecorderSettings
{
    public int Framerate { get; init; } = 60;
    public int BitrateKbps { get; init; } = 15_000;
    /// <summary>出力解像度。縦横比が違う場合は黒帯を付けて収める。null なら録画対象の解像度のまま。</summary>
    public (int Width, int Height)? OutputSize { get; init; } = (1920, 1080);
    /// <summary>
    /// true なら VALORANT のウィンドウだけを録画する（ウィンドウモードでもデスクトップが映らない）。
    /// ウィンドウが見つからなければモニター全体にフォールバックする。
    /// </summary>
    public bool CaptureGameWindow { get; init; } = true;
    /// <summary>モニター全体を録画するときのモニター（\\.\DISPLAY1 など）。null ならメインモニター。</summary>
    public string? MonitorDeviceName { get; init; }
    public bool CaptureSystemAudio { get; init; } = true;
    public bool CaptureMicrophone { get; init; }
    /// <summary>録画エンジン内部のログの出力先（調査用）。null なら出さない。</summary>
    public string? EngineLogPath { get; init; }
}
