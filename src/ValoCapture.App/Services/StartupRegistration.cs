using Microsoft.Win32;

namespace ValoCapture.App.Services;

/// <summary>
/// Windows へのサインイン時の自動起動。HKCU の Run キーに登録する（管理者権限は不要）。
/// 有効かどうかはレジストリが正（タスクマネージャーの「スタートアップ」から変えられても食い違わないように）。
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ValoCapture";

    /// <summary>旧名（ValoRecorder）のときの登録名。</summary>
    private const string LegacyValueName = "ValoRecorder";

    /// <summary>自動起動ではウィンドウを開かずトレイだけで起動する。</summary>
    private static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>登録済みなら、今の実行ファイルの場所で登録し直す（フォルダを移動した場合に備える）。</summary>
    public static void RefreshIfEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        // 旧名で登録されていたら新しい名前で登録し直す（旧名の exe を起動しないように）
        if (key.GetValue(LegacyValueName) is string)
        {
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            key.SetValue(ValueName, Command);
        }

        if (key.GetValue(ValueName) is string current && current != Command)
            key.SetValue(ValueName, Command);
    }
}
