#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."
VERSION="${APP_VERSION:-1.0.0}"
PROJECT_FILE="$PROJECT_DIR/src/GitHubRunnerTray.App/GitHubRunnerTray.App.csproj"
PUBLISH_DIR="$PROJECT_DIR/publish/win-x64"

echo "Publishing GitHubRunnerTray for Windows x64..."

if [ ! -f "$PROJECT_FILE" ]; then
  echo "Project file does not exist: $PROJECT_FILE" >&2
  exit 1
fi

dotnet publish "$PROJECT_FILE" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:Version="$VERSION" \
  -o "$PUBLISH_DIR"

echo "Published to $PUBLISH_DIR"
echo "Version: $VERSION"
