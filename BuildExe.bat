@echo off
chcp 65001 >nul
setlocal

rem ダブルクリックで tools\publish.ps1 を実行し、dist に exe と配布用 zip を作る
cd /d "%~dp0"
echo exe を作成しています（数十秒かかります）...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1"
if errorlevel 1 (
    echo.
    echo exe の作成に失敗しました。上のメッセージを確認してください。
    pause
    exit /b 1
)

rem 出来上がったものをすぐ確認できるように dist フォルダを開く
start "" explorer "%~dp0dist"
echo.
pause
