#!/usr/bin/env bash
# Exports a self-contained Windows build to build/windows and zips it to build/AppStein-windows.zip.
# Needs the Godot 4.7.2 mono export templates installed (~/.local/share/godot/export_templates/4.7.2.stable.mono).
# Steam: the zip also gets steam_api64.dll (from third_party/steam/win64, or the file named in $STEAM_API_DLL) and steam_appid.txt next to the exe.
# The Steamworks.NET managed dll is part of the Godot export (data_AppStein_windows_x86_64). The script fails if one of them is missing
# or if steam_appid.txt does not match SteamConfig.AppId in src/Net/Steam/SteamConfig.cs.
set -euo pipefail
cd "$(dirname "$0")/.."
DLL="${STEAM_API_DLL:-third_party/steam/win64/steam_api64.dll}"
[ -f "$DLL" ] || { echo "ERROR: $DLL not found (the Steam native library, see third_party/steam/README.md)"; exit 1; }
[ -f steam_appid.txt ] || { echo "ERROR: steam_appid.txt missing"; exit 1; }
CONST=$(grep -oE 'AppId = [0-9]+' src/Net/Steam/SteamConfig.cs | grep -oE '[0-9]+')
FILE=$(tr -d '[:space:]' < steam_appid.txt)
[ "$CONST" = "$FILE" ] || { echo "ERROR: steam_appid.txt says $FILE but SteamConfig.AppId is $CONST; make them equal"; exit 1; }
rm -rf build/windows build/AppStein-windows.zip; mkdir -p build/windows
godot --headless --path . --export-release "Windows" build/windows/AppStein.exe
cp "$DLL" build/windows/steam_api64.dll
cp steam_appid.txt build/windows/steam_appid.txt
(cd build && mv windows AppStein && zip -qr AppStein-windows.zip AppStein && mv AppStein windows)
LIST=$(unzip -l build/AppStein-windows.zip)   # (a variable: "unzip | grep -q" can fail with SIGPIPE under pipefail)
for f in AppStein.exe steam_api64.dll steam_appid.txt; do
  grep -qE "AppStein/$f\$" <<<"$LIST" || { echo "ERROR: $f is not in the zip"; exit 1; }
done
grep -q "data_AppStein_windows_x86_64/Steamworks.NET.dll" <<<"$LIST" || { echo "ERROR: the Steamworks.NET managed dll is not in the zip"; exit 1; }
ls -la build/AppStein-windows.zip
