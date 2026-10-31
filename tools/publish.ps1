# Velopack でインストーラーと更新パッケージを dist\releases に作る（vpk のバージョンは .config\dotnet-tools.json）
#   ValoCapture-win-Setup.exe   … 友人に渡すインストーラー（.NET と Visual C++ ランタイムも入れる）
#   ValoCapture-win-Portable.zip … インストールせずに使う版（展開したフォルダで自動更新もできる）
#   *.nupkg, releases.win.json   … アプリ内の自動更新が読む
# バージョンは src\ValoCapture.App\ValoCapture.App.csproj の <Version>
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
# -RepoUrl と -Token を渡すと、GitHub の最新リリースを取ってきて差分の更新パッケージも作る（リリースのワークフロー用）
param(
    [string]$RepoUrl,
    [string]$Token
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$app = Join-Path $dist 'app'
$releases = Join-Path $dist 'releases'
$csproj = Join-Path $root 'src\ValoCapture.App\ValoCapture.App.csproj'

if (Get-Process ValoCapture, ValoCapture.App -ErrorAction SilentlyContinue) {
    throw 'ValoCapture が起動中です。タスクトレイから終了してから実行してください。'
}

$version = ([xml](Get-Content $csproj -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "$csproj に <Version> がありません。" }

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
dotnet publish $csproj -p:PublishProfile=Velopack -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }

# アセンブリ名は ValoCapture.App のまま、配布用の名前だけ変える（exe は同じフォルダの ValoCapture.App.dll を読む）
Move-Item (Join-Path $app 'ValoCapture.App.exe') (Join-Path $app 'ValoCapture.exe')

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'vpk を用意できませんでした。' }

    if ($RepoUrl) {
        # 最初の Velopack 版のように取ってくるものがなければ、差分なしで続ける
        dotnet vpk download github --repoUrl $RepoUrl --token $Token -o $releases
        if ($LASTEXITCODE -ne 0) { Write-Warning '前回のリリースを取得できなかったので、差分の更新パッケージは作りません。' }
    }

    dotnet vpk pack --packId ValoCapture --packVersion $version --packTitle ValoCapture --packAuthors Kinefuchi1942 `
        --packDir $app --mainExe ValoCapture.exe --icon (Join-Path $root 'src\ValoCapture.App\Assets\app.ico') `
        --framework vcredist143-x64 -o $releases
    if ($LASTEXITCODE -ne 0) { throw 'パッケージの作成に失敗しました。' }
} finally { Pop-Location }

Write-Host "作成しました: $releases"
Write-Host "配布用:       $(Join-Path $releases 'ValoCapture-win-Setup.exe')"
