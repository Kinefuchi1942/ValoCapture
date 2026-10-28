using ScreenRecorderLib;
using ValoRecorder.Core.Recording;

namespace ValoRecorder.Recording;

/// <summary>
/// ScreenRecorderLib（Media Foundation、GPU エンコード）による録画。
/// VALORANT は排他フルスクリーンになりうるので、ウィンドウではなくモニター全体を Desktop Duplication で撮る。
/// </summary>
public sealed class ScreenRecorderLibRecorder : IScreenRecorder
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    private RecorderSettings _settings;
    private Recorder? _recorder;
    private TaskCompletionSource<DateTimeOffset>? _started;
    private TaskCompletionSource<string>? _completed;
    private DateTimeOffset _startedAt;

    public ScreenRecorderLibRecorder(RecorderSettings settings) => _settings = settings;

    public bool IsRecording => _recorder is not null;

    /// <summary>録画の設定。録画中に変えた場合は次の録画から反映される。</summary>
    public RecorderSettings Settings
    {
        get => _settings;
        set => _settings = value;
    }

    public async Task<DateTimeOffset> StartAsync(string outputPath, CancellationToken ct = default)
    {
        if (_recorder is not null) throw new InvalidOperationException("すでに録画中です");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var recorder = Recorder.CreateRecorder(BuildOptions());
        recorder.OnStatusChanged += (_, e) =>
        {
            if (e.Status == RecorderStatus.Recording) _started.TrySetResult(DateTimeOffset.UtcNow);
        };
        recorder.OnRecordingComplete += (_, e) => _completed.TrySetResult(e.FilePath);
        recorder.OnRecordingFailed += (_, e) =>
        {
            var error = new InvalidOperationException($"録画に失敗しました: {e.Error}");
            _started.TrySetException(error);
            _completed.TrySetException(error);
        };

        _recorder = recorder;
        recorder.Record(outputPath);

        try
        {
            _startedAt = await _started.Task.WaitAsync(StartTimeout, ct);
            return _startedAt;
        }
        catch
        {
            recorder.Stop();
            recorder.Dispose();
            _recorder = null;
            throw;
        }
    }

    public async Task<RecordingResult> StopAsync(CancellationToken ct = default)
    {
        var recorder = _recorder ?? throw new InvalidOperationException("録画していません");
        var stoppedAt = DateTimeOffset.UtcNow;
        recorder.Stop();

        try
        {
            var path = await _completed!.Task.WaitAsync(StopTimeout, ct);
            return new RecordingResult(path, _startedAt, stoppedAt);
        }
        finally
        {
            recorder.Dispose();
            _recorder = null;
        }
    }

    /// <summary>直近の録画で実際に使った録画対象（ログ用）。</summary>
    public string CaptureSourceDescription { get; private set; } = "";

    private RecordingSourceBase BuildSource()
    {
        // ウィンドウ録画（Windows Graphics Capture）は縮小出力でエラーになるため、
        // ゲームのいるモニターを Desktop Duplication で撮り、ウィンドウモードならゲーム画面部分だけ切り抜く
        if (_settings.CaptureGameWindow && GameWindow.Find() is { } game)
        {
            var source = new DisplayRecordingSource(game.MonitorDeviceName) { IsCursorCaptureEnabled = false };
            var r = game.ClientRect;
            if (!game.IsFullscreen)
                source.SourceRect = new ScreenRect(r.X, r.Y, r.Width, r.Height);
            CaptureSourceDescription = game.IsFullscreen
                ? $"VALORANT（フルスクリーン, {game.MonitorDeviceName} {r.Width}x{r.Height}）"
                : $"VALORANT（ウィンドウ, {game.MonitorDeviceName} の {r.Width}x{r.Height}+{r.X}+{r.Y} を切り抜き）";
            return source;
        }

        var display = _settings.MonitorDeviceName is { } name
            ? new DisplayRecordingSource(name)
            : DisplayRecordingSource.MainMonitor;
        display.IsCursorCaptureEnabled = false;
        CaptureSourceDescription = _settings.CaptureGameWindow
            ? $"モニター全体（VALORANT のウィンドウが見つからないため）"
            : "モニター全体";
        return display;
    }

    private RecorderOptions BuildOptions()
    {
        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = { BuildSource() } },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                Stretch = StretchMode.Uniform,
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = _settings.CaptureSystemAudio || _settings.CaptureMicrophone,
                Bitrate = AudioBitrate.bitrate_192kbps,
                Channels = AudioChannels.Stereo,
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.CBR,
                    EncoderProfile = H264Profile.High,
                },
                Bitrate = _settings.BitrateKbps * 1000,
                Framerate = _settings.Framerate,
                // 固定フレームレートだと取りこぼしたフレームの分だけ映像が縮み、音声とずれる
                // （7 分 49 秒の録画で映像が 3.4 秒短くなった）。実際の取得時刻をタイムスタンプに使う
                IsFixedFramerate = false,
                IsHardwareEncodingEnabled = true,
                // 異常終了してもそこまでの動画を再生できるようにする
                IsFragmentedMp4Enabled = true,
            },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = false },
        };

        // 再生デバイス（ゲーム音）と録音デバイス（マイク）。指定がない・抜かれている場合は既定のデバイス
        if (_settings.CaptureSystemAudio)
            options.AudioOptions.AudioSources.Add(IsConnected(_settings.SystemAudioDeviceId, AudioDevices.GetOutputDevices())
                ? new LoopbackAudioSource(_settings.SystemAudioDeviceId)
                : LoopbackAudioSource.Default);
        if (_settings.CaptureMicrophone)
            options.AudioOptions.AudioSources.Add(IsConnected(_settings.MicrophoneDeviceId, AudioDevices.GetInputDevices())
                ? new CaptureAudioSource(_settings.MicrophoneDeviceId)
                : CaptureAudioSource.Default);

        if (_settings.EngineLogPath is { } logPath)
            options.LogOptions = new LogOptions { IsLogEnabled = true, LogFilePath = logPath, LogSeverityLevel = LogLevel.Debug };

        if (_settings.OutputSize is { } size)
        {
            var screenSize = new ScreenSize(size.Width, size.Height);
            options.OutputOptions.OutputFrameSize = screenSize;
            foreach (var source in options.SourceOptions.RecordingSources)
            {
                source.OutputSize = screenSize;
                source.Stretch = StretchMode.Uniform;
            }
        }

        return options;
    }

    private static bool IsConnected(string? deviceId, IReadOnlyList<AudioDeviceInfo> devices) =>
        deviceId is not null && devices.Any(d => d.Id == deviceId);
}
