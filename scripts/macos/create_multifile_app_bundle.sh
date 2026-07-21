#!/bin/bash

set -euo pipefail

if [ "$#" -ne 5 ]; then
    echo "usage: $0 <runtime-directory> <output-directory> <entitlements> <version> <source-revision>" >&2
    exit 64
fi

runtime_directory=$1
output_directory=$2
entitlements_path=$3
version=$4
source_revision=$5

script_directory=$(cd "$(dirname "$0")" && pwd)
repository_root=$(cd "$script_directory/../.." && pwd)
macos_resources="$repository_root/distribution/macos"
launcher_source="$script_directory/multifile_app_launcher.c"
app_directory="$output_directory/Ryujinx.app"
contents_directory="$app_directory/Contents"
macos_directory="$contents_directory/MacOS"
resources_directory="$contents_directory/Resources"
bundled_runtime_directory="$resources_directory/runtime"

if [ ! -x "$runtime_directory/Ryujinx" ]; then
    echo "Ryujinx runtime executable is missing: $runtime_directory/Ryujinx" >&2
    exit 66
fi

rm -rf "$app_directory"
mkdir -p "$macos_directory" "$bundled_runtime_directory" "$contents_directory/Frameworks"

cp -R "$runtime_directory"/. "$bundled_runtime_directory"
clang -arch arm64 -Os -o "$macos_directory/Ryujinx" "$launcher_source"
chmod u+x "$macos_directory/Ryujinx" "$bundled_runtime_directory/Ryujinx"

if [ -f "$bundled_runtime_directory/THIRDPARTY.md" ]; then
    mv "$bundled_runtime_directory/THIRDPARTY.md" "$resources_directory/THIRDPARTY.md"
fi

if [ -f "$bundled_runtime_directory/LICENSE.txt" ]; then
    mv "$bundled_runtime_directory/LICENSE.txt" "$resources_directory/LICENSE.txt"
fi

cp "$macos_resources/Info.plist" "$contents_directory/Info.plist"
cp "$macos_resources/Ryujinx.icns" "$resources_directory/Ryujinx.icns"
cp "$macos_resources/updater.sh" "$resources_directory/updater.sh"
cp "$macos_resources/Assets.car" "$resources_directory/Assets.car"
printf 'APPL????' > "$contents_directory/PkgInfo"

plutil -replace CFBundleLongVersionString -string "$version-$source_revision" "$contents_directory/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$contents_directory/Info.plist"
plutil -replace CFBundleVersion -string "$version" "$contents_directory/Info.plist"

if ! otool -l "$bundled_runtime_directory/Ryujinx" | grep -q '@executable_path'; then
    install_name_tool -add_rpath '@executable_path' "$bundled_runtime_directory/Ryujinx"
fi

while IFS= read -r -d '' file_path; do
    if file "$file_path" | grep -q 'Mach-O'; then
        codesign --force --sign - "$file_path"
    fi
done < <(find "$contents_directory" -type f -print0)

codesign --entitlements "$entitlements_path" --force --sign - "$bundled_runtime_directory/Ryujinx"
codesign --deep --entitlements "$entitlements_path" --force --sign - "$app_directory"
codesign --verify --deep --strict "$app_directory"
