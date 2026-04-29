#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."
VERSION="${APP_VERSION:-1.0.0}"

echo "Publishing GitHubRunnerTray for Windows x64..."

cd "$PROJECT_DIR"

dotnet publish src/GitHubRunnerTray.App/GitHubRunnerTray.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:Version="$VERSION" \
  -o "./publish/win-x64"

echo "Published to ./publish/win-x64"
echo "Version: $VERSION"
