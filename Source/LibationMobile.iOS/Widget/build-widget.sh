#!/bin/bash
# Builds the home-screen widget and the app's Swift bridge to it, for the phone and the simulator. The app's
# csproj embeds them when they have been built, and leaves them out otherwise.
#
#   LIBATION_TEAM=<Apple team ID> ./build-widget.sh
#
# Signing is automatic: Xcode registers the widget's ID (the app's, plus .widget) under that team.
set -euo pipefail
cd "$(dirname "$0")"
: "${LIBATION_TEAM:?Set LIBATION_TEAM to the Apple developer team ID that signs the app}"

xcodegen generate --quiet
# Debug, not Release: on the phone, a Release build of the widget drew its buttons but iOS never ran their
# actions, while the same code built for Debug works (found 2026-10-04 on iOS 26.6; the binaries differ only in
# how Swift optimised them).
for target in LibationWidgetExtension LibationIntentsExtension LibationWidgetBridge; do
	xcodebuild -quiet -project LibationWidget.xcodeproj -scheme "$target" -configuration Debug \
		-destination 'generic/platform=iOS' -derivedDataPath build -allowProvisioningUpdates \
		DEVELOPMENT_TEAM="$LIBATION_TEAM" build
	xcodebuild -quiet -project LibationWidget.xcodeproj -scheme "$target" -configuration Debug \
		-destination 'generic/platform=iOS Simulator' -derivedDataPath build \
		CODE_SIGNING_ALLOWED=NO build
done

# The app's build signs the widget again, along with everything else in the app; it needs the entitlements
# Xcode gave it, with the team's prefix filled in.
products=build/Build/Products
codesign -d --entitlements - --xml "$products/Debug-iphoneos/LibationWidgetExtension.appex" > "$products/LibationWidgetExtension.entitlements"
codesign -d --entitlements - --xml "$products/Debug-iphoneos/LibationIntentsExtension.appex" > "$products/LibationIntentsExtension.entitlements"
echo "Widget built: $products"
