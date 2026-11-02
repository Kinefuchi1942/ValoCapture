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

# 更新後の起動時に出す変更点（ReleaseNotes.md）を git のコミットの件名から作って同梱する。
# 版ごとに、前の v タグからの src を変えたコミットを並べる（「1.2.3: …」のバージョンを上げるコミットは除く）。
# この版のタグがまだなければ（タグを付ける前に手元で作るとき）、HEAD までをこの版とする
$notesPath = Join-Path $app 'ReleaseNotes.md'
$currentNotes = Join-Path $dist 'ReleaseNotes-current.md'
Push-Location $root
try {
    $encoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.Encoding]::UTF8
    $tags = @(git tag --list 'v*' --sort=-v:refname | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' -and [version]$_.Substring(1) -lt [version]$version })
    if ($LASTEXITCODE -ne 0) { throw 'git でタグを取得できません。' }
    $head = if (git tag --list "v$version") { "v$version" } else { 'HEAD' }
    $refs = @(@{ Version = $version; Ref = $head }) + @($tags | ForEach-Object { @{ Version = $_.Substring(1); Ref = $_ } }) | Select-Object -First 21
    $sections = for ($i = 0; $i -lt $refs.Count - 1; $i++) {
        $date = git log -1 --format=%cs $refs[$i].Ref
        $changes = @(git log --format=%s "$($refs[$i + 1].Ref)..$($refs[$i].Ref)" -- src | Where-Object { $_ -notmatch '^\d+\.\d+\.\d+: ' })
        "## $($refs[$i].Version) ($date)`n" + (($changes | ForEach-Object { "- $_`n" }) -join '')
    }
    $utf8 = New-Object Text.UTF8Encoding $false
    [IO.File]::WriteAllText($notesPath, ($sections -join "`n"), $utf8)
    [IO.File]::WriteAllText($currentNotes, [string]@($sections)[0], $utf8)
} catch {
    Write-Warning "変更点を作れませんでした（更新後に変更点を出しません）: $_"
} finally {
    [Console]::OutputEncoding = $encoding
    Pop-Location
}

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
        --framework vcredist143-x64 -o $releases @(if (Test-Path $currentNotes) { '--releaseNotes', $currentNotes })
    if ($LASTEXITCODE -ne 0) { throw 'パッケージの作成に失敗しました。' }
} finally { Pop-Location }

Write-Host "作成しました: $releases"
Write-Host "配布用:       $(Join-Path $releases 'ValoCapture-win-Setup.exe')"
