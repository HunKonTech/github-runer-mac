#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."
VERSION="${APP_VERSION:-1.0.0}"
PROJECT_FILE="$PROJECT_DIR/src/GitRunnerManager.App/GitRunnerManager.App.csproj"
PUBLISH_DIR="$PROJECT_DIR/publish/win-x64"

echo "Publishing GitRunnerManager for Windows x64..."

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

if command -v powershell.exe >/dev/null 2>&1; then
  PUBLISH_DIR_WIN="$(cygpath -w "$PUBLISH_DIR")"
  powershell.exe -NoProfile -Command "
    \$publishDir = '$PUBLISH_DIR_WIN'
    \$runtimeFiles = @(
      'concrt140.dll',
      'msvcp140.dll',
      'msvcp140_1.dll',
      'msvcp140_2.dll',
      'vcruntime140.dll',
      'vcruntime140_1.dll'
    )

    foreach (\$file in \$runtimeFiles) {
      \$source = Join-Path \$env:WINDIR \"System32\\\$file\"
      if (Test-Path \$source) {
        Copy-Item -LiteralPath \$source -Destination \$publishDir -Force
      }
    }
  "
fi

echo "Published to $PUBLISH_DIR"
echo "Version: $VERSION"
