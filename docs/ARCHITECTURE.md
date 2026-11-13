# App Stein — Architecture (v2 rules)

Godot 4.7.2 .NET, C# only. Everything (scenes, meshes, UI) is built in code; `scenes/Main.tscn` is a single node with `src/Core/Main.cs`.
Game data lives in static C# registries (`src/Items/Defs.cs`), not `.tres`.

## Build / run / test
- Editor: open the folder in Godot 4.7.2 .NET, press Play (the editor builds the C# project itself). Main menu: Host / Join.
- CLI build: `dotnet build AppStein.csproj` (must end with 0 errors). First-time/CI import: `godot --headless --path . --import`.
- Command-line args (after `--`): `--host`, `--join=<ip>`, `--name=X`, `--port=N`, `--autostart=N` (host starts when N players are present),
  `--preset=debug` (60 s rounds, quick weighing ceremony, 10 s downed time), `--preset=soak`, `--roundsecs=N` (round length override), `--tribute=Name=N,...`
  (server: sets that tribute weight at every round start; test/screenshot staging), `--give=[Name=]stein:id|util:id|ore:id`,
  `--bot=<routine>` (`idle`, `miner`, `killer`, `pit`, `explorer`, `t_*` item-test bots), `--timeout=<sec>`, `--botdebug`, `-- --weighcheck` (headless self-check of the elimination rules).
  Any of host/join/bot = "automated": the process quits itself after the match ends or the host disappears.
- Smoke test: `tools/smoke_test.sh` (env `PORT`, `LOGDIR`, `TIMEOUT`). Builds, imports, runs the weighing-rules self-check, then host (idle bot) + `Alice` (miner bot)
  + `Bob` (killer bot) as separate headless processes, debug preset, ~3 min: Alice feeds (tribute credited to her client only), Bob hides outside the Hall and
  weighs zero, the reveal sequence is identical on every peer, Bob is thrown and stays a ghost, round 2 is the final two, Alice wins. Processes print
  `PASS: <name>` / `FAIL: <name>`; the script requires every expected PASS, no FAIL, no `ERROR`/exception text. Exit code non-zero on any problem.
  **Run it before every commit that touches gameplay or networking.** When adding a feature, add a PASS line in the code and the name to the script's list.

## Autoloads and nodes
- `Net` (`src/Net/Net.cs`): ENet listen-server (port 7777, 8 peers), lobby roster `Net.Names` (peer id -> name), events `LobbyChanged/Connected/ServerLost/PeerLeft`.
- `Game` (`src/Core/Game.cs` + partials `Game.Weighing.cs`, `Game.Downed.cs`, `Items/Game.Items.cs`): match/round state machine (server) + **all in-round RPCs**.
  Phases: Lobby -> Round -> Weighing -> (next Round | MatchEnd) -> Lobby. Public per-miner state: `Game.Status` (Alive / Downed / Dead / Eliminated).
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
- Server-authoritative: health, damage, downed/death, inventory, tribute weight, lantern oil, round state, the weighing, world objects, loot bags.
- **Hidden information:** tribute weight, inventory and oil go only to their owner via `RpcId(peer, nameof(ClientOwnState), ...)` (`SendOwnState`) and
  `ClientFeedAck`. The handlers assert `owner == MyId` and log `FAIL: foreign_state_received` / `foreign_feed_ack` otherwise (the smoke test would catch a leak).
  Public: position, status, downed, held utility, whether a lantern is lit / flickering / out, announcements, Goblin cues (`ClientGoblinCue`: category + seed, every peer picks the same line),
  and everything the weighing reveals (a pile's weight is sent only when it is revealed). Loot-bag contents stay on the server (`BagObj.Items`) until looted.
- Late joiners: `OnPeerRegistered` -> they never were miners: marked `Eliminated` (ghost for the rest of the match), `ServerSendSnapshot` if a round is running.
  A miner who disconnects is eliminated at once (`OnPeerLeft`); if at most one miner remains at any moment the match ends with them as winner (`CheckMatchOver`).
- Physics layers: 1 world, 2 miners, 4 solid objects (veins, goblin), 8 pickups/bags (interaction rays only).

## Adding content
**Ore**: add an `OreDef` in `Ores` (`Defs.cs`): id, `TributeWeight` (weight on the Scale), colour, glow, `MinDepth`, `SpawnWeight` (relative pick chance; 0 = never spawns, e.g. the Midas `statue`).
Veins/loose ore pick via `Ores.Pick(rng, roomDepth, round)`. Power steins weigh `Steins.TributeOf(id)` when fed (Common 6, Rare 12, Legendary 20).
**Stein**: add a `SteinDef` in `Steins`: `Modify(StatBlock)` for passive stats (speed, jump, damage taken, melee, mining speed, sack bonus, regen, no-fall),
`OnTick(ServerPlayer, dt)` for per-tick server logic, `OnEvent(ServerPlayer, SteinEvent)` for `RoundStart/RoundEnd/DamageTaken/DamageDealt/Kill/Death/Fed/Mined`
(`RoundEnd` fires after the lock-out, before the weighing piles are built: the Blood penalty lives there).
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
(applies stein modifiers and kills correctly). Spawn from cave markers (`Game.MarkerSpawners`). Monsters are unkillable: derive from `MonsterObj`, use `Stun(seconds)` for anything that should interrupt them.
Use `World.Cave.FindPath` / `NavNodes` for coarse navigation.

## Cave builder
`ICaveBuilder.Build(Node3D parent, int seed, int round)` -> `CaveInfo` (`src/World/CaveInfo.cs`). It runs on **every peer** and must be deterministic for a given seed
(geometry only; networked objects are spawned by the server from the markers). Swap the implementation via `World.Builder`. `CaveInfo` contains:
the **Hall of the Scale seams**: `HallZone` (AABB; the miners inside it when the timer ends are counted; no PvP, monsters/traps/explosions do nothing there; `InHall(p)`; the old name
`CampZone` is kept as an alias property on `CaveInfo`), `GoblinPos`, `PalmPos` (the Goblin's open palm: the feeding point; stones are fed by interacting with the Goblin object, steins by
holding interact, validated within 5.5 m of the palm), `ScalePos` (centre of the weighing platform; `HallScale` builds placeholder visuals there), `WeighSpots` (>= 8 standing places inside the Hall,
one per miner in peer-id order, all facing `ScalePos`; `MakeWeighSpots` lays them on an arc), `ShaftPos` (where the thrown miner falls into the dark),
`MinerSpawns` (>= 8), `Rooms[]` (`Bounds`, `Center`, `Depth` from the Hall, `Markers[]` with `Kind` = `vein | ore | monster | dragon | trap | lever | loot | chest | shrine | oil` and `Pos`),
and a waypoint graph `NavNodes/NavEdges` (`FindPath`) used by the bots. Keep the Hall lit (OmniLights) and everything else dark; the world environment has no ambient light (`World.BuildCave`).
`--cavecheck` verifies the seams (spots inside the Hall, >= 8, distinct). `ModularCave` uses the `camp` module as the Hall (legend char `i` = oil drum marker); `GreyBoxCave` is the old grid cave.

## Round/match rules implemented (`Game`, `MatchConfig`)
- N miners = N-1 rounds (`MatchConfig.RoundsFor`). Round timer: 6 min +1 per round up to 10 (`Config.cs`; presets `debug`, `soak`).
- **Tribute:** `ServerPlayer.TributeWeight` (server-authoritative, owner-only, reset to 0 after every weighing). `Game.ServerFeed` feeds the whole sack (tap interact on the Goblin),
  `Game.ServerFeedStein` one stein (hold interact ~1 s at the Goblin, `RequestFeedStein`, shows a hold bar). Fed items are gone. Goblin cues `feed_small/feed_large/feed_stein` are public.
- **Timer end -> `ServerBeginWeighing`** (phase `Weighing`): a living miner outside `HallZone` weighs zero and loses everything carried (`NeedsKit`: back next round with pickaxe + lantern, no stein);
  `RoundEnd` stein events run (Blood, Infinity; an Infinity win ends the match before any weighing); piles are built; everyone left in the match (also ghosts) is revived and placed on `WeighSpots`.
  Early end when every miner in the cave is dead.
- **Weighing ceremony** (`Game.Weighing.cs`, server timeline in `AdvanceWeighing`, client state `Game.Weigh` + `WeighChanged`): Intro -> Reveal x N (heaviest first, lightest last; `ClientWeighReveal`
  carries the weight) -> Verdict (`Weighing.Resolve`) -> Throw (`ClientWeighThrow`: `Miner.StartThrow` lifts the miner to the palm, over `ShaftPos` and drops them; the victim's camera is on the miner) -> "N miners remain" card
  -> next round, or match end after a final weighing. Presentation: `WeighingUi` (overlay), `HallScale` (placeholder platform + number in the world), Goblin lines via `GoblinCue`. Timings are in `MatchConfig`.
  Rules (`Weighing.Resolve`, unit-checked by `--weighcheck`): lightest thrown; ties: all tied thrown unless that leaves nobody (then one at random); final two: heavier wins (tie: random).
- **Hooks for later steps** (server): `Weighing.Prepare(WeighingContext)` once before the first reveal (Liar swap, gifting, Pact stone: change piles only through `WeighingContext.Add/Set/Swap`),
  `Weighing.BeforeReveal(WeighingContext, WeighPile)` right before each miner's pile is revealed (per-miner step).
- **Eliminated players** stay as ghosts: everything the role does is behind `EliminatedRole` (`OnEliminated`, `OnRoundStart` on the server, `ClientEnter` on the client). The Turned replace its bodies.
- **Downed** (`Game.Downed.cs`): at 0 health `DownMiner` -> `ServerPlayer.Downed` for `Config.DownedSeconds` (30): crawls at 12% speed, cannot act (`Game.CanAct`), visible lying pose (`Miner.IsDowned`),
  HUD countdown; `RequestLoot` takes one item per press (sack, steins, utilities); further damage or the timer kills (`KillMiner`: bag, ghost, back next round). Seam for dragging / reviving: `ServerRevive`,
  `ServerPlayer.DownedBy`, `Miner.LootTarget`.
- **Lantern oil** (`ItemsServer.TickLantern`): `ServerPlayer.Oil` (seconds, `Config.LanternOilSeconds` = 240, full at every round start, owner-only), `LanternOn` (toggle key L, `RequestToggleLantern`).
  Oil burns only while the lantern is lit and held. The public light state (0 lit, 1 out, 2 flickering at <= 25%) goes through `ClientLantern`. `OilDrumObj` (marker `oil`, `Config.OilDrumRefills` each) refills.
- **Monsters are unkillable:** `MonsterObj.Stun(seconds)` (state `Stunned`); Golem back-crystal hit 6 s, dynamite 2.5 s, bear trap 3 s. Ember Elemental and Gas Wisp classes stay but are no longer spawned. Dragon unchanged.
- Death in the cave is not elimination. Midas: a miner killed by the holder leaves a `statue` stone (weight 15) instead of a bag. Blood: no kill by the weighing -> stein lost and half of this round's tribute.

## Folder ownership for parallel work
| Workstream | Owns (edit freely) | Touch only via small, announced changes |
|---|---|---|
| Cave generation | `src/World/GreyBoxCave.cs` (or new generator files), `CaveInfo.cs`, `ICaveBuilder` impls, room modules, lighting in `World.BuildCave` | `Game.PopulateWorld` (marker kinds) |
| Items / steins / trials | `src/Items/*` (new files preferred over growing `Defs.cs`), new `NetObject` kinds for shrines/chests in a new file `src/Items/*Objects.cs` | `ServerPlayer`, `Game.RequestDrop`, `Miner.SetHeld` |
| Monsters | `src/Monsters/*` | `NetObject.Factories` registration line, `Game.PopulateWorld` spawn lines |
| Traps / hazards | `src/Traps/*` | same as monsters |
| UI / Goblin / screens | `src/UI/*`, `src/Goblin/*` (real Goblin model/dialogue replaces `GoblinObj` visuals; `HallScale` replaces the placeholder Scale) | `Game` events (`PhaseChanged`, `FeedChanged`, `OwnChanged`, `WeighChanged`, `GoblinCue`) |
| Core (networking, match loop, miner) | `src/Core/Game.cs`, `src/Net/*`, `src/Player/*` | everyone: coordinate before editing |
To avoid merge conflicts in `Game.cs`, put new gameplay RPCs in a `partial class Game` in your own file (e.g. `src/Monsters/Game.Monsters.cs`);
`Game` is already `partial`.

## Known limitations / stubs
- Ghost meddling is small: right mouse snuffs the nearest lantern for 6 s, E knocks a pebble (cooldown 6 s). Eliminated players are ghosts until the Turned exist.
- Hit detection is a single server ray from the reported eye position; remote miners on the server are interpolated, so very fast movers can be slightly off. No lag compensation.
- Movement is client-authoritative (speed/teleport are not validated). Fall damage is trusted within clamps.
- Peer ids are Godot's random ints (not 1..8). Timings and tribute numbers are first guesses (`Config.cs`, `Defs.cs`).
- Only one thrown miner gets the Goblin's reaching arm at a time (a tie throws the others without a hand). `--cave=greybox` is a legacy test cave: the giant does not fit its small hall (visuals poke through the walls; positions and rules still work).
- Not verifiable headless: visuals, lighting, input feel, mouse capture/pause menu behaviour, HUD layout. Bots navigate only via the nav graph.
- A hard-killed host is noticed by clients only after the ENet timeout (~30 s); a normal quit disconnects immediately.

## Extension points for parallel content work (`src/Core/Seams.cs`) — use these instead of editing shared files
- **Content modules:** a static class marked `[ContentModule]` with `public static void Register()` runs once on every peer at startup. Do all your
  registration there (`NetObject.Factories["kind"] = ...`, `Game.MarkerSpawners["kind"] = ...`, event subscriptions). One module class per workstream, in its own folder.
  Do NOT edit the `Factories` initializer or `Game.PopulateWorld`.
- **Marker spawners:** `Game.MarkerSpawners[kind] = (game, room, marker, rng) => ...` is called on the server for every cave marker of that kind each round.
  Kinds: `monster`, `dragon` (Monsters); `trap`, `lever` (Traps); `loot`, `chest`, `shrine` (Items). `Game.RoundPopulated` fires afterwards.
- **Noise:** call `Game.ServerNoise(pos, loudnessMetres, byPlayer)` on the server when something is loud; subscribe to `Game.NoiseMade` to react (monsters, rockfalls).
- **Damage causes:** pass a cause string to `ServerPlayer.TakeDamage`: `melee`, `fall`, `gas`, `fire`, `crush`, `spikes`, `monster`, `explosion`, `collapse` (the rockfall lever). Damage that takes health to 0 downs the miner.
  `gas` is ignored when `Stats.GasImmune`.
- **StatBlock** already has `GasImmune`, `DarkVision`, `Luck`, `NoiseMul` (owned by the Items workstream; others only read them).
- **Physics layers:** `Layers.World/Miners/Solid/Pickup/ThinWall/Monsters`. Thin (Ethereal) walls are StaticBodies on `Layers.ThinWall` built by the cave.
- **Water:** `CaveInfo.WaterZones` / `InWater(pos)`.
- **New RPCs:** in your own `partial class Game` file inside your folder.

## Seeing the game
`tools/screenshot.sh <outdir> <frames> <game args>` renders off-screen and writes PNG frames (10 frames = 1 s). Open a PNG with your image-reading tool to check
visuals. Software rendering is slow: keep it to a few hundred frames.

## Items workstream (src/Items)
Registered by `ItemsModule` ([ContentModule]). Steins incl. Blood/Infinity (`ItemsServer`, `InfinityTracker`), chests/shrines/loose loot (`Loot`, `WorldObjects.cs`),
utilities via RMB (`Game.RequestUseUtility` -> `ItemsServer.UseHeld`: dynamite, bear trap, rope), active stein key Q (`stein_ability`), L toggles the lantern, F cycles the highlighted stein slot
(swap target), drop ore moved to T. Monsters can implement `IRootable` (bear trap: stun) and `IExplodable` (dynamite: stun). Owner-only item state goes through `ClientItemState`.
Tests: `tools/items_test.sh` (scripted by `--itest=util|infinity|tie|disconnect`, `--give=`, `--debugspawn=`; bots `--bot=t_*`, see `ItemsDebug.cs`; elimination is steered by setting tribute weights).
## Modular cave (src/World/Cave)
Default builder `ModularCave` (`--cave=greybox` selects the old one). 14x14-cell (2 m) hand-built modules in `CaveModules.cs` (ASCII legend in the file), rotated and joined on an 18-cell slot grid.
Debug: `-- --cavecheck` (headless self-check, seeds 1-20 x rounds 1-5), `--caveseed=N --caveround=N`, `--cavelist`, screenshots with `--camroom=N [--camat=<marker kind>] [--camoff=x,y,z --camyaw=deg --campitch=deg]` or `--cam=x,y,z`.
Nav graph = one node per walkable cell (crawl, thin-wall and Hall-furniture cells excluded); `FindPath` is A*. Miners collide with layer 16 (thin walls).


## Step 1B: the look and the space
**Hall of the Scale** (the `camp` module, ceiling 12 m, north door never connected). Local frame (origin = hall centre `h0`, x east, z south): the giant Goblin sits at the north wall
(`CaveInfo.GoblinSeat` = h0 + (0,0,-9.2)); the `GoblinObj` network object (`GoblinPos`) sits on the floor under his feeding palm (seat + (-4.2,0,4.4)); `PalmPos` = GoblinPos + 1.4 m up; bots and the
feed prompt stand 2.3 m in front of `GoblinPos`. `ScalePos` = h0 + (0,0,-1.8) with the gantry posts at x = +-6.2; `ShaftPos` = h0 + (9.4,0,-2); `WeighSpots` = 8 places on a 3.2 m arc around the Scale;
`CagePos` (cage lift on a chain, visual + box collider) = h0 + (-9.5,0,10); `MinerSpawns` rows 9/10. Hall furniture is solid; `ModularCave.HallBlocked` removes nav nodes inside/around it
(`cavecheck` asserts the feeding spot and all weigh spots stay walkable and connected).
**Goblin rig** (`src/Goblin/`): `GMesh` (flat-shaded ellipsoid/tube/box kit with per-triangle colour variation), `GoblinParts` (geometry: the face is a sculpted height field of Gaussian features
- brow, sockets, hooked nose, cheekbones, lips with a smirk, chin, jowls - split into a static upper face and a jaw node; hair, ears, eyes, lids, brows, torso with navy quarter-zip and popped collar, legs, throne),
`GoblinRig` (animation + collision: breathing, blink, ear twitch; eyes follow the nearest miner fast, head slowly; jaw moves while `GoblinVoice.Said` fires; chewing on `feed_*` cues; `React("happy"|"angry")`;
lit from below by sickly green-yellow omni lights). Right hand (world -X) = the open feeding palm (glow disc + "FEED HIM" label, collision slab top at y = 1.45 m); the left arm is a stretchy two-bone IK arm
(bones scale up to 1.5x) that reaches the thrown miner, lifts, swings over the shaft and releases, driven by `Miner.ThrowU` (the same timeline the miner uses: `ThrowReachEnd/LiftEnd/SwingEnd`,
`Miner.ThrowHold/ThrowTop`), with a spotlight on the victim. `HallScale` = the gantry balance (beam tilts with the shown weight, the left pan's pile grows, big number above) + the shaft (wellhead, red glow, solid parapet).
**Tunnels and rooms** (`src/World/Cave/`): connecting tunnels are ONE grid cell wide; a *lattice warp* (`ModularCave.Warp`: cell-corner points pulled inward) turns them into 1.2-1.4 m wide passages, used by both the
mesh (`CaveVisuals.P`) and the collision (`CaveBuilder`: convex prisms fill the gap). Kinds (`TunnelDef.Kind`): 0 ordinary (ceiling 2.1-2.3 m), 1 crawl (middle two cells 1.1 m: crouch only; never the only way
to a room, max 2 per cave), 2 squeeze (0.85 m wide in the middle, `CaveInfo.SqueezeZones`, miners move at 55%). Tunnel mouths (door cells) are 3 m high. Room modules are small chambers with 3.0-5.0 m ceilings (monster rooms
>= 4.4 m; golems have `Headroom` 4 m and cannot enter lower cells, so they stay in their rooms). The miner capsule is 0.32 m radius, 1.0 m high when crouched.
`--cavecheck` additionally asserts tunnel widths/heights from the warp table and the cell grid, crawl count, room ceilings and sizes, that every room is reachable, and the Hall seams.
**Look**: no ambient light, near-black cold rock, exponential fog 0.042, lantern = warm omni (3.0 energy, 13 m range, shadows on). Light sources are only lanterns, glowing ore (emissive + faint light), the Hall's
green-yellow glow, the shaft's red glow and existing effect lights. `FilmGrain` (`src/UI/FilmGrain.cs`, canvas layer 2 under the HUD) draws grain + vignette; toggle in the settings (`UserSettings.FilmGrain`).
The project uses Forward Plus; the screenshot tool and tests run with `opengl3` (Compatibility), so everything above is Compatibility-safe (no Forward-only features).
**Staging views**: `--nohud`, `--camgoblin=entrance|face|q|profile|close|palm|hand|side|throw` (`--camfov=`), `--camtunnel=N [--camback=m --camcrouch]`, `--camdark` (no lantern), `--cavelist` also lists tunnels.
Soak: brave explorer bots deliberately walk over one known damaging trap per match (`_trapRuns`), so `trap_hit_miner` no longer depends on luck.

## Monsters v2.1 (src/Monsters)
**Roaming.** Monsters no longer have a home: they patrol the whole cave over `MonsterNav` (`MonsterNav.cs`: the cave nav graph plus crawl cells, with per-node ceiling / tunnel / squeeze / Hall / vein flags; A* and Dijkstra per `NavProfile`).
A profile says where a body fits: `Tall` (golem, Plug: ceiling >= 2.05, no squeezes, no crawls), `Person` (Lamplighter, Assessor: squeezes ok), `Swarm` (Wicks: everything incl. crawls). Nothing but Wicks passes a crawl. Nodes in the Hall zone (+0.5 m) and next to un-mined veins are blocked.
Senses (`MonsterObj.CanSee`, `MonsterSenses`): a lit lantern in line of sight within 28 m; a miner without a lit lantern within 2 m if standing still, 5.5 m walking, 9 m sprinting (speed measured on the server); noise events (`Game.NoiseMade`, radius = loudness) send them to investigate;
Wicks feel any lit lantern within 38 m without line of sight. Chase -> 5 s without contact -> search the last known spot 6 s -> patrol again. Monsters never enter `HallZone` (route snaps to the nearest reachable node, `Allowed` vetoes steps).
**Spawning and director** (`MonsterModule`): `MonsterTuning.RoamTarget(miners, round)` = 2 + miners/2 + round (cap 12) roamers at round start, kinds from `Guaranteed` (R1: Lamplighter + Wicks; R2+: Plug and Assessor; more later) and `FillWeights` (rest, mostly golems),
placed on nodes >= 25 m of path from the Hall and >= 14 m apart. The old `monster` markers place nothing now. `MonsterDirector` (per round, server): every 40 s, if no living miner outside the Hall had a monster within 15 m for 60 s, the nearest roaming Golem / Wicks / Lamplighter is sent
(`Dispatch`) toward a random miner's rough area (10 m scatter). `--nomonsters` turns spawning off (the director stays off unless `--mondirector`; `--dirquiet=`/`--dircheck=` shorten the timers for tests).
**Monsters.** Golem (hunched, tunnel-sized, walk 4.4 / charge 7.0 m/s, 35 damage + knockback, red eyes + dim red light). Plug (`Plug.cs`: solid on layer World so miners cannot pass, 1 m/s, creeps to the last noise or drifts between tunnels, 20 dmg/s crush, stun clamped to 0.8 s,
dynamite pushes it back 4.5 m along its trail). Wicks (`Wicks.cs`: one object, 10 bodies drawn client side; drains `ServerPlayer.Oil` 22 s/s and 4 dmg/s on contact; gives up 4 s after the lantern is out). Lamplighter (`Lamplighter.cs`: the lantern is built by `HeldVisuals.Lantern`, so
colour/energy/range/shadows equal a miner's; within 3 m or when hit it snuffs the light, grabs (`ServerRootPlayer` 2 s, 40 damage), walks away and relights). Assessor (`Assessor.cs`: from round 2; target = highest `AssessorHooks.TributeOf`, re-evaluated every 20 s, walks 5 m/s, pins 3 s,
`AssessorHooks.TakeTopStone` removes one stone, retreats 45 s; nothing in the UI shows its target). **When the sack/tribute rules change, only the two delegates in `AssessorHooks` need updating.**
**Sounds**: `MonsterSfx.cs` (registered from `SfxRecipes`), names in `SfxNames` (plug_creep/crush, wicks_chitter/drain, lamp_snuff/grab, assessor_step/stamp); played client side from each monster's `Animate`.
**Tests / staging**: `tools/monsters_test.sh` (legacy greybox scenarios plus generated-cave scenarios roam, dark, hall, lamp, assessor, plug, wicks, director, replicate; `ONLY="roam hall"` runs a subset). Screenshots: `--cammonster=golem|plug|wicks|lamplighter|assessor|lampcompare [--camdist=N] --camroom=0 [--camdark] --nomonsters`.
Bot spots: `--monpos=deep:<pathMetresFromHall>[:variant]`, `--monlantern=off`.
