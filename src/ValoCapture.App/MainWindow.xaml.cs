using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ValoCapture.App.ViewModels;

namespace ValoCapture.App;

public partial class MainWindow : Window
{
    private const string PlayGlyphText = "";
    private const string PauseGlyphText = "";
    private const string FullscreenGlyphText = "";
    private const string ExitFullscreenGlyphText = "";

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _isPlaying;
    private TimeSpan _duration;

    // シークバーのドラッグ中。シークは溜まると最後の位置の表示が遅れるので、間隔を空けて最新の位置だけ出す
    private readonly DispatcherTimer _scrubTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _isScrubbing;
    private bool _resumeAfterScrub;
    private TimeSpan? _pendingScrub;

    // 動画を置き換えて開き直したときに戻す再生位置と再生中かどうか
    private (TimeSpan Position, bool Playing)? _restoreAfterOpen;

    // 全画面にする前の状態
    private bool _isFullscreen;
    private WindowState _restoreState;
    private WindowStyle _restoreStyle;
    private ResizeMode _restoreResize;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _vm.SeekRequested += t => { Seek(t); Play(); };
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _positionTimer.Start();
        _scrubTimer.Tick += (_, _) => FlushScrub();

        OpenSelectedVideo();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedSession)) OpenSelectedVideo();
    }

    private void OpenSelectedVideo()
    {
        _restoreAfterOpen = null;
        Pause();
        Player.Close();
        SetDuration(TimeSpan.Zero);

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

    private void SetDuration(TimeSpan duration)
    {
        _duration = duration;
        Timeline.Duration = duration;
        DurationLabel.Text = duration.ToString(@"mm\:ss");
        TotalRun.Text = $"/ {duration:mm\\:ss}";
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        SetDuration(Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero);
        ApplySpeed(CurrentSpeed());
        UpdatePosition();

        if (_restoreAfterOpen is { } restore)
        {
            _restoreAfterOpen = null;
            Seek(restore.Position);
            if (restore.Playing) Play();
        }
    }

    /// <summary>
    /// 試合の動画ファイルを置き換える。開いている動画なら、いったん閉じて置き換え、同じ位置から開き直す。
    /// </summary>
    public async Task ReplaceVideoAsync(string directory, Func<Task> replace)
    {
        var session = _vm.SelectedSession;
        if (session is null || Player.Source is null ||
            !string.Equals(session.Session.Directory, directory, StringComparison.OrdinalIgnoreCase))
        {
            await replace();
            return;
        }

        var restore = (Player.Position, _isPlaying);
        Pause();
        Player.Close();
        Player.Source = null;
        try
        {
            await replace();
        }
        finally
        {
            // 置き換えている間に別の試合を選んでいれば、そちらがもう開いている
            if (_vm.SelectedSession == session)
            {
                OpenSelectedVideo();
                _restoreAfterOpen = restore;
            }
        }
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e) => Pause();

    private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e) =>
        PlayerMessage.Text = $"再生できません: {e.ErrorException.Message}";

    private void UpdatePosition()
    {
        // ドラッグ中はつまみをマウスに合わせたままにする（シークが追いつく前の古い位置で戻さない）
        if (_isScrubbing) return;
        ShowPosition(Player.Position);
    }

    private void ShowPosition(TimeSpan pos)
    {
        Timeline.Position = pos;
        PositionRun.Text = pos.ToString(@"mm\:ss");
    }

    private void Seek(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && t > _duration) t = _duration;
        Player.Position = t;
        ShowPosition(t);
    }

    private void OnTimelineDragStarted()
    {
        _isScrubbing = true;
        _resumeAfterScrub = _isPlaying;
        if (_isPlaying) Pause();
    }

    private void OnTimelineSeek(TimeSpan t)
    {
        if (!_isScrubbing)
        {
            Seek(t);
            return;
        }

        PositionRun.Text = t.ToString(@"mm\:ss");
        _pendingScrub = t;
        // 前のシークから間隔が空いていればすぐ出し、そうでなければタイマーで最新の位置だけ出す
        if (!_scrubTimer.IsEnabled)
        {
            FlushScrub();
            _scrubTimer.Start();
        }
    }

    private void FlushScrub()
    {
        if (_pendingScrub is not { } t)
        {
            _scrubTimer.Stop();
            return;
        }
        _pendingScrub = null;
        Player.Position = t;
    }

    private void OnTimelineDragCompleted(TimeSpan t)
    {
        _scrubTimer.Stop();
        _pendingScrub = null;
        _isScrubbing = false;
        Seek(t);
        if (_resumeAfterScrub) Play();
    }

    private void Play()
    {
        if (Player.Source is null) return;
        Player.Play();
        _isPlaying = true;
        PlayGlyph.Text = PauseGlyphText;
    }

    private void Pause()
    {
        Player.Pause();
        _isPlaying = false;
        PlayGlyph.Text = PlayGlyphText;
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
    private void OnVideoClick(object sender, MouseButtonEventArgs e)
    {
        // ダブルクリックで全画面の切り替え
        if (e.ClickCount == 2) ToggleFullscreen(); else TogglePlay();
    }
    private void OnNextEvent(object sender, RoutedEventArgs e) => JumpToEvent(_vm.NextEvent(Player.Position));
    private void OnPreviousEvent(object sender, RoutedEventArgs e) => JumpToEvent(_vm.PreviousEvent(Player.Position));
    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnEventClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is EventItemViewModel item)
            JumpToEvent(item);
    }

    // 動画の角を枠に合わせて丸める
    private void OnVideoFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var radius = _isFullscreen ? 0 : 9;
        VideoClip.Clip = new RectangleGeometry(new Rect(0, 0, VideoClip.ActualWidth, VideoClip.ActualHeight), radius, radius);
    }

    private void OnSpeedChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Player is not null)
            ApplySpeed(double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture));
    }

    private double CurrentSpeed() =>
        SpeedGroup.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) is { Tag: string tag }
            ? double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture)
            : 1;

    // SpeedRatio を設定すると直前のシークが打ち消されるので、変わったときだけ設定する
    private void ApplySpeed(double speed)
    {
        if (Player.SpeedRatio != speed) Player.SpeedRatio = speed;
    }

    /// <summary>動画・タイムライン・操作だけを画面いっぱいに表示する。</summary>
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        var side = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        LeftPanel.Visibility = RightPanel.Visibility = Header.Visibility = side;
        LeftColumn.Width = new GridLength(_isFullscreen ? 0 : 236);
        RightColumn.Width = new GridLength(_isFullscreen ? 0 : 272);
        LeftGap.Width = RightGap.Width = new GridLength(_isFullscreen ? 0 : 16);
        Root.Margin = new Thickness(_isFullscreen ? 0 : 16);
        VideoFrame.CornerRadius = new CornerRadius(_isFullscreen ? 0 : 10);
        VideoFrame.BorderThickness = new Thickness(_isFullscreen ? 0 : 1);
        TimelinePanel.Margin = new Thickness(_isFullscreen ? 12 : 0, 12, _isFullscreen ? 12 : 0, 0);
        Controls.Margin = new Thickness(_isFullscreen ? 12 : 0, 12, _isFullscreen ? 12 : 0, _isFullscreen ? 12 : 0);
        FullscreenGlyph.Text = _isFullscreen ? ExitFullscreenGlyphText : FullscreenGlyphText;
        FullscreenButton.ToolTip = _isFullscreen ? "全画面を終了 (F / Esc)" : "全画面 (F)";

        if (_isFullscreen)
        {
            _restoreState = WindowState;
            _restoreStyle = WindowStyle;
            _restoreResize = ResizeMode;
            // 最大化したままだとタスクバーが残るので、一度戻してから枠なしで最大化する
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _restoreStyle;
            ResizeMode = _restoreResize;
            WindowState = _restoreState;
        }
    }

    // 右クリックした試合を選んでからメニューを出す
    private void OnMatchRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is ListBoxItem item)
            item.IsSelected = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 削除の確認中は Enter / Esc だけ受け付ける
        if (_vm.DeleteCandidate is not null)
        {
            if (e.Key == Key.Enter) _vm.ConfirmDeleteCommand.Execute(null);
            else if (e.Key == Key.Escape) _vm.CancelDeleteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left: Seek(Player.Position - TimeSpan.FromSeconds(5)); break;
            case Key.Right: Seek(Player.Position + TimeSpan.FromSeconds(5)); break;
            case Key.OemComma: Seek(Player.Position - TimeSpan.FromMilliseconds(100)); break;
            case Key.OemPeriod: Seek(Player.Position + TimeSpan.FromMilliseconds(100)); break;
            case Key.Up: _vm.Volume = Math.Min(1, _vm.Volume + 0.1); break;
            case Key.Down: _vm.Volume = Math.Max(0, _vm.Volume - 0.1); break;
            case Key.M: _vm.ToggleMuteCommand.Execute(null); break;
            case Key.F: ToggleFullscreen(); break;
            case Key.Escape when _isFullscreen: ToggleFullscreen(); break;
            case Key.N: JumpToEvent(_vm.NextEvent(Player.Position)); break;
            case Key.P: JumpToEvent(_vm.PreviousEvent(Player.Position)); break;
            case Key.Delete: _vm.RequestDeleteCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }
}
