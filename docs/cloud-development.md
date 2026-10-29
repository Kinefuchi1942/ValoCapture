# クラウド環境での開発の流れ

ValoRecorder は Windows 専用のアプリ（WPF / WinForms / ScreenRecorderLib）なので、作業を **クラウド（Claude Code のクラウドセッション、Linux）** と **手元の Windows PC** に分けて進める。

## 役割分担

| 作業 | クラウド | 手元の Windows |
| --- | :---: | :---: |
| コードの変更（全プロジェクト） | ○ | ○ |
| `ValoRecorder.Core` のビルド | ○ | ○ |
| `ValoRecorder.Core.Tests` の実行 | ○ | ○ |
| App / Recording / Probe のビルド（net8.0-windows） | × | ○ |
| アプリを起動しての動作確認（録画・UI・ゲーム検出） | × | ○ |
| exe と配布用 zip の作成（`BuildExe.bat`） | × | ○ |
| 録画データ・試合データを使う調査 | × ※ | ○ |

※ `recordings/`、`match-*.json`、`probe-log.jsonl`、`dist/` は `.gitignore` の対象なので、クラウドには存在しない。調査に必要なときは、該当部分をチャットに貼るか、個人データを除いたサンプルを `tests/` に追加する。

### クラウドでビルドできるプロジェクト

| プロジェクト | ターゲット | クラウド |
| --- | --- | :---: |
| `src/ValoRecorder.Core` | net8.0 | ○ |
| `tests/ValoRecorder.Core.Tests` | net8.0 | ○ |
| `src/ValoRecorder.Recording` | net8.0-windows | × |
| `src/ValoRecorder.App` | net8.0-windows | × |
| `src/ValoRecorder.Probe` | net8.0-windows | × |

Windows 専用プロジェクトのコードもクラウドで書き換えられるが、コンパイルが通るかは手元で確かめるまで分からない。ロジックはなるべく `ValoRecorder.Core` に置き、テストで確認できる形にする。

## 1 回の作業の流れ

1. **クラウドで依頼する**
   クラウドセッションで、やりたいことを日本語で伝える。
2. **クラウドで変更とテスト**
   Claude がコードを変更し、`dotnet test tests/ValoRecorder.Core.Tests` を実行する。
3. **クラウドでコミット・プッシュ**
   コミットメッセージは日本語。現在は `main` に直接プッシュしている。
4. **手元で取り込む**
   ```
   git pull
   ```
5. **手元で動作確認**
   `ValoRecorder.bat` をダブルクリックすると、Release ビルドしてから起動する（起動中のときはビルドせずにそのまま起動する）。
6. **問題があればクラウドへ戻す**
   ビルドエラー・例外・おかしな挙動は、エラーメッセージやログをそのままクラウドセッションに貼って修正を依頼し、2 に戻る。
7. **配布するとき**
   `src/ValoRecorder.App/ValoRecorder.App.csproj` の `<Version>` を上げて（クラウドで頼んでもよい）、手元で `BuildExe.bat` をダブルクリックする。`dist/ValoRecorder.exe` と `dist/ValoRecorder-<バージョン>.zip` ができる。

### 手元で直接変更したとき

手元で変更した場合は、先にコミットしてプッシュしておく。クラウド側の変更と食い違うのを防ぐため、クラウドで作業を始める前に手元の未プッシュの変更をなくしておく。

## クラウド環境の準備

クラウドのコンテナは毎回まっさらな状態から始まり、.NET 8 SDK は入っていない。Microsoft の配布サーバー（`builds.dotnet.microsoft.com`）はネットワーク設定で遮断されているため、Ubuntu のパッケージから入れる。

```
apt-get update && apt-get install -y dotnet-sdk-8.0
```

毎回入れるのが手間なら、クラウド環境の設定にある **セットアップスクリプト** に上のコマンドを書いておくと、セッション開始時に自動で入る。

## 注意点

- クラウドのコンテナは一定時間操作しないと消える。残したい変更は必ずコミット・プッシュする。
- 個人のプレイデータ（録画・試合データ・ログ）はリポジトリに入れない。
- Claude への指示や前提は、リポジトリ直下の `CLAUDE.md` に書いておくと毎回のセッションで自動的に読み込まれる。
