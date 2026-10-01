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
for target in LibationWidgetExtension LibationWidgetBridge; do
	xcodebuild -quiet -project LibationWidget.xcodeproj -scheme "$target" -configuration Release \
		-destination 'generic/platform=iOS' -derivedDataPath build -allowProvisioningUpdates \
		DEVELOPMENT_TEAM="$LIBATION_TEAM" build
	xcodebuild -quiet -project LibationWidget.xcodeproj -scheme "$target" -configuration Release \
		-destination 'generic/platform=iOS Simulator' -derivedDataPath build \
		CODE_SIGNING_ALLOWED=NO build
done

# The app's build signs the widget again, along with everything else in the app; it needs the entitlements
# Xcode gave it, with the team's prefix filled in.
products=build/Build/Products
codesign -d --entitlements - --xml "$products/Release-iphoneos/LibationWidgetExtension.appex" > "$products/LibationWidgetExtension.entitlements"
echo "Widget built: $products"
