using System.Windows;
using Microsoft.Win32;
using ValoRecorder.App.ViewModels;
using ValoRecorder.Core.Settings;

namespace ValoRecorder.App;

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

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_vm.TryApply(_settings)) DialogResult = true;
    }
}
