#!/bin/bash
# Render the platform icon files from icon.svg and icon-foreground.svg. Needs rsvg-convert (brew install librsvg).
set -euo pipefail
cd "$(dirname "$0")"
SOURCE=../..
IOS=$SOURCE/LibationMobile.iOS/Assets.xcassets/AppIcon.appiconset
ANDROID=$SOURCE/LibationMobile.Android/Resources

mkdir -p "$IOS" "$ANDROID/mipmap-xxxhdpi" "$ANDROID/drawable"
# iOS takes one opaque 1024px image and makes every size itself; it rounds the corners.
rsvg-convert -w 1024 -h 1024 icon.svg -o "$IOS/icon-1024.png"
# iOS launch screen mark, at 1x, 2x and 3x.
LAUNCH=$SOURCE/LibationMobile.iOS/Assets.xcassets/LaunchMark.imageset
mkdir -p "$LAUNCH"
for scale in 1 2 3; do
  rsvg-convert -w $((200 * scale)) -h $((200 * scale)) icon-foreground.svg -o "$LAUNCH/launch-mark@${scale}x.png"
done
# Android: adaptive foreground at 432px (108dp at xxxhdpi), and a plain square for the few places that
# still ask for a bitmap.
rsvg-convert -w 432 -h 432 icon-foreground.svg -o "$ANDROID/mipmap-xxxhdpi/ic_launcher_foreground.png"
rsvg-convert -w 512 -h 512 icon.svg -o "$SOURCE/LibationMobile.Android/Icon.png"
echo "Rendered iOS and Android icons."
