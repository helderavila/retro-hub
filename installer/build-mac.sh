#!/bin/zsh
# Gera dist/RetroHub-<versão>-macos-<arch>.dmg:
#   1. dotnet publish self-contained (o .NET vai junto — quem instala não precisa de nada);
#   2. monta "Retro Hub.app" (Info.plist, ícone a partir do logo);
#   3. assina (ad-hoc por padrão; SIGN_IDENTITY="Developer ID Application: …" para distribuir) e cria o .dmg.
# Uso:  installer/build-mac.sh [arm64|x64]      (padrão: arquitetura deste Mac)
set -euo pipefail

root=${0:a:h:h}
arch=${1:-$([ "$(uname -m)" = arm64 ] && echo arm64 || echo x64)}
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
dist="$root/dist"
publish="$dist/publish-macos-$arch"
app="$dist/Retro Hub.app"
dotnet=${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}

echo "Retro Hub $version · macOS $arch"
rm -rf "$publish" "$app"

"$dotnet" publish "$root/src/RetroHub.Mac/RetroHub.Mac.csproj" \
    -c Release -r "osx-$arch" --self-contained true \
    -p:DebugType=none -o "$publish" --nologo -v q

# ── App bundle ──────────────────────────────────────────────────────────────
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
ditto "$publish" "$app/Contents/MacOS"

# Icon: the logo mark at every size macOS asks for
iconset="$dist/RetroHub.iconset"
rm -rf "$iconset" && mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -z $size $size "$root/src/RetroHub.App/Assets/logo-mark.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$root/src/RetroHub.App/Assets/logo-mark.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/RetroHub.icns"
rm -rf "$iconset"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Retro Hub</string>
  <key>CFBundleDisplayName</key><string>Retro Hub</string>
  <key>CFBundleIdentifier</key><string>com.retrohub.app</string>
  <key>CFBundleExecutable</key><string>RetroHub</string>
  <key>CFBundleIconFile</key><string>RetroHub.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.games</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Retro Hub</string>
</dict>
</plist>
PLIST

# ── Signature ───────────────────────────────────────────────────────────────
# Ad-hoc ("-") runs on this Mac; others see the "unidentified developer" prompt (right click › Open).
# With a Developer ID, notarize the .dmg afterwards: xcrun notarytool submit … && xcrun stapler staple …
# Every file in Contents/MacOS is signed on its own first: --deep skips loose dylibs there (the .NET ones would keep
# Microsoft's signature, which macOS refuses to load into a process signed by someone else), and Apple treats
# everything in that folder as code, so the managed .dll files need a signature too.
identity=${SIGN_IDENTITY:--}
if [ "$identity" = "-" ]; then
    sign=(codesign --force --timestamp=none -s -)
else
    sign=(codesign --force --timestamp --options runtime --entitlements "$root/installer/RetroHub.entitlements" -s "$identity")
fi
find "$app/Contents/MacOS" -type f ! -name RetroHub -print0 | xargs -0 -n 50 "${sign[@]}"
"${sign[@]}" "$app"
codesign --verify --deep --strict "$app"

# ── Disk image ──────────────────────────────────────────────────────────────
dmg="$dist/RetroHub-$version-macos-$arch.dmg"
staging="$dist/dmg-staging"
rm -rf "$staging" "$dmg" && mkdir -p "$staging"
ditto "$app" "$staging/Retro Hub.app"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "Retro Hub" -srcfolder "$staging" -ov -format UDZO "$dmg" >/dev/null
rm -rf "$staging"

echo "App:   $app"
printf "Disco: %s (%.1f MB)\n" "$dmg" $(( $(stat -f%z "$dmg") / 1048576.0 ))
