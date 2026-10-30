using CommunityToolkit.Mvvm.ComponentModel;
using ValoCapture.App.Services;
using ValoCapture.Core.Game;
using ValoCapture.Core.Sessions;
using ValoCapture.Core.Settings;
using ValoCapture.Recording;

namespace ValoCapture.App.ViewModels;

/// <summary>音声デバイスの選択肢。Id が null なら Windows の既定のデバイス。</summary>
public sealed record DeviceOption(string? Id, string Name)
{
    // ComboBox の選択中の表示は DisplayMemberPath ではなく ToString を使う
    public override string ToString() => Name;
}

/// <summary>録画対象の選択肢。CaptureGameWindow が false なら MonitorDeviceName のモニター全体（null ならメインモニター）。</summary>
public sealed record CaptureOption(bool CaptureGameWindow, string? MonitorDeviceName, string Name)
{
    public override string ToString() => Name;
}

/// <summary>出力解像度の選択肢。Width・Height が 0 なら録画対象の解像度のまま。</summary>
public sealed record ResolutionOption(int Width, int Height, string Name)
{
    public override string ToString() => Name;
}

/// <summary>フレームレート・ビットレートの選択肢。</summary>
public sealed record NumberOption(int Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>録画するモードの選択肢。</summary>
public sealed partial class QueueOption(string id, bool isSelected) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = DisplayNames.Queue(id);
    [ObservableProperty] private bool _isSelected = isSelected;
}

