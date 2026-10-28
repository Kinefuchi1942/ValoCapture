@echo off
chcp 65001 >nul
setlocal

rem ダブルクリックで ValoRecorder を起動する。ソースが更新されていればビルドしてから起動する
cd /d "%~dp0"
set "EXE=%~dp0src\ValoRecorder.App\bin\Release\net8.0-windows\ValoRecorder.App.exe"

rem 起動中は exe がロックされてビルドできないので、そのまま起動する（二重起動はアプリ側で通知される）
tasklist /fi "imagename eq ValoRecorder.App.exe" | find /i "ValoRecorder.App.exe" >nul
if not errorlevel 1 goto launch

echo ValoRecorder をビルドしています...
dotnet build src\ValoRecorder.App\ValoRecorder.App.csproj -c Release -nologo -v q
if errorlevel 1 (
    echo.
    echo ビルドに失敗しました。上のエラーを確認してください。
    pause
    exit /b 1
)

:launch
start "" "%EXE%"
