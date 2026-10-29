#!/bin/bash
# クラウド環境のセットアップスクリプト。環境設定の「セットアップスクリプト」にこの内容を貼り付ける
# Microsoft の配布サーバーは遮断されているので、.NET 8 SDK は Ubuntu のパッケージから入れる
set -euo pipefail

if dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
    echo ".NET 8 SDK はインストール済み"
    exit 0
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq dotnet-sdk-8.0
dotnet --list-sdks
