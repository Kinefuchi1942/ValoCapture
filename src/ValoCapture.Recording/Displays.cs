using ScreenRecorderLib;

namespace ValoCapture.Recording;

/// <summary>録画できるモニター。DeviceName（\.\DISPLAY1 など）は録画設定に保存する値。</summary>
public sealed record DisplayInfo(string DeviceName, string Name);

public static class Displays
{
    public static IReadOnlyList<DisplayInfo> GetDisplays() =>
        Recorder.GetDisplays()
            .Select(d => new DisplayInfo(d.DeviceName, string.IsNullOrEmpty(d.FriendlyName) ? d.DeviceName : d.FriendlyName))
            .ToList();
}
