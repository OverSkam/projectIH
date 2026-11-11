#!/usr/bin/env bash
# Exports a self-contained Windows build to build/windows and zips it to build/AppStein-windows.zip.
# Needs the Godot 4.7.2 mono export templates installed (~/.local/share/godot/export_templates/4.7.2.stable.mono).
set -euo pipefail
cd "$(dirname "$0")/.."
rm -rf build/windows build/AppStein-windows.zip; mkdir -p build/windows
godot --headless --path . --export-release "Windows" build/windows/AppStein.exe
(cd build && mv windows AppStein && zip -qr AppStein-windows.zip AppStein && mv AppStein windows)
ls -la build/AppStein-windows.zip
