using Velopack;
using ValoCapture.App.Services;

namespace ValoCapture.App;

/// <summary>
/// エントリポイント。Velopack のインストール・更新・アンインストール時の処理は、WPF を起動する前に済ませる必要がある。
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // アンインストール後に消えた exe を自動起動しようとしないよう、Run キーの登録を消す
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.SetEnabled(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
