# 開発者向けガイド

## 必要なもの

- Windows 10 / 11（x64）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## プロジェクト構成

| プロジェクト | ターゲット | 内容 |
| --- | --- | --- |
| `src/ValoCapture.Core` | net8.0 | ゲーム状態の検知、Riot API クライアント、試合セッション・タイムライン、録画の保存管理、設定 |
| `src/ValoCapture.Recording` | net8.0-windows | ScreenRecorderLib による録画、ゲームウィンドウ・音声デバイスの取得 |
| `src/ValoCapture.App` | net8.0-windows | WPF のアプリ本体（メイン画面・設定画面・タスクトレイ） |
| `src/ValoCapture.Probe` | net8.0-windows | 状態検知・録画を手元で確かめるコンソールツール（使い方は `Program.cs` 冒頭） |
| `tests/ValoCapture.Core.Tests` | net8.0 | `ValoCapture.Core` の単体テスト |

ロジックはなるべく `ValoCapture.Core` に置き、テストで確認できる形にする。

## ビルド・実行

| やりたいこと | 方法 |
| --- | --- |
| ビルドして起動 | `ValoCapture.bat` をダブルクリック（起動中のときはビルドせずにそのまま起動する） |
| テスト | `dotnet test tests/ValoCapture.Core.Tests` |
| インストーラーを作る | `BuildExe.bat` をダブルクリック → `dist/releases/ValoCapture-win-Setup.exe` など（下の「配布と自動更新」） |

## 配布と自動更新

配布と自動更新には [Velopack](https://velopack.io/) を使う。`tools/publish.ps1` は .NET 同梱のフォルダを `dist/app` に発行し、`dotnet vpk pack` で `dist/releases` に次のものを作る（vpk のバージョンは `.config/dotnet-tools.json` で固定し、`Velopack` パッケージと揃える）。

| ファイル | 内容 |
| --- | --- |
| `ValoCapture-win-Setup.exe` | インストーラー。`%LocalAppData%\ValoCapture` に入れ、Visual C++ ランタイムも入れる |
| `ValoCapture-win-Portable.zip` | インストールせずに使う版（展開したフォルダで自動更新もできる） |
| `ValoCapture-<バージョン>-full.nupkg`、`-delta.nupkg`、`releases.win.json` | アプリが読む更新パッケージ |

- アプリは `Program.Main` の最初で `VelopackApp.Build().Run()` を呼ぶ（インストール・更新・アンインストール時の処理。アンインストール時は自動起動の登録を消す）
- 更新の確認とダウンロードは `Services/AppUpdater.cs`。GitHub Releases の最新リリースを見る。Setup.exe で入れていない場合（bin から起動したときなど）は何もしない
- 新しい版はダウンロードまで自動で行い、適用はトレイの「更新して再起動」で行う。録画中は適用しない
- 自動更新を手元で試すときは、`dotnet publish -p:Version=<新しい版>` と `dotnet vpk pack` で新しい版を別のフォルダに作り、環境変数 `VALOCAPTURE_UPDATE_SOURCE` にそのフォルダを入れてから、古い版のアプリを起動する（GitHub ではなくそのフォルダを確認する）

## リリース

GitHub Releases への公開は GitHub Actions（`.github/workflows/release.yml`）が行う。`v` で始まるタグをプッシュすると、Windows のランナーでテストと `tools/publish.ps1` を実行する。前回のリリースを取ってきて差分の更新パッケージも作り、`dotnet vpk upload github` で Setup.exe・Portable.zip・更新パッケージを添付したリリースを作る。

1. `src/ValoCapture.App/ValoCapture.App.csproj` の `<Version>` を上げてコミットし、`main` にプッシュする（基本はパッチを上げる。マイナーやメジャーを上げるのは指示があったときだけ）
2. 同じバージョンのタグを付けてプッシュする（`<Version>` が `1.0.1` ならタグは `v1.0.1`。一致しないとワークフローが失敗する）

```bash
git tag v1.0.1
```

```bash
git push origin v1.0.1
```

インストーラーは毎回同じ名前で添付するので、README の `releases/latest/download/ValoCapture-win-Setup.exe` は常に最新版を指す。進み具合は GitHub の「Actions」タブで確認できる。

## ツール

| ファイル | 内容 |
| --- | --- |
| `tools/publish.ps1` | インストーラーと更新パッケージの作成（`BuildExe.bat` とリリースのワークフローから呼ばれる） |
| `tools/make_icon.py` | アプリアイコン（`Assets/app.ico`）の生成 |
| `tools/calibrate_offset.py` | 録画のキルフィードから時刻補正の値を測る |
| `tools/cloud-setup.sh` | クラウド環境に .NET 8 SDK を入れるセットアップスクリプト |

## クラウドでの開発

Linux のクラウド環境でビルド・テストできるのは `ValoCapture.Core` と `tests/ValoCapture.Core.Tests` だけ。アプリの動作確認とインストーラーの作成は手元の Windows で行う。作業の分け方は [cloud-development.md](cloud-development.md) を参照。

## 注意

録画・試合データ・ログ（`recordings/`、`match-*.json`、`probe-log.jsonl`）は個人のプレイデータなのでリポジトリに入れない（`.gitignore` 済み）。
