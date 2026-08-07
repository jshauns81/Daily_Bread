#!/bin/bash
#
# Build BreadBox in Release and install it to /Applications.
#
# Running from Xcode leaves the app in DerivedData, where a `Clean Build
# Folder` deletes it out from under the Dock icon. This puts a real copy in
# /Applications that survives, and is the same command after every change:
# pull, run this, relaunch.
#
# Signed with the same automatic Development identity Xcode uses, because on
# Apple silicon an unsigned binary will not launch at all. Nothing here
# creates or modifies certificates, and it never passes
# -allowProvisioningUpdates.

set -euo pipefail

cd "$(dirname "$0")"

DEST="${1:-/Applications}"
BUILD_DIR=$(mktemp -d)
trap 'rm -rf "$BUILD_DIR"' EXIT

# Xcode-beta is the project's toolchain everywhere else; match it so BreadBox
# is never the odd one out.
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode-beta.app}"
[ -d "$DEVELOPER_DIR" ] || export DEVELOPER_DIR=/Applications/Xcode.app

command -v xcodegen >/dev/null || { echo "error: xcodegen not installed (brew install xcodegen)" >&2; exit 1; }

echo "==> Generating project"
xcodegen generate --spec "$PWD/project.yml" --project "$PWD" >/dev/null

echo "==> Building Release"
xcodebuild -project BreadBox.xcodeproj -scheme BreadBox \
  -configuration Release -derivedDataPath "$BUILD_DIR" build 2>&1 \
  | grep -E "error:|warning: .*(deprecat|unused)|^\*\* BUILD" || true

APP="$BUILD_DIR/Build/Products/Release/BreadBox.app"
[ -d "$APP" ] || { echo "error: build produced no app" >&2; exit 1; }

# A running copy cannot be replaced cleanly; ask it to quit first.
if pgrep -x BreadBox >/dev/null; then
  echo "==> Quitting the running BreadBox"
  osascript -e 'quit app "BreadBox"' 2>/dev/null || true
  sleep 2
fi

echo "==> Installing to $DEST"
rm -rf "$DEST/BreadBox.app"
cp -R "$APP" "$DEST/BreadBox.app"

# Fails loudly rather than leaving something that dies on launch with a
# uselessly generic "damaged" alert.
codesign --verify --deep --strict "$DEST/BreadBox.app" \
  && echo "==> Signature verified"

echo
echo "Installed: $DEST/BreadBox.app"
echo "Open it once, then right-click its Dock icon → Options → Keep in Dock."
