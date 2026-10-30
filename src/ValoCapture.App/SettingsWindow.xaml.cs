using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ValoCapture.App.Services;
using ValoCapture.App.ViewModels;
using ValoCapture.Core.Settings;

namespace ValoCapture.App;

/// <summary>設定画面。保存したら DialogResult = true で閉じる（設定は書き換え済み）。</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = _vm = new SettingsViewModel(settings);
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "動画の保存先を選択", InitialDirectory = _vm.RecordingsRoot };
        if (dialog.ShowDialog(this) == true) _vm.RecordingsRoot = dialog.FolderName;
    }

    /// <summary>
    /// 入力欄やコンボボックスの上でもホイールで設定画面をスクロールする（WPF の TextBox はホイールを握りつぶすため）。
    /// 開いたドロップダウンの中は別ウィンドウなので、そちらのスクロールに任せる。
    /// </summary>
    private void OnScrollerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && PresentationSource.FromDependencyObject(source)?.RootVisual != this) return;
        if (Scroller.ScrollableHeight <= 0) return;
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - e.Delta * 0.4);
        e.Handled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_vm.TryApply(_settings)) return;

        try
        {
            if (_vm.LaunchAtStartup != StartupRegistration.IsEnabled)
                StartupRegistration.SetEnabled(_vm.LaunchAtStartup);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            _vm.Error = $"自動起動の設定を変更できませんでした: {ex.Message}";
            return;
        }
        DialogResult = true;
    }
}
