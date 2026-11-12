# App Stein

App Stein is a first-person multiplayer mining game for 2 to 8 players. You and your friends climb down into a dark mineshaft, dig out ore and hand it to the boss Goblin for money, while monsters, traps and each other get in the way. Everyone shares the same cave but only one miner wins: the richest after five rounds, or whoever holds the cursed Infinity Stein long enough.

## What you need (Windows)

1. **Godot 4.7.2, the ".NET" (Mono) build.** Download it from <https://godotengine.org/download/windows/> and choose the file marked **.NET**. The normal Godot build will not work, because this game is written in C#. Godot needs no installer: unzip it anywhere.
2. **The .NET 8 SDK** (the SDK, not just the "runtime"): <https://dotnet.microsoft.com/download/dotnet/8.0>. Install it with the default options, then restart Godot if it was open.
3. This project folder (the one that contains `project.godot`).

## Open the game and press Play

1. Start the Godot .NET program. In the Project Manager click **Import** (or **Open**), pick the `project.godot` file in this folder and confirm.
2. Wait a moment: the first time, Godot builds the C# code by itself.
3. Press the **Play** button in the top right corner (or **F5**). The game's main menu opens.

If Godot complains about C# or ".NET", check that you downloaded the .NET build of Godot and installed the .NET 8 SDK, then press the hammer (**Build**) button at the top and try again.

## Playing with friends

One player is the **host**; the others **join** the host. A match needs at least 2 players.

**Host:** type your miner name, click **Host game**. The lobby shows "Friends join with:" followed by your local IP address(es) and the port (UDP 7777). When everybody is in, click **Start match**.

**Join:** type your name, type the host's IP address in the "host address" box, click **Join game**, then wait in the lobby for the host to start.

**Same network (same Wi-Fi or LAN):** the joiners type the local IP address shown in the host's lobby (it usually looks like `192.168.x.x`). If it does not connect, allow Godot through the Windows firewall (a popup appears the first time you host).

**Over the internet, either:**
- the host forwards **UDP port 7777** in the router to the host PC (and allows it in the firewall), and the others join using the host's public IP address (search "what is my IP"), or
- everybody installs a virtual-LAN tool (for example Radmin VPN, ZeroTier or Hamachi), joins the same virtual network, and the others join using the host's address inside that virtual network.

**Trying multiplayer alone with two instances:**
1. In Godot open the **Debug** menu and choose **Customize Run Instances...**, tick **Enable Multiple Instances**, set the number to 2, and press **Play**. Two game windows open.
2. In the first window click **Host game**. In the second window type `127.0.0.1` as the host address and click **Join game**.
3. In the first window click **Start match**.

Without the editor you can also start two copies of the Godot program with `--path` pointing to this folder, for example from a Command Prompt: `Godot_v4.7.2-stable_mono_win64.exe --path "C:\path\to\app-stein"`, twice.

## Controls

| Action | Key / button |
|---|---|
| Move | W A S D |
| Look | Mouse |
| Jump | Space |
| Sprint | Left Shift |
| Crouch / crawl | Left Ctrl |
| Use / pick up / open / deliver to the Goblin | E |
| Mine (pickaxe) / strike | Left mouse button |
| Use the held item (place dynamite, bear trap or rope; look at the map) | Right mouse button |
| Select utility slot 1 / 2 | 1 / 2 |
| Use the active power of your selected stein (e.g. Ethereal) | Q |
| Choose which stein slot is highlighted (the one swapped when you pick up a new stein) | F |
| Drop the held utility item | G |
| Drop a piece of ore from your sack | T |
| Show the miners list | Tab (hold) |
| Pause menu / settings | Esc |
| As a ghost: fly | W A S D, Space (up), Ctrl (down) |
| As a ghost: rattle something (blow out a lantern or knock a pebble) | Right mouse button |

Mouse sensitivity and field of view are in **Settings** (main menu or pause menu).

## How a match works

- A match is **5 rounds**. Every round is a new, deeper cave and starts and ends at the **Goblin's camp**. The camp is safe: no fighting between players, no monsters and no traps work there.
- Each round has a **timer** (6 minutes in round 1, growing to 12 minutes). When it hits zero the Goblin **collapses the mine** and every miner who is not back in the camp dies. Be home in time.
- You start with a **pickaxe**, a **lantern** and one random **power stein**. Mine ore from veins with the pickaxe or pick up loose ore, keep it in your **sack** (4 slots) and **deliver it to the Goblin** with E to turn it into money. The cave is nearly black without the lantern.
- The whole group must deliver a **quota** each round. If the quota is missed, everybody loses 20% of their money. The Goblin also names the **poorest miner** of the round and curses them: they start the next round with less health.
- Other miners can see where you are but **nobody can see your money, sack or steins**. When someone dies, everything they carry drops in a **bag** that any living miner can loot. Dead miners become **ghosts** until the next round and can only meddle a little.
- Dangers: **Stone Golems** (hit hard, weak from behind), **Ember Elementals** (fast and burning), **Gas Wisps** (poison; a gas mask helps), the **Dragon** (round 3 and later, asleep in the deepest room; noise wakes it), and traps such as spikes, rockfalls, gas vents, dart tripwires, trapdoors, levers and cuttable rope bridges.
- **Power steins** are found in shrine trials, rare chests and monster drops. The **Blood Stein** is powerful but you must kill a miner each round or you lose it and half your money. The **Infinity Stein** does nothing, but whoever carries it at the end of 3 rounds in a row wins on the spot.
- After round 5 (or an Infinity win) the **richest miner wins**.
