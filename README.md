# App Stein

App Stein is a first-person multiplayer mining game for 2 to 8 players. You and your friends climb down into a dark mineshaft, dig out stones and feed them to a giant Goblin, while monsters, traps and each other get in the way. After every round the Goblin weighs everybody's tribute and throws the lightest miner down the shaft. Only one miner wins: the last one left, or whoever holds the cursed Infinity Stein long enough.

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
| Use / pick up / open / loot a downed miner | E |
| Feed the Goblin: tap E for all stones in your sack, hold E (about 1 second) for the selected power stein | E at the Goblin |
| Lantern on / off (saves oil) | L |
| Mine (pickaxe) / strike | Left mouse button |
| Use the held item (place dynamite, bear trap or rope; look at the map) | Right mouse button |
| Select utility slot 1 / 2 | 1 / 2 |
| Use the active power of your selected stein (e.g. Ethereal) | Q |
| Choose which stein slot is highlighted (the one swapped when you pick up a new stein) | F |
| Drop the held utility item | G |
| Drop a stone from your sack | T |
| Show the miners list | Tab (hold) |
| Pause menu / settings | Esc |
| As a ghost: fly | W A S D, Space (up), Ctrl (down) |
| As a ghost: snuff the nearest lantern | Right mouse button |
| As a ghost: knock a pebble (noise) | E |

Mouse sensitivity and field of view are in **Settings** (main menu or pause menu).

## How a match works

- With N miners a match has **N-1 rounds**. Every round is a new, deeper cave. Round 1 lasts **6 minutes**, every later round one minute more (up to 10).
- You start with a **pickaxe**, a **lantern** and one random **power stein**. Mine stones from veins with the pickaxe or pick up loose ones, carry up to **4 in your sack**, and bring them to the Goblin in the **Hall**. Interact (E) to feed him every stone in your sack. Each stone adds its **weight** to *your tribute for this round*: Coal 1, Iron 2, Silver 4, Gold 7, Ruby 10, Diamond 14. A power stein can be fed too (hold E on the Goblin with that stein selected, F picks the slot): Common 6, Rare 12, Legendary 20. Fed items are gone.
- When the timer ends the **lift leaves**. A miner who is not inside the Hall weighs **zero** and loses everything they carry. Then the **weighing**: every miner stands in the Hall facing the Scale and the piles are revealed one at a time, heaviest first, the lightest last. The **lightest miner is thrown down the shaft** and is out of the match (if several are tied for lightest, all of them are thrown, unless that would leave nobody; then one of them, picked at random). With two miners left, the heavier pile wins the match. Tribute starts from zero again every round.
- **Nobody can see anyone else's tribute weight, sack or steins.** Weights become public only during the weighing.
- Your **lantern burns oil**: about 4 minutes when full, and it starts every round full. Press **L** to switch it off and save oil (or to hide). When the oil runs low the light flickers; at zero it goes out. **Oil drums** in the cave refill it (a few refills each). The cave is nearly black without light.
- At 0 health you are **downed** for 30 seconds: you can only crawl very slowly, anyone may rob you (E, one item at a time), and any further hit kills you. If you die, everything you carry drops in a **bag** that any living miner can loot; you become a **ghost** until the next round and return with only a pickaxe and a lantern. Dying in the cave is not elimination: tribute you already fed this round still counts. Only the Scale eliminates.
- Thrown miners stay in the match as ghosts. The Hall is safe: no fighting, and monsters, traps and explosions do nothing there.
- **Monsters cannot be killed.** The Stone Golem is stunned for a few seconds by a hit on the glowing crystal on its back; dynamite and bear traps stun monsters briefly. The Dragon (round 3 and later) sleeps on a hoard in the deepest room and wakes on noise. Traps: spikes, rockfalls, gas vents, dart tripwires, trapdoors, levers and cuttable rope bridges.
- **Power steins** are found in shrine trials, chests and geodes. The **Blood Stein** is powerful but you must kill a miner each round or you lose it and half of that round's tribute. The **Midas Stein** turns a miner you kill into a gold statue stone (weight 15). The **Infinity Stein** does nothing, but whoever carries it at the end of 3 rounds in a row wins on the spot.
