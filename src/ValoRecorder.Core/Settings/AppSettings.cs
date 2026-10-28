using System.Text.Json;
using ValoRecorder.Core.Game;
using ValoRecorder.Core.Recording;
using ValoRecorder.Core.Sessions;

namespace ValoRecorder.Core.Settings;

/// <summary>アプリの設定。%APPDATA%\ValoRecorder\settings.json に保存する。</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string RecordingsRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ValoRecorder");

    public List<string> TargetQueues { get; set; } = [QueueIds.Competitive];

    public int Framerate { get; set; } = 60;
    public int BitrateKbps { get; set; } = 15_000;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public bool CaptureMicrophone { get; set; }

    /// <summary>ゲーム音を録音する再生デバイスの ID。null なら Windows の既定のデバイス。</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>マイクの録音デバイスの ID。null なら Windows の既定のデバイス。</summary>
    public string? InputDeviceId { get; set; }

    /// <summary>録画全体の容量の上限（GB）。超えたら古い試合から削除する（ブックマークは残す）。0 なら無制限。</summary>
    public int StorageLimitGb { get; set; }
    public long DefaultOffsetMs { get; set; } = SessionOrchestrator.CalibratedOffsetMs;

    /// <summary>ビューアの再生音量（0〜1）。</summary>
    public double Volume { get; set; } = 0.8;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ValoRecorder", "settings.json");

    /// <summary>設定を読む。ファイルがない・壊れている場合は既定値。</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (JsonException) { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public RecorderSettings ToRecorderSettings() => new()
    {
        Framerate = Framerate,
        BitrateKbps = BitrateKbps,
        OutputSize = (OutputWidth, OutputHeight),
        CaptureMicrophone = CaptureMicrophone,
        SystemAudioDeviceId = OutputDeviceId,
        MicrophoneDeviceId = InputDeviceId,
    };
}
