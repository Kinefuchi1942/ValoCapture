using CommunityToolkit.Mvvm.ComponentModel;
using ValoCapture.App.Services;
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

/// <summary>設定画面。「保存」を押すまで <see cref="AppSettings"/> は変えない。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(AppSettings settings)
    {
        OutputDevices = BuildOptions(AudioDevices.GetOutputDevices(), settings.OutputDeviceId);
        InputDevices = BuildOptions(AudioDevices.GetInputDevices(), settings.InputDeviceId);
        _selectedOutput = OutputDevices.First(o => o.Id == settings.OutputDeviceId);
        _selectedInput = InputDevices.First(o => o.Id == settings.InputDeviceId);
        _launchAtStartup = StartupRegistration.IsEnabled;
        _captureMicrophone = settings.CaptureMicrophone;
        _recordingsRoot = settings.RecordingsRoot;
        _limitStorage = settings.StorageLimitGb > 0;
        _storageLimitGbText = (settings.StorageLimitGb > 0 ? settings.StorageLimitGb : DefaultLimitGb).ToString();
        UpdateUsage();
    }

    /// <summary>容量制限を初めてオンにしたときの値。1 試合（15 Mbps で 40 分）≒ 4.5 GB。</summary>
    private const int DefaultLimitGb = 100;

    public IReadOnlyList<DeviceOption> OutputDevices { get; }
    public IReadOnlyList<DeviceOption> InputDevices { get; }

    /// <summary>Windows へのサインイン時に自動起動するか（設定ファイルではなくレジストリに保存する）。</summary>
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private DeviceOption _selectedOutput;
    [ObservableProperty] private DeviceOption _selectedInput;
    [ObservableProperty] private bool _captureMicrophone;
    [ObservableProperty] private string _recordingsRoot;
    [ObservableProperty] private bool _limitStorage;
    [ObservableProperty] private string _storageLimitGbText;
    [ObservableProperty] private string _usageText = "";
    [ObservableProperty] private string _error = "";

    partial void OnRecordingsRootChanged(string value) => UpdateUsage();

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

        settings.OutputDeviceId = SelectedOutput.Id;
        settings.InputDeviceId = SelectedInput.Id;
        settings.CaptureMicrophone = CaptureMicrophone;
        settings.RecordingsRoot = RecordingsRoot;
        settings.StorageLimitGb = LimitStorage ? limitGb : 0;
        Error = "";
        return true;
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
