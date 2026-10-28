using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ValoRecorder.App.ViewModels;

namespace ValoRecorder.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _isPlaying;
    private TimeSpan _duration;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _vm.SeekRequested += t => { Seek(t); Play(); };
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _positionTimer.Start();

        OpenSelectedVideo();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedSession)) OpenSelectedVideo();
    }

    private void OpenSelectedVideo()
    {
        Pause();
        Player.Close();
        _duration = TimeSpan.Zero;
        Timeline.Duration = TimeSpan.Zero;

        var session = _vm.SelectedSession;
        if (session is null)
        {
            PlayerMessage.Text = "録画がありません";
            return;
        }
        if (!session.HasVideo)
        {
            PlayerMessage.Text = "動画ファイルが見つかりません";
            return;
        }

        PlayerMessage.Text = "";
        Player.Source = new Uri(session.VideoPath);
        // 先頭フレームを表示するため一度再生して止める
        Player.Play();
        Player.Pause();
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        _duration = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        Timeline.Duration = _duration;
        ApplySpeed();
        UpdatePosition();
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e) => Pause();

    private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e) =>
        PlayerMessage.Text = $"再生できません: {e.ErrorException.Message}";

    private void UpdatePosition()
    {
        var pos = Player.Position;
        Timeline.Position = pos;
        TimeText.Text = $"{pos:mm\\:ss} / {_duration:mm\\:ss}";
    }

    private void Seek(TimeSpan t)
    {
        if (_duration > TimeSpan.Zero && t > _duration) t = _duration;
        Player.Position = t;
        UpdatePosition();
    }

    private void Play()
    {
        if (Player.Source is null) return;
        Player.Play();
        ApplySpeed();
        _isPlaying = true;
        PlayButton.Content = "⏸";
    }

    private void Pause()
    {
        Player.Pause();
        _isPlaying = false;
        PlayButton.Content = "▶";
    }

    public void PauseVideo() => Pause();

    private void TogglePlay()
    {
        if (_isPlaying) Pause(); else Play();
    }

    private void JumpToEvent(EventItemViewModel? item)
    {
        if (item is null) return;
        EventList.SelectedItem = item;
        EventList.ScrollIntoView(item);
        _vm.JumpToCommand.Execute(item);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlay();
    private void OnVideoClick(object sender, MouseButtonEventArgs e) => TogglePlay();
    private void OnTimelineSeek(TimeSpan t) => Seek(t);
    private void OnNextEvent(object sender, RoutedEventArgs e) => JumpToEvent(_vm.NextEvent(Player.Position));
    private void OnPreviousEvent(object sender, RoutedEventArgs e) => JumpToEvent(_vm.PreviousEvent(Player.Position));

    private void OnEventClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is EventItemViewModel item)
            JumpToEvent(item);
    }

    private void OnSpeedChanged(object sender, SelectionChangedEventArgs e) => ApplySpeed();

    private void ApplySpeed()
    {
        if (Player is null || SpeedBox?.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var speed = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        // SpeedRatio を設定すると直前のシークが打ち消されるので、変わったときだけ設定する
        if (Player.SpeedRatio != speed) Player.SpeedRatio = speed;
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "録画フォルダを選択", InitialDirectory = _vm.RecordingsRoot };
        if (dialog.ShowDialog(this) == true) _vm.RecordingsRoot = dialog.FolderName;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left: Seek(Player.Position - TimeSpan.FromSeconds(5)); break;
            case Key.Right: Seek(Player.Position + TimeSpan.FromSeconds(5)); break;
            case Key.OemComma: Seek(Player.Position - TimeSpan.FromMilliseconds(100)); break;
            case Key.OemPeriod: Seek(Player.Position + TimeSpan.FromMilliseconds(100)); break;
            case Key.N: JumpToEvent(_vm.NextEvent(Player.Position)); break;
            case Key.P: JumpToEvent(_vm.PreviousEvent(Player.Position)); break;
            default: return;
        }
        e.Handled = true;
    }
}
