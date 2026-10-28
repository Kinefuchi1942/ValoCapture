﻿# デスクトップに ValoRecorder のショートカットを作る（先に tools\publish.ps1 で exe を作っておく）
#   powershell -ExecutionPolicy Bypass -File tools\create_shortcut.ps1
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist\ValoRecorder.exe'
if (-not (Test-Path $exe)) { throw "$exe がありません。先に tools\publish.ps1 を実行してください。" }

$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $desktop 'ValoRecorder.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = Split-Path -Parent $exe
$shortcut.Save()

Write-Host "作成しました: $(Join-Path $desktop 'ValoRecorder.lnk')"
