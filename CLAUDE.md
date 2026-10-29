# ValoCapture

- 応答は常に日本語で行う。
- コミットメッセージも日本語で書く（既存の履歴に合わせる）。
- 大半のプロジェクトは net8.0-windows（WPF / WinForms / ScreenRecorderLib）。Linux のクラウド環境でビルド・テストできるのは `ValoCapture.Core` と `tests/ValoCapture.Core.Tests`（net8.0）だけ。アプリの動作確認と exe / zip の作成（BuildExe.bat、tools\publish.ps1）は Windows で行う。
- クラウドと手元の Windows での作業の分け方は `docs/cloud-development.md` を参照。
