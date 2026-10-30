# ValoCapture

- 応答は常に日本語で行う。
- コミットメッセージも日本語で書く（既存の履歴に合わせる）。
- 大半のプロジェクトは net8.0-windows（WPF / WinForms / ScreenRecorderLib）。Linux のクラウド環境でビルド・テストできるのは `ValoCapture.Core` と `tests/ValoCapture.Core.Tests`（net8.0）だけ。アプリの動作確認と exe / zip の作成（BuildExe.bat、tools\publish.ps1）は Windows で行う。
- クラウドと手元の Windows での作業の分け方は `docs/cloud-development.md` を参照。
- 配布する exe に影響する修正（`src/` 配下など）をしたら、確認を待たずにリリースまで通す。
  1. `src/ValoCapture.App/ValoCapture.App.csproj` の `<Version>` を上げる（不具合修正はパッチ、機能追加はマイナー）
  2. コミットして `main` に取り込む
  3. `main` の最新コミットに `v<Version>` のタグを付けてプッシュする（`.github/workflows/release.yml` が GitHub Releases に公開する）
  4. Actions の成功と、リリースに exe・zip が添付されたことを確認して報告する
  - ドキュメントだけの変更などではリリースしない。タグは必ずワークフローを含む `main` のコミットに付ける。手順の詳細は `docs/development.md` の「リリース」。
