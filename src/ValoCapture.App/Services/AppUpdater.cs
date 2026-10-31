using Velopack;
using Velopack.Sources;

namespace ValoCapture.App.Services;

/// <summary>
/// GitHub Releases に新しい版があればダウンロードしておき、ユーザーの操作で適用する（Velopack）。
/// Setup.exe で入れていない場合（bin から起動した開発中など）は使えない。
/// </summary>
public sealed class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/Kinefuchi1942/ValoCapture";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>新しい版を確認し、あればダウンロードまで行う。適用できる版を返す（なければ null）。</summary>
    public async Task<VelopackAsset?> CheckAndDownloadAsync()
    {
        if (!IsInstalled) return null;

        var info = await _manager.CheckForUpdatesAsync();
        if (info is null) return null;

        // ダウンロード済みなら Velopack が再利用する
        await _manager.DownloadUpdatesAsync(info);
        return info.TargetFullRelease;
    }

    /// <summary>
    /// このプロセスの終了を待って更新を適用し、新しい版を起動する更新プログラムを走らせる。
    /// 呼んだあとはすぐにアプリを終了すること（60 秒待っても終わらないと更新プログラムはあきらめる）。
    /// </summary>
    public void ApplyAfterExit(VelopackAsset update) =>
        _manager.WaitExitThenApplyUpdates(update, silent: false, restart: true, restartArgs: []);
}
