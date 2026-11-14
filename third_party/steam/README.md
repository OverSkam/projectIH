# Steam native library

`win64/steam_api64.dll` is Valve's Steamworks redistributable (the small loader library that every Steam game ships next to its exe).
`tools/build_windows.sh` copies it next to `AppStein.exe` together with `steam_appid.txt`. The managed wrapper (Steamworks.NET) comes from NuGet and is
included by the Godot export; the NuGet package does NOT ship this native DLL.

Origin of the file in this repository: the `steamworks.js` 0.4.0 npm package (`dist/win64/steam_api64.dll`), SHA-256
`1add7f151fa644870a735ae86e68d1f019f296130d8e7c0a7ed3ecc7482dccbc`. It is byte-identical to `Windows-x64/steam_api64.dll` in the official
Steamworks.NET 2024.8.0 standalone release (https://github.com/rlabrecque/Steamworks.NET/releases/tag/2024.8.0), i.e. the SDK 1.60 library
that Steamworks.NET 2024.8.0 is built for.
The SDK's license allows shipping this DLL with a game that is released on Steam.
