#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."
VERSION="${APP_VERSION:-1.0.0}"

echo "Publishing GitHubRunnerTray for Linux x64..."

cd "$PROJECT_DIR"

dotnet publish src/GitHubRunnerTray.App/GitHubRunnerTray.App.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:Version="$VERSION" \
  -o "./publish/linux-x64"

echo "Published to ./publish/linux-x64"
echo "Version: $VERSION"
