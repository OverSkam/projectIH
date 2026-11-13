# App Stein

App Stein is a first-person multiplayer mining game for 2 to 8 players. You and your friends climb down into a dark mineshaft, dig out stones into your one bottomless sack and carry them back to a giant Goblin, while monsters, traps and each other get in the way. After every round the Goblin weighs everybody's sack and throws the lightest miner down the shaft. Only one miner wins: the last one left, or whoever holds the Infinity Stein long enough.

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
| Use / pick up / loot a downed miner. **Hold** E for 2.5 s to open a bright chest | E |
| Take the stone / pour the bag / rob the downed miner under the crosshair (one item per press) | E |
| Lantern on / off (saves oil) | L |
| Mine veins, crack geodes (3 hits) with the pickaxe / strike | Left mouse button |
| Use the held item (place dynamite, bear trap or rope; look at the map) | Right mouse button |
| Select utility slot 1 / 2 | 1 / 2 |
| Use the active power of your selected stein (e.g. Ethereal) | Q |
| Choose which stein slot is highlighted (the one swapped when you pick up a new stein) | F |
| Tap: drop the held utility item. Hold (1 second): stuff the selected power stein into your sack (it loses its power, counts Common 6 / Rare 12 / Legendary 20) | G |
| Show the miners list | Tab (hold) |
| Pause menu / settings | Esc |
| As a ghost: fly | W A S D, Space (up), Ctrl (down) |
| As a ghost: snuff the nearest lantern | Right mouse button |
| As a ghost: knock a pebble (noise) | E |

Mouse sensitivity and field of view are in **Settings** (main menu or pause menu).

## How a match works

- With N miners a match has **N-1 rounds**. Every round is a new, deeper cave. Round 1 lasts **6 minutes**, every later round one minute more (up to 10).
- You start with a **pickaxe**, a **lantern** and one random **power stein**. Mine stones from veins with the pickaxe or pick up loose ones (E, a deliberate choice) into your single **bottomless sack**: there is no limit, but nothing can ever be dropped or taken out again. Each stone has a **tribute** (what it is worth) and a **burden** (how much it slows you): Coal 1/3, Iron 2/3, Silver 4/2, Gold 7/2, Ruby 10/1, Diamond 14/1 (tribute/burden; gold statue 15/5). Your speed is `max(35%, 1 / (1 + burden/36))` (about 55% at burden 30), sprinting tires you faster and your footsteps carry farther. A power stein can be stuffed into the sack (hold G): it loses its power and counts Common 6 / Rare 12 / Legendary 20, burden 1 (the Infinity Stein cannot be stuffed). The **Packmule Stein** halves your burden.
- There is **no feeding during the round**. When the timer ends the **lift leaves**: your tribute is the content of your sack **if you are standing inside the Hall**; a miner outside weighs **zero** and the sack is lost. Everyone in the Hall is placed on their glowing **tribute ring**, the sacks drop in front of them, and the Goblin lifts each sack to his palm in turn while the weight counts up above it, heaviest first, lightest last. The **lightest miner is thrown down the shaft** and is out of the match (ties: all tied miners are thrown, unless that would leave nobody; then one at random). With two miners left, the heavier sack wins the match. After the weighing every sack is emptied. Hanging around in the Hall during the round earns you the Goblin's scorn.
- **Nobody can see anyone else's tribute, sack or steins.** Sack contents become public only during the weighing (the sack models only vary a little in size).
- Your **lantern burns oil**: about 4 minutes when full, and it starts every round full. Press **L** to switch it off and save oil (or to hide). When the oil runs low the light flickers; at zero it goes out. **Oil drums** in the cave refill it (a few refills each). With the lantern out you are almost blind: dim shapes within about 2.5 m, black beyond (nobody else sees that). Dust and haze drift in the lantern beam.
- At 0 health you are **downed** for 30 seconds: you can only crawl very slowly, and any further hit kills you. Anyone may rob you: each E press takes **one** stone (the most valuable first) into their sack. If you die, your sack lies there as a **bag**: another miner interacts (E) and pours **all** of it into their own sack (burden included). You become a **ghost** until the next round and return with only a pickaxe and a lantern. Dying in the cave is not elimination. Only the weighing eliminates.
- Thrown miners stay in the match as ghosts. The Hall is safe: no fighting, and monsters, traps and explosions do nothing there.
- **Monsters cannot be killed.** The Stone Golem is stunned for a few seconds by a hit on the glowing crystal on its back; dynamite and bear traps stun monsters briefly. The Dragon (round 3 and later) sleeps on a hoard in the deepest room and wakes on noise. Traps: spikes, rockfalls, gas vents, dart tripwires, trapdoors, levers and cuttable rope bridges.
- **Geodes, bright chests and mimics.** A mined vein breaks off 1 to 3 sealed **geodes**. Crack one with the pickaxe where it lies (3 hits, each louder, light leaking through the cracks): the stones fly out and land (E takes one), mostly junk, but depth, later rounds, big geodes and the Luck Stein improve the odds, and a few hold a power stein. The rarer the result, the brighter and louder the reveal (a power stein lights the whole tunnel and is heard 45 m away by miners and monsters). Some geodes are duds: gas (a gas mask protects) or bats that make a big noise. **Bright chests** glow gold in the dark: **hold E for 2.5 s** to open one (damage or walking away cancels it), 3 to 6 stones jump out, sometimes a stein. There are few, so miners race for them. About one in five is a **mimic**: it looks the same but its glow slowly breathes (and you hear it breathe if you stand still next to it). It bites and holds you for 4 s (30 damage, you cannot move, your lantern goes out), shrieks (everything hears it), then spits out bait and scuttles away to hide as a chest elsewhere. Another miner hitting it frees you; dynamite and bear traps stun it; nothing kills it.
- **Power steins** are found in shrine trials, chests and geodes. The **Blood Stein** is powerful but you must kill a miner each round or you lose it and half of that round's tribute. The **Midas Stein** turns a miner you kill into a gold statue stone (tribute 15, burden 5) that goes into the sack of whoever picks it up. The **Infinity Stein** does nothing, but whoever carries it at the end of 3 rounds in a row wins on the spot.
