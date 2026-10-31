@echo off
chcp 65001 >nul
setlocal

rem ダブルクリックで tools\publish.ps1 を実行し、dist\releases にインストーラーと更新パッケージを作る
cd /d "%~dp0"
echo インストーラーを作成しています（数十秒かかります）...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1"
if errorlevel 1 (
    echo.
    echo インストーラーの作成に失敗しました。上のメッセージを確認してください。
    pause
    exit /b 1
)

rem 出来上がったものをすぐ確認できるように dist\releases フォルダを開く
start "" explorer "%~dp0dist\releases"
echo.
pause
