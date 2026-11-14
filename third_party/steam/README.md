# Steam native library

`win64/steam_api64.dll` is Valve's Steamworks redistributable (the small loader library that every Steam game ships next to its exe).
`tools/build_windows.sh` copies it next to `AppStein.exe` together with `steam_appid.txt`. The managed wrapper (Steamworks.NET) comes from NuGet and is
included by the Godot export; the NuGet package does NOT ship this native DLL.

Origin of the file in this repository: taken from the `steamworks.js` 0.4.0 npm package (`dist/win64/steam_api64.dll`), SHA-256
`1add7f151fa644870a735ae86e68d1f019f296130d8e7c0a7ed3ecc7482dccbc`. It exports every function Steamworks.NET 2024.8.0 imports for the parts this
game uses (checked by comparing the export names), but its exact SDK version is not stated in the file; Steamworks.NET 2024.8.0 is built for SDK 1.60.
If anything about Steam misbehaves, replace it with `redistributable_bin/win64/steam_api64.dll` from the Steamworks SDK 1.60
(free download for any Steamworks partner account: https://partner.steamgames.com/downloads/list) and rebuild.
The SDK's license allows shipping this DLL with a game that is released on Steam.
