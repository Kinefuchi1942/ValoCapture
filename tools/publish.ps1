# dist\ValoCapture.exe（.NET 同梱の単体 exe）と、友人に渡す配布用の dist\ValoCapture-<バージョン>.zip を作る
# SDK のない PC にもこの exe だけ渡せば動く。バージョンは src\ValoCapture.App\ValoCapture.App.csproj の <Version>
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$csproj = Join-Path $root 'src\ValoCapture.App\ValoCapture.App.csproj'

if (Get-Process ValoCapture, ValoCapture.App -ErrorAction SilentlyContinue) {
    throw 'ValoCapture が起動中です。タスクトレイから終了してから実行してください。'
}

$version = ([xml](Get-Content $csproj -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "$csproj に <Version> がありません。" }

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
dotnet publish $csproj -p:PublishProfile=SingleFile -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }

# 参照先プロジェクト・パッケージのデバッグシンボルは配布に要らない
Remove-Item (Join-Path $dist '*.pdb')
# アセンブリ名は ValoCapture.App のまま、配布用の名前だけ変える
$exe = Join-Path $dist 'ValoCapture.exe'
Move-Item (Join-Path $dist 'ValoCapture.App.exe') $exe

# 同梱する説明書。メモ帳で確実に読めるよう BOM 付き UTF-8・CRLF にする
$readme = Join-Path $dist 'はじめにお読みください.txt'
$text = (Get-Content (Join-Path $PSScriptRoot 'dist-readme.txt') -Encoding UTF8 -Raw).Replace('{VERSION}', $version)
[IO.File]::WriteAllText($readme, ($text -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding $true))

# 展開すると ValoCapture フォルダができるようにする。日本語のファイル名が化けないよう UTF-8 で格納する
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = Join-Path $dist "ValoCapture-$version.zip"
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create', [Text.Encoding]::UTF8)
try {
    foreach ($file in $exe, $readme) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file, "ValoCapture/$(Split-Path -Leaf $file)", 'Optimal')
    }
} finally { $archive.Dispose() }

Write-Host "作成しました: $exe"
Write-Host "配布用:       $zip"
