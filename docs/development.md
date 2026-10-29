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
| exe と配布用 zip を作る | `BuildExe.bat` をダブルクリック → `dist/ValoCapture.exe` と `dist/ValoCapture-<バージョン>.zip` |
| デスクトップにショートカットを作る | `powershell -ExecutionPolicy Bypass -File tools\create_shortcut.ps1`（先に exe を作っておく） |

配布し直すときは `src/ValoCapture.App/ValoCapture.App.csproj` の `<Version>` を上げる。

## ツール

| ファイル | 内容 |
| --- | --- |
| `tools/publish.ps1` | 単体 exe と配布用 zip の作成（`BuildExe.bat` から呼ばれる） |
| `tools/dist-readme.txt` | zip に同梱する説明書のひな形 |
| `tools/make_icon.py` | アプリアイコン（`Assets/app.ico`）の生成 |
| `tools/calibrate_offset.py` | 録画のキルフィードから時刻補正の値を測る |
| `tools/cloud-setup.sh` | クラウド環境に .NET 8 SDK を入れるセットアップスクリプト |

## クラウドでの開発

Linux のクラウド環境でビルド・テストできるのは `ValoCapture.Core` と `tests/ValoCapture.Core.Tests` だけ。アプリの動作確認と exe / zip の作成は手元の Windows で行う。作業の分け方は [cloud-development.md](cloud-development.md) を参照。

## 注意

録画・試合データ・ログ（`recordings/`、`match-*.json`、`probe-log.jsonl`）は個人のプレイデータなのでリポジトリに入れない（`.gitignore` 済み）。
