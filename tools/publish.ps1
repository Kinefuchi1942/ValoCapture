# dist\ValoRecorder.exe（.NET 同梱の単体 exe）を作る。SDK のない PC にもこの 1 ファイルだけ渡せば動く
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'

if (Get-Process ValoRecorder, ValoRecorder.App -ErrorAction SilentlyContinue) {
    throw 'ValoRecorder が起動中です。タスクトレイから終了してから実行してください。'
}

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
dotnet publish (Join-Path $root 'src\ValoRecorder.App\ValoRecorder.App.csproj') -p:PublishProfile=SingleFile -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }

# 参照先プロジェクト・パッケージのデバッグシンボルは配布に要らない
Remove-Item (Join-Path $dist '*.pdb')
# アセンブリ名は ValoRecorder.App のまま、配布用の名前だけ変える
Move-Item (Join-Path $dist 'ValoRecorder.App.exe') (Join-Path $dist 'ValoRecorder.exe')
Write-Host "作成しました: $(Join-Path $dist 'ValoRecorder.exe')"
