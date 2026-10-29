using ScreenRecorderLib;

namespace ValoCapture.Recording;

/// <summary>録音に使えるオーディオデバイス。Id は録画設定に保存する値。</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public static class AudioDevices
{
    /// <summary>再生デバイス（ゲーム音をループバック録音する対象）。</summary>
    public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        Recorder.GetSystemAudioLoopbackDevices()
            .Select(d => new AudioDeviceInfo(d.DeviceName, d.FriendlyName, d.IsDefaultDevice))
            .ToList();

    /// <summary>録音デバイス（マイク）。</summary>
    public static IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        Recorder.GetSystemAudioCaptureDevices()
            .Select(d => new AudioDeviceInfo(d.DeviceName, d.FriendlyName, d.IsDefaultDevice))
            .ToList();
}
