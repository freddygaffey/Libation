#!/bin/bash
# Builds Kokoro on the GPU for iPhones: mlalma/kokoro-ios (MLX Swift, with the misaki pronunciations the HSC library
# used) plus KokoroGPU-src/CAPI.swift, a C interface the .NET app calls. Writes KokoroGPU/ (frameworks and resource
# bundles, about 43 MB, not in git) and KokoroGPU.props, which the app project imports when it exists. Devices
# only: MLX does not run in the simulator. Needs Xcode's Metal toolchain (xcodebuild -downloadComponent MetalToolchain).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK=$(mktemp -d)
git clone -q https://github.com/mlalma/kokoro-ios.git "$WORK/kokoro-ios"
git -C "$WORK/kokoro-ios" checkout -q 1.0.8 2>/dev/null || true
cp "$HERE/KokoroGPU-src/CAPI.swift" "$WORK/kokoro-ios/Sources/KokoroSwift/"
(cd "$WORK/kokoro-ios" && xcodebuild archive -scheme KokoroSwift -destination 'generic/platform=iOS' \
  -archivePath "$WORK/ios.xcarchive" -derivedDataPath "$WORK/dd" SKIP_INSTALL=NO -configuration Release -quiet)
PRODUCTS="$WORK/dd/Build/Intermediates.noindex/ArchiveIntermediates/KokoroSwift/BuildProductsPath/Release-iphoneos"
rm -rf "$HERE/KokoroGPU" && mkdir -p "$HERE/KokoroGPU/Frameworks" "$HERE/KokoroGPU/Bundles"
cp -RL "$WORK/ios.xcarchive/Products/usr/local/lib/"*.framework "$HERE/KokoroGPU/Frameworks/"
for b in KokoroSwift_KokoroSwift MisakiSwift_MisakiSwift mlx-swift_Cmlx; do cp -RL "$PRODUCTS/$b.bundle" "$HERE/KokoroGPU/Bundles/"; done
{
  echo '<Project>'
  echo '  <!-- Written by Native/build-kokoro-gpu.sh. -->'
  echo '  <ItemGroup Condition="'"'"'$(RuntimeIdentifier)'"'"' == '"'"'ios-arm64'"'"'">'
  for f in "$HERE"/KokoroGPU/Frameworks/*.framework; do
    echo "    <NativeReference Include=\"Native/KokoroGPU/Frameworks/$(basename "$f")\"><Kind>Framework</Kind></NativeReference>"
  done
  echo '    <BundleResource Include="Native/KokoroGPU/Bundles/**" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />'
  echo '  </ItemGroup>'
  echo '</Project>'
} > "$HERE/KokoroGPU.props"
rm -rf "$WORK"
echo "Kokoro GPU frameworks in $HERE/KokoroGPU"
