#!/bin/bash
# Builds espeak-ng.xcframework (eSpeak NG 1.52.0, static, iPhone and simulator) from source into this folder.
# Its English data comes from the same version, in ../Resources/espeak-ng-data.
#
# The library alone, compiled directly: eSpeak's CMake install rules fail for iOS. Changes from a default build:
# no audio output, MBROLA, sonic or speech-player; Klatt kept; a 1024-byte data path (the default 160 is shorter than
# the simulator's bundle path); and leNNtoh from OSByteOrder, which the iOS SDK's <endian.h> lacks.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK=$(mktemp -d)
git clone -q --depth 1 --branch 1.52.0 https://github.com/espeak-ng/espeak-ng.git "$WORK/src"
cp "$HERE/config.h" "$HERE/apple_endian.h" "$WORK/"
SRCS="common mnemonics error ieee80 compiledata compiledict dictionary encoding intonation langopts numbers phoneme phonemelist readclause setlengths soundicon spect ssml synthdata synthesize tr_languages translate translateword voices wavegen speech espeak_api klatt"
build() {
  local sdk=$1 target=$2 out="$WORK/out/$3"
  local sysroot=$(xcrun --sdk $sdk --show-sdk-path)
  mkdir -p "$out/obj"
  for s in $SRCS; do
    xcrun --sdk $sdk clang -c -O2 -target $target -isysroot $sysroot -fPIC -fno-exceptions -fwrapv -w \
      -DLIBESPEAK_NG_EXPORT=1 -DN_PATH_HOME=1024 -include "$WORK/apple_endian.h" '-DPATH_ESPEAK_DATA=""' \
      -I"$WORK" -I"$WORK/src/src/include" -I"$WORK/src/src/include/compat" -I"$WORK/src/src/ucd-tools/src/include" -I"$WORK/src/src/libespeak-ng" \
      "$WORK/src/src/libespeak-ng/$s.c" -o "$out/obj/$s.o"
  done
  for s in case categories ctype proplist scripts tostring; do
    xcrun --sdk $sdk clang -c -O2 -target $target -isysroot $sysroot -fPIC -w -I"$WORK/src/src/ucd-tools/src/include" \
      "$WORK/src/src/ucd-tools/src/$s.c" -o "$out/obj/ucd_$s.o"
  done
  xcrun --sdk $sdk libtool -static -o "$out/libespeak-ng.a" "$out"/obj/*.o
}
build iphoneos arm64-apple-ios16.0 ios-arm64
build iphonesimulator arm64-apple-ios16.0-simulator ios-arm64-simulator
mkdir -p "$WORK/headers" && cp "$WORK"/src/src/include/espeak-ng/*.h "$WORK/headers/"
rm -rf "$HERE/espeak-ng.xcframework"
xcodebuild -create-xcframework \
  -library "$WORK/out/ios-arm64/libespeak-ng.a" -headers "$WORK/headers" \
  -library "$WORK/out/ios-arm64-simulator/libespeak-ng.a" -headers "$WORK/headers" \
  -output "$HERE/espeak-ng.xcframework"
rm -rf "$WORK"
