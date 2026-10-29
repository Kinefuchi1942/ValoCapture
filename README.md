# ValoCapture

VALORANT の試合を自動で録画し、キル・デス・アシストなどのイベントから見返せる Windows 用アプリ。

![ビューアのモックアップ](mockups/viewer.png)

## 主な機能

- **自動録画**：タスクトレイに常駐し、対象の試合が始まると録画を開始、終わると停止する
- **イベントのタイムライン**：試合後に試合情報を取得し、キル・デス・アシストを録画の再生位置に対応づけて表示する。イベントをクリックするとその場面へ移動する
- **再生**：0.25x〜2x の速度切り替え、全画面表示、時刻補正（タイムラインと動画のずれの調整）
- **設定画面**：対象のモード（初期設定はコンペティティブのみ）、解像度・fps・ビットレート、音声デバイス、マイク録音、保存先
- **容量制限**：上限を超えると古い試合から削除する。☆ブックマークした試合は残る
- **Windows の起動時に自動起動**するオプション

試合情報の取得には Riot クライアントのローカル API と非公式 API を読み取り専用で使う。ゲームへの書き込みや注入は一切しない。Riot Games 非公式のツール。

## 使う（配布版）

1. 配布用の `ValoCapture-<バージョン>.zip` を好きなフォルダに展開する
2. `ValoCapture.exe` をダブルクリックして起動する（インストール・管理者権限は不要）

起動しないときは [Visual C++ 再頒布可能パッケージ（x64）](https://aka.ms/vs/17/release/vc_redist.x64.exe) を入れる。詳しくは zip に同梱する説明書（[tools/dist-readme.txt](tools/dist-readme.txt)）を参照。

### 再生中のキー操作

| キー | 操作 |
| --- | --- |
| Space | 再生 / 一時停止 |
| ← / → | 5 秒戻る / 進む |
| , / . | 0.1 秒戻る / 進む（コマ送り） |
| P / N | 前 / 次のイベントへ |
| ↑ / ↓ | 音量を上げる / 下げる |
| M | ミュート |
| F | 全画面（Esc で戻る） |

### 保存場所

| 保存するもの | 場所 |
| --- | --- |
| 録画 | `ビデオ\ValoCapture`（設定画面で変更可） |
| 設定 | `%APPDATA%\ValoCapture` |

## 開発

### 必要なもの

- Windows 10 / 11（x64）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### プロジェクト構成

| プロジェクト | ターゲット | 内容 |
| --- | --- | --- |
| `src/ValoCapture.Core` | net8.0 | ゲーム状態の検知、Riot API クライアント、試合セッション・タイムライン、録画の保存管理、設定 |
| `src/ValoCapture.Recording` | net8.0-windows | ScreenRecorderLib による録画、ゲームウィンドウ・音声デバイスの取得 |
| `src/ValoCapture.App` | net8.0-windows | WPF のアプリ本体（メイン画面・設定画面・タスクトレイ） |
| `src/ValoCapture.Probe` | net8.0-windows | 状態検知・録画を手元で確かめるコンソールツール（使い方は `Program.cs` 冒頭） |
| `tests/ValoCapture.Core.Tests` | net8.0 | `ValoCapture.Core` の単体テスト |

ロジックはなるべく `ValoCapture.Core` に置き、テストで確認できる形にする。

### ビルド・実行

| やりたいこと | 方法 |
| --- | --- |
| ビルドして起動 | `ValoCapture.bat` をダブルクリック（起動中のときはビルドせずにそのまま起動する） |
| テスト | `dotnet test tests/ValoCapture.Core.Tests` |
| exe と配布用 zip を作る | `BuildExe.bat` をダブルクリック → `dist/ValoCapture.exe` と `dist/ValoCapture-<バージョン>.zip` |
| デスクトップにショートカットを作る | `powershell -ExecutionPolicy Bypass -File tools\create_shortcut.ps1`（先に exe を作っておく） |

配布し直すときは `src/ValoCapture.App/ValoCapture.App.csproj` の `<Version>` を上げる。

### ツール

| ファイル | 内容 |
| --- | --- |
| `tools/publish.ps1` | 単体 exe と配布用 zip の作成（`BuildExe.bat` から呼ばれる） |
| `tools/dist-readme.txt` | zip に同梱する説明書のひな形 |
| `tools/make_icon.py` | アプリアイコン（`Assets/app.ico`）の生成 |
| `tools/calibrate_offset.py` | 録画のキルフィードから時刻補正の値を測る |
| `tools/cloud-setup.sh` | クラウド環境に .NET 8 SDK を入れるセットアップスクリプト |

### クラウドでの開発

Linux のクラウド環境でビルド・テストできるのは `ValoCapture.Core` と `tests/ValoCapture.Core.Tests` だけ。アプリの動作確認と exe / zip の作成は手元の Windows で行う。作業の分け方は [docs/cloud-development.md](docs/cloud-development.md) を参照。

### 注意

録画・試合データ・ログ（`recordings/`、`match-*.json`、`probe-log.jsonl`）は個人のプレイデータなのでリポジトリに入れない（`.gitignore` 済み）。