/// <summary>設定画面。「保存」を押すまで <see cref="AppSettings"/> は変えない。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(AppSettings settings)
    {
        OutputDevices = BuildOptions(AudioDevices.GetOutputDevices(), settings.OutputDeviceId);
        InputDevices = BuildOptions(AudioDevices.GetInputDevices(), settings.InputDeviceId);
        _selectedOutput = OutputDevices.First(o => o.Id == settings.OutputDeviceId);
        _selectedInput = InputDevices.First(o => o.Id == settings.InputDeviceId);
        Queues = QueueIds.Selectable
            .Select(q => new QueueOption(q, settings.TargetQueues.Contains(q, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        CaptureTargets = BuildCaptureTargets(Displays.GetDisplays(), settings.MonitorDeviceName);
        _selectedCaptureTarget = CaptureTargets.First(o => o.CaptureGameWindow == settings.CaptureGameWindow
            && (o.CaptureGameWindow || o.MonitorDeviceName == settings.MonitorDeviceName));
        var keepSize = settings.OutputWidth <= 0 || settings.OutputHeight <= 0;
        var (width, height) = keepSize ? (0, 0) : (settings.OutputWidth, settings.OutputHeight);
        Resolutions = BuildResolutions(width, height);
        _selectedResolution = Resolutions.First(o => o.Width == width && o.Height == height);
        Framerates = BuildNumbers([30, 60, 120, 144], settings.Framerate, v => $"{v} fps");
        _selectedFramerate = Framerates.First(o => o.Value == settings.Framerate);
        Bitrates = BuildNumbers([8_000, 15_000, 25_000, 40_000], settings.BitrateKbps, BitrateName);
        _selectedBitrate = Bitrates.First(o => o.Value == settings.BitrateKbps);
        _launchAtStartup = StartupRegistration.IsEnabled;
        _captureMicrophone = settings.CaptureMicrophone;
        _recordingsRoot = settings.RecordingsRoot;
        _limitStorage = settings.StorageLimitGb > 0;
        _storageLimitGbText = (settings.StorageLimitGb > 0 ? settings.StorageLimitGb : DefaultLimitGb).ToString();
        UpdateUsage();
        UpdateSizeEstimate();
    }

    /// <summary>容量制限を初めてオンにしたときの値。1 試合（15 Mbps で 40 分）≒ 4.5 GB。</summary>
    private const int DefaultLimitGb = 100;

    /// <summary>録画の音声のビットレート（ScreenRecorderLibRecorder の設定と同じ）。</summary>
    private const int AudioBitrateKbps = 192;

    public IReadOnlyList<DeviceOption> OutputDevices { get; }
    public IReadOnlyList<DeviceOption> InputDevices { get; }
    public IReadOnlyList<CaptureOption> CaptureTargets { get; }
    public IReadOnlyList<QueueOption> Queues { get; }
    public IReadOnlyList<ResolutionOption> Resolutions { get; }
    public IReadOnlyList<NumberOption> Framerates { get; }
    public IReadOnlyList<NumberOption> Bitrates { get; }

    /// <summary>Windows へのサインイン時に自動起動するか（設定ファイルではなくレジストリに保存する）。</summary>
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private CaptureOption _selectedCaptureTarget;
    [ObservableProperty] private ResolutionOption _selectedResolution;
    [ObservableProperty] private NumberOption _selectedFramerate;
    [ObservableProperty] private NumberOption _selectedBitrate;
    [ObservableProperty] private string _sizeEstimateText = "";
    [ObservableProperty] private DeviceOption _selectedOutput;
    [ObservableProperty] private DeviceOption _selectedInput;
    [ObservableProperty] private bool _captureMicrophone;
    [ObservableProperty] private string _recordingsRoot;
    [ObservableProperty] private bool _limitStorage;
    [ObservableProperty] private string _storageLimitGbText;
    [ObservableProperty] private string _usageText = "";
    [ObservableProperty] private string _error = "";

    partial void OnRecordingsRootChanged(string value) => UpdateUsage();
    partial void OnSelectedBitrateChanged(NumberOption value) => UpdateSizeEstimate();

    /// <summary>ビットレートは固定（CBR）なので、容量は解像度・フレームレートによらずビットレートで決まる。</summary>
    private void UpdateSizeEstimate()
    {
        var bytesPerHour = (SelectedBitrate.Value + AudioBitrateKbps) * 1000L / 8 * 3600;
        SizeEstimateText = $"容量の目安: 1 時間あたり約 {bytesPerHour / (double)RecordingStorage.BytesPerGb:0.0} GB（1 試合 40 分で約 {bytesPerHour * 2 / 3 / (double)RecordingStorage.BytesPerGb:0.0} GB）";
    }

    private void UpdateUsage()
    {
        var sessions = MatchSession.LoadAll(RecordingsRoot);
        var bytes = sessions.Sum(RecordingStorage.GetSize);
        var bookmarked = sessions.Count(s => s.IsBookmarked);
        UsageText = $"現在の使用量: {bytes / (double)RecordingStorage.BytesPerGb:0.0} GB（{sessions.Count} 試合、うちブックマーク {bookmarked}）";
    }

    /// <summary>入力を検証して設定に書き込む。不正なら false を返し <see cref="Error"/> に理由を出す。</summary>
    public bool TryApply(AppSettings settings)
    {
        var limitGb = 0;
        if (LimitStorage && (!int.TryParse(StorageLimitGbText, out limitGb) || limitGb <= 0))
        {
            Error = "容量の上限は 1 以上の整数（GB）で入力してください";
            return false;
        }

        if (!Queues.Any(q => q.IsSelected))
        {
            Error = "録画するモードを 1 つ以上選んでください";
            return false;
        }

        // 候補にないキュー（settings.json に手で書いたものなど）は残す
        settings.TargetQueues =
        [
            .. settings.TargetQueues.Where(q => !QueueIds.Selectable.Contains(q, StringComparer.OrdinalIgnoreCase)),
            .. Queues.Where(q => q.IsSelected).Select(q => q.Id),
        ];
        settings.CaptureGameWindow = SelectedCaptureTarget.CaptureGameWindow;
        // ゲーム画面を選んだときは、見つからないときに撮るモニターとして前の選択を残す
        if (!SelectedCaptureTarget.CaptureGameWindow) settings.MonitorDeviceName = SelectedCaptureTarget.MonitorDeviceName;
        settings.OutputWidth = SelectedResolution.Width;
        settings.OutputHeight = SelectedResolution.Height;
        settings.Framerate = SelectedFramerate.Value;
        settings.BitrateKbps = SelectedBitrate.Value;
        settings.OutputDeviceId = SelectedOutput.Id;
        settings.InputDeviceId = SelectedInput.Id;
        settings.CaptureMicrophone = CaptureMicrophone;
        settings.RecordingsRoot = RecordingsRoot;
        settings.StorageLimitGb = LimitStorage ? limitGb : 0;
        Error = "";
        return true;
    }

    private static List<CaptureOption> BuildCaptureTargets(IReadOnlyList<DisplayInfo> displays, string? savedName)
    {
        var options = new List<CaptureOption>
        {
            new(true, null, "VALORANT のゲーム画面"),
            new(false, null, "メインモニター全体"),
        };
        options.AddRange(displays.Select(d => new CaptureOption(false, d.DeviceName, $"モニター全体: {d.Name}（{d.DeviceName}）")));

        if (savedName is not null && displays.All(d => d.DeviceName != savedName))
            options.Add(new CaptureOption(false, savedName, "接続されていないモニター（メインモニターで録画）"));
        return options;
    }

    private static List<ResolutionOption> BuildResolutions(int savedWidth, int savedHeight)
    {
        var options = new List<ResolutionOption>
        {
            new(0, 0, "録画対象の解像度のまま"),
            new(2560, 1440, "2560 × 1440"),
            new(1920, 1080, "1920 × 1080（標準）"),
            new(1600, 900, "1600 × 900"),
            new(1280, 720, "1280 × 720"),
        };
        // settings.json に手で書いた値は選択肢に足して残す
        if (savedWidth > 0 && savedHeight > 0 && options.All(o => o.Width != savedWidth || o.Height != savedHeight))
            options.Add(new ResolutionOption(savedWidth, savedHeight, $"{savedWidth} × {savedHeight}"));
        return options;
    }

    private static List<NumberOption> BuildNumbers(int[] values, int saved, Func<int, string> name)
    {
        var options = values.Select(v => new NumberOption(v, name(v))).ToList();
        if (!values.Contains(saved)) options.Add(new NumberOption(saved, name(saved)));
        return options;
    }

    private static string BitrateName(int kbps)
    {
        var label = kbps switch
        {
            8_000 => "低",
            15_000 => "標準",
            25_000 => "高",
            40_000 => "最高",
            _ => null,
        };
        var mbps = $"{kbps / 1000.0:0.#} Mbps";
        return label is null ? mbps : $"{label}（{mbps}）";
    }

    private static List<DeviceOption> BuildOptions(IReadOnlyList<AudioDeviceInfo> devices, string? savedId)
    {
        var defaultName = devices.FirstOrDefault(d => d.IsDefault)?.Name;
        var options = new List<DeviceOption>
        {
            new(null, defaultName is null ? "Windows の既定のデバイス" : $"Windows の既定のデバイス（{defaultName}）"),
        };
        options.AddRange(devices.Select(d => new DeviceOption(d.Id, d.Name)));

        // 保存済みのデバイスが今は抜かれている場合も、選択は消さずに残す（録画時は既定のデバイスを使う）
        if (savedId is not null && devices.All(d => d.Id != savedId))
            options.Add(new DeviceOption(savedId, "接続されていないデバイス（既定のデバイスで録音）"));
        return options;
    }
}
