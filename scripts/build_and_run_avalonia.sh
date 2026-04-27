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

APP_CONTENTS="./publish/macos/$APP_NAME.app/Contents"
mkdir -p "$APP_CONTENTS/MacOS"
mkdir -p "$APP_CONTENTS/Resources"

echo "Creating app bundle..."

cp ./publish/macos/GitHubRunnerTray "$APP_CONTENTS/MacOS/$APP_NAME"
chmod +x "$APP_CONTENTS/MacOS/$APP_NAME"

for f in ./publish/macos/*.dylib ./publish/macos/*.dll ./publish/macos/*.pdb ./publish/macos/*.json ./publish/macos/*.config; do
    if [ -f "$f" ]; then
        cp "$f" "$APP_CONTENTS/Resources/"
    fi
done

for f in ./publish/macos/lib*; do
    if [ -f "$f" ]; then
        cp "$f" "$APP_CONTENTS/Resources/"
    fi
done

if [ -d "./publish/macos/Assets" ]; then
    cp -r "./publish/macos/Assets" "$APP_CONTENTS/"
fi

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
</dict>
</plist>
EOFPLIST

echo -n "APPL????" > "$APP_CONTENTS/PkgInfo"

chmod -R 755 "$APP_CONTENTS"

echo "App bundle created at ./publish/macos/$APP_NAME.app"
echo "Installing to /Applications..."
rm -rf "/Applications/$APP_NAME.app"
cp -R "./publish/macos/$APP_NAME.app" "/Applications/"
open "/Applications/$APP_NAME.app"
echo "Done! App is now running."