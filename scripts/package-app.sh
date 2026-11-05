#!/usr/bin/env bash
# Builds the cross-platform DAXDiff app (src/DaxDiff.App) for macOS or Linux into ./dist:
#   macOS: DAXDiff-<version>-<rid>.dmg holding DAXDiff.app (ad-hoc signed, not notarized)
#   Linux: DAXDiff-<version>-<rid>.tar.gz with the app, an icon, a .desktop entry and install.sh
# Usage: scripts/package-app.sh osx-arm64|osx-x64|linux-x64|linux-arm64
# Run it on the matching system: macOS packages need hdiutil, iconutil and codesign.
set -euo pipefail

rid="${1:?usage: package-app.sh <runtime identifier>}"
root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(dotnet msbuild "$root/src/DaxDiff.Domain/DaxDiff.Domain.csproj" -getProperty:Version | tr -d '[:space:]')"
publish="$root/publish/app-$rid"
dist="$root/dist"
icon="$root/src/DaxDiff.App/Assets/DAXDiff.png"

rm -rf "$publish"
mkdir -p "$dist"
dotnet publish "$root/src/DaxDiff.App/DaxDiff.App.csproj" -c Release -r "$rid" --self-contained true -o "$publish"

case "$rid" in
  osx-*)
    stage="$root/publish/dmg-$rid"
    app="$stage/DAXDiff.app"
    rm -rf "$stage"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$publish/." "$app/Contents/MacOS/"

    iconset="$root/publish/DAXDiff.iconset"
    rm -rf "$iconset" && mkdir -p "$iconset"
    for size in 16 32 128 256 512; do
      sips -z $size $size "$icon" --out "$iconset/icon_${size}x${size}.png" >/dev/null
      sips -z $((size * 2)) $((size * 2)) "$icon" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$app/Contents/Resources/DAXDiff.icns"

    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>DAXDiff</string>
  <key>CFBundleDisplayName</key><string>DAXDiff</string>
  <key>CFBundleIdentifier</key><string>io.github.tanvir073.daxdiff</string>
  <key>CFBundleVersion</key><string>${version%%-*}</string>
  <key>CFBundleShortVersionString</key><string>${version}</string>
  <key>CFBundleExecutable</key><string>DAXDiff</string>
  <key>CFBundleIconFile</key><string>DAXDiff.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>DAXDiff project</string>
      <key>CFBundleTypeExtensions</key><array><string>pbicomp</string></array>
      <key>CFBundleTypeRole</key><string>Editor</string>
    </dict>
  </array>
</dict>
</plist>
PLIST

    # Apple Silicon refuses unsigned code; an ad-hoc signature lets it run (users still confirm the first start).
    codesign --force --deep --sign - "$app"
    ln -s /Applications "$stage/Applications"
    out="$dist/DAXDiff-$version-$rid.dmg"
    rm -f "$out"
    hdiutil create -volname "DAXDiff $version" -srcfolder "$stage" -ov -format UDZO "$out" >/dev/null
    ;;

  linux-*)
    name="DAXDiff-$version"
    stage="$root/publish/tar-$rid/$name"
    rm -rf "$root/publish/tar-$rid"
    mkdir -p "$stage"
    cp -R "$publish/." "$stage/"
    cp "$icon" "$stage/daxdiff.png"
    cat > "$stage/daxdiff.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=DAXDiff
Comment=Compare two Power BI semantic models
Exec=DAXDIFF_DIR/DAXDiff %f
Icon=DAXDIFF_DIR/daxdiff.png
Terminal=false
Categories=Office;Development;
DESKTOP
    cat > "$stage/install.sh" <<'INSTALL'
#!/usr/bin/env sh
# Adds DAXDiff to the application menu of the current user. The app stays in this folder.
set -e
dir="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$HOME/.local/share/applications"
sed "s#DAXDIFF_DIR#$dir#g" "$dir/daxdiff.desktop" > "$HOME/.local/share/applications/daxdiff.desktop"
echo "DAXDiff added to the application menu."
INSTALL
    chmod +x "$stage/install.sh" "$stage/DAXDiff"
    out="$dist/$name-$rid.tar.gz"
    tar -C "$root/publish/tar-$rid" -czf "$out" "$name"
    ;;

  *)
    echo "Unsupported runtime identifier: $rid" >&2
    exit 1
    ;;
esac

echo "Download: $out"
