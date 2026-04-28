#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."
AVALONIA_DIR="$PROJECT_DIR/Avalonia"
APP_NAME="GitHubRunnerTray"
BUNDLE_ID="com.githubrunnertray.app"

echo "=== Building GitHubRunnerTray for macOS ==="

cd "$AVALONIA_DIR"

echo "Restoring packages..."
dotnet restore

echo "Building for macOS arm64..."
rm -rf ./publish/macos
dotnet publish src/GitHubRunnerTray.App/GitHubRunnerTray.App.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o ./publish/macos

PUBLISH_DIR="./publish/macos"
APP_CONTENTS="$PUBLISH_DIR/$APP_NAME.app/Contents"

echo "Creating app bundle..."
rm -rf "$PUBLISH_DIR/$APP_NAME.app"
mkdir -p "$APP_CONTENTS/MacOS"
mkdir -p "$APP_CONTENTS/Resources"

cp "$PUBLISH_DIR/GitHubRunnerTray" "$APP_CONTENTS/MacOS/$APP_NAME"
chmod +x "$APP_CONTENTS/MacOS/$APP_NAME"

cp "$PUBLISH_DIR"/*.dll "$APP_CONTENTS/MacOS/"
cp "$PUBLISH_DIR"/*.dylib "$APP_CONTENTS/MacOS/"
cp "$PUBLISH_DIR"/*.pdb "$APP_CONTENTS/MacOS/" 2>/dev/null || true
cp "$PUBLISH_DIR"/*.json "$APP_CONTENTS/MacOS/"
cp "$PUBLISH_DIR"/*.config "$APP_CONTENTS/MacOS/" 2>/dev/null || true

for f in "$PUBLISH_DIR"/lib*; do
    if [ -f "$f" ]; then
        cp "$f" "$APP_CONTENTS/MacOS/"
    fi
done

if [ -d "$PUBLISH_DIR/Assets" ]; then
    cp -r "$PUBLISH_DIR/Assets" "$APP_CONTENTS/"
fi

if [ -d "$AVALONIA_DIR/src/GitHubRunnerTray.App/Assets" ]; then
    cp -r "$AVALONIA_DIR/src/GitHubRunnerTray.App/Assets" "$APP_CONTENTS/MacOS/"
fi

cp "$PROJECT_DIR/Assets/app_icon.ico" "$APP_CONTENTS/Resources/"

cat > "$APP_CONTENTS/Info.plist" << 'EOFPLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>GitHubRunnerTray</string>
    <key>CFBundleIdentifier</key>
    <string>com.githubrunnertray.app</string>
    <key>CFBundleName</key>
    <string>GitHubRunnerTray</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.13</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>CFBundleIconFile</key>
    <string>app_icon.ico</string>
</dict>
</plist>
EOFPLIST

echo -n "APPL????" > "$APP_CONTENTS/PkgInfo"

chmod -R 755 "$APP_CONTENTS"

APP_BUNDLE="$PUBLISH_DIR/$APP_NAME.app"
echo "App bundle created at $APP_BUNDLE"
echo "Installing to /Applications..."
rm -rf "/Applications/$APP_NAME.app"
cp -R "$APP_BUNDLE" "/Applications/"
open "/Applications/$APP_NAME.app"
echo "Done! App is now running."
