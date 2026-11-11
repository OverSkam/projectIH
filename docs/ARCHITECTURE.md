# App Stein — Architecture (foundation)

Godot 4.7.2 .NET, C# only. Everything (scenes, meshes, UI) is built in code; `scenes/Main.tscn` is a single node with `src/Core/Main.cs`.
Game data lives in static C# registries (`src/Items/Defs.cs`), not `.tres`.

## Build / run / test
- Editor: open the folder in Godot 4.7.2 .NET, press Play (the editor builds the C# project itself). Main menu: Host / Join.
- CLI build: `dotnet build AppStein.csproj` (must end with 0 errors). First-time/CI import: `godot --headless --path . --import`.
- Command-line args (after `--`): `--host`, `--join=<ip>`, `--name=X`, `--port=N`, `--autostart=N` (host starts when N players are present),
  `--preset=debug` (2 short rounds, tiny quota), `--bot=<routine>` (`idle`, `miner`, `killer`, `pit`), `--timeout=<sec>`, `--botdebug`.
  Any of host/join/bot = "automated": the process quits itself after the match ends or the host disappears.
- Smoke test: `tools/smoke_test.sh` (env `PORT`, `LOGDIR`, `TIMEOUT`). Builds, imports, runs host (idle bot) + `Alice` (miner bot) + `Bob` (killer bot)
  as separate headless processes, debug preset, ~2.5 min. Processes print `PASS: <name>` / `FAIL: <name>`; the script requires every expected PASS, no
  FAIL, no `ERROR`/exception text, matching final money on server and clients, winner = Alice. Exit code non-zero on any problem.
  **Run it before every commit that touches gameplay or networking.** When adding a feature, add a PASS line in the code and the name to the script's list.

## Autoloads and nodes
- `Net` (`src/Net/Net.cs`): ENet listen-server (port 7777, 8 peers), lobby roster `Net.Names` (peer id -> name), events `LobbyChanged/Connected/ServerLost/PeerLeft`.
- `Game` (`src/Core/Game.cs`): match/round state machine (server) + **all in-round RPCs**. Phases: Lobby -> Round -> Summary -> (next Round | MatchEnd) -> Lobby.
- `World` (`src/World/World.cs`, child of `/root/Game/World`, re-created every round on every peer): `Geometry` (cave), `Miners` (children named by peer id),
  `Objects` (networked objects), registry `World.Objects[id]`.
- `Main` (scene root) owns UI layers: `MenuUI` (menu+lobby), `Hud`, `Screens` (round summary / match end). UI only reads `Game`/`Net` state and events.
- Server-only per-player record: `ServerPlayer` (`src/Player/ServerPlayer.cs`). Clients never have one.

## RPC and authority conventions (follow strictly)
- No MultiplayerSpawner/Synchronizer. Everything is an explicit RPC declared **on `Game`** (an always-existing node, so no "node not found" races).
- `Request*` / `Report*`: client -> server. `[Rpc(AnyPeer, CallLocal = true, Reliable)]`, called as `RpcId(1, ...)`. The handler starts with
  `if (!Net.I.IsServer) return; var p = Sender();` (`Sender()` maps remote id 0 to the local host) and then **validates** (alive, phase, cooldown, distance, slot space).
- `Client*`: server -> clients. `[Rpc(Authority, CallLocal = true, Reliable)]`; the server's own process runs the same handler, so the host is just another client.
- Movement: the owner simulates its miner and sends `ReportState` (unreliable, ~20 Hz); the server stores it in `ServerPlayer.Pos` and forwards `ClientState`
  to the other peers, which interpolate. Fall damage is reported by the owner (`ReportFall`) and clamped/validated by the server.
- Server-authoritative: health, damage, death, inventory, money, round state, world objects, loot bags.
- **Hidden information:** money and inventory go only to their owner via `RpcId(peer, nameof(ClientOwnState), ...)` (`SendOwnState`). The handler asserts
  `owner == MyId` and logs `FAIL: foreign_state_received` otherwise (the smoke test would catch a leak). Public: position, alive/dead, held utility, announcements,
  group quota, poorest miner name. Loot-bag contents stay on the server (`BagObj.Items`) until looted.
- Late joiners: `OnPeerRegistered` -> `ServerSendSnapshot` (begin-round as spectator + all objects + all living miners). They are ghosts until the next round.
- Physics layers: 1 world, 2 miners, 4 solid objects (veins, goblin), 8 pickups/bags (interaction rays only).

## Adding content
**Ore**: add an `OreDef` in `Ores` (`Defs.cs`): id, value, colour, glow, `MinDepth`, `Weight`. Veins/loose ore pick via `Ores.Pick(rng, roomDepth, round)`.
**Stein**: add a `SteinDef` in `Steins`: `Modify(StatBlock)` for passive stats (speed, jump, damage taken, melee, mining speed, sack bonus, regen, no-fall),
`OnTick(ServerPlayer, dt)` for per-tick server logic, `OnEvent(ServerPlayer, SteinEvent)` for `RoundStart/RoundEnd/DamageTaken/DamageDealt/Kill/Death/Deposit/Mined`.
Stats are recomputed by `ServerPlayer.RecalcStats()` whenever steins change; the owner receives speed/jump/no-fall in `ClientOwnState`. New stat = add a field to `StatBlock`
and use it where the server (or the owner via OwnState) applies it. `Steins.RandomCommon` picks the starting stein, so a new Common is automatically in the pool.
**Utility**: add a `UtilityDef` in `Utilities` (placeholders for Rope, Gas mask, Map, Dynamite, Bear trap exist with `Stub = true`). Held visuals are in `Miner.SetHeld`.
Behaviour for "use utility" (RMB, action `use_utility`) is not wired yet: add `Request UseUtility` in `Game`, validate on the server, replicate results as `Client*` RPCs.
**Interactable / networked world object**: subclass `NetObject` (`src/World/NetObjects.cs`), implement `Build()` (visuals + collision, layer 4 solid or 8 pickup),
`Prompt(inv)` (text starting with `"E"` makes it interactable), `ServerInteract(p)` and/or `ServerHit(p, mining, pickaxe)`; register in `NetObject.Factories`.
Spawn on the server with `Game.I.ServerSpawnObject(kind, pos, data)` (assigns the id, replicates), change with `ServerSetObjectData`, remove with `ServerDespawnObject`.
`Data` is a string (`Kv()` helper); anything secret must stay in server-only fields. `ApplyData` runs on every peer.
**Server-driven monster / trap**: make it a `NetObject` kind; the server runs its AI in `_PhysicsProcess` only when `Net.I.IsServer`, moves it, and replicates
position/state either through `ServerSetObjectData` at low rate or a new `Client*` RPC (unreliable for position). Damage goes through `ServerPlayer.TakeDamage(amount, source, cause)`
(applies stein modifiers and kills correctly). Spawn from cave markers in `Game.PopulateWorld` (markers `monster`, `trap`, `loot` already exist in the grey-box cave).
Use `World.Cave.FindPath` / `NavNodes` for coarse navigation.

## Cave builder
`ICaveBuilder.Build(Node3D parent, int seed, int round)` -> `CaveInfo` (`src/World/CaveInfo.cs`). It runs on **every peer** and must be deterministic for a given seed
(geometry only; networked objects are spawned by the server from the markers). Swap the implementation via `World.Builder`. `CaveInfo` contains:
`CampZone` (AABB; no PvP, safe from the collapse), `GoblinPos`, `MinerSpawns` (>= 8), `Rooms[]` (`Bounds`, `Center`, `Depth` from camp, `Markers[]` with
`Kind` = `vein | ore | monster | trap | loot` and `Pos`), and a waypoint graph `NavNodes/NavEdges` (`FindPath`) used by the bots. Keep the camp lit (OmniLights) and everything else dark;
the world environment has no ambient light (`World.BuildCave`). The current `GreyBoxCave` is a grid of axis-aligned rooms/tunnels with one pit (ramp out).

## Round/match rules implemented (`Game`, `MatchConfig`)
5 rounds (config), timer, shared quota (miss = 20% money loss for everyone), Goblin deposit (`GoblinObj`), collapse at timer end (everyone outside `CampZone` dies),
early end when all are dead, poorest-of-round cursed (60% health next round), survivor bonus when exactly one is alive, dead -> ghost (`Ghost`) -> respawn next round with the
start kit (Pickaxe + Lantern; survivors keep their carried items), match winner = most money. Tuning in `src/Core/Config.cs` (`Default`, `Debug`).

## Folder ownership for parallel work
| Workstream | Owns (edit freely) | Touch only via small, announced changes |
|---|---|---|
| Cave generation | `src/World/GreyBoxCave.cs` (or new generator files), `CaveInfo.cs`, `ICaveBuilder` impls, room modules, lighting in `World.BuildCave` | `Game.PopulateWorld` (marker kinds) |
| Items / steins / trials | `src/Items/*` (new files preferred over growing `Defs.cs`), new `NetObject` kinds for shrines/chests in a new file `src/Items/*Objects.cs` | `ServerPlayer`, `Game.RequestDrop`, `Miner.SetHeld` |
| Monsters | `src/Monsters/*` | `NetObject.Factories` registration line, `Game.PopulateWorld` spawn lines |
| Traps / hazards | `src/Traps/*` | same as monsters |
| UI / Goblin / screens | `src/UI/*`, `src/Goblin/*` (real Goblin model/dialogue replaces `GoblinObj` visuals) | `Game` events (`PhaseChanged`, `FeedChanged`, `OwnChanged`) |
| Core (networking, match loop, miner) | `src/Core/Game.cs`, `src/Net/*`, `src/Player/*` | everyone: coordinate before editing |
To avoid merge conflicts in `Game.cs`, put new gameplay RPCs in a `partial class Game` in your own file (e.g. `src/Monsters/Game.Monsters.cs`);
`Game` is already `partial`.

## Known limitations / stubs
- Ghost meddling is a stub (`RequestMeddle`: cooldown + announcement only). Utility "use" (RMB) and stein/legendary mechanics (Blood, Infinity), Hush/Owl/Strongarm/Feather etc. are not implemented.
- Respawned players get only the start kit (no stein) — design leaves this open.
- Hit detection is a single server ray from the reported eye position; remote miners on the server are interpolated, so very fast movers can be slightly off. No lag compensation.
- Movement is client-authoritative (speed/teleport are not validated). Fall damage is trusted within clamps.
- Peer ids are Godot's random ints (not 1..8). Quota/round-time numbers are first guesses (see `MatchConfig.Default`).
- Not verifiable headless: visuals, lighting, input feel, mouse capture/pause menu behaviour, HUD layout. Bots navigate only the grey-box cave via its waypoint graph.
- A hard-killed host is noticed by clients only after the ENet timeout (~30 s); a normal quit disconnects immediately.

## Extension points for parallel content work (`src/Core/Seams.cs`) — use these instead of editing shared files
- **Content modules:** a static class marked `[ContentModule]` with `public static void Register()` runs once on every peer at startup. Do all your
  registration there (`NetObject.Factories["kind"] = ...`, `Game.MarkerSpawners["kind"] = ...`, event subscriptions). One module class per workstream, in its own folder.
  Do NOT edit the `Factories` initializer or `Game.PopulateWorld`.
- **Marker spawners:** `Game.MarkerSpawners[kind] = (game, room, marker, rng) => ...` is called on the server for every cave marker of that kind each round.
  Kinds: `monster`, `dragon` (Monsters); `trap`, `lever` (Traps); `loot`, `chest`, `shrine` (Items). `Game.RoundPopulated` fires afterwards.
- **Noise:** call `Game.ServerNoise(pos, loudnessMetres, byPlayer)` on the server when something is loud; subscribe to `Game.NoiseMade` to react (monsters, rockfalls).
- **Damage causes:** pass a cause string to `ServerPlayer.TakeDamage`: `melee`, `fall`, `gas`, `fire`, `crush`, `spikes`, `monster`, `explosion`, `collapse`.
  `gas` is ignored when `Stats.GasImmune`.
- **StatBlock** already has `GasImmune`, `DarkVision`, `Luck`, `NoiseMul` (owned by the Items workstream; others only read them).
- **Physics layers:** `Layers.World/Miners/Solid/Pickup/ThinWall/Monsters`. Thin (Ethereal) walls are StaticBodies on `Layers.ThinWall` built by the cave.
- **Water:** `CaveInfo.WaterZones` / `InWater(pos)`.
- **New RPCs:** in your own `partial class Game` file inside your folder.

## Seeing the game
`tools/screenshot.sh <outdir> <frames> <game args>` renders off-screen and writes PNG frames (10 frames = 1 s). Open a PNG with your image-reading tool to check
visuals. Software rendering is slow: keep it to a few hundred frames.
