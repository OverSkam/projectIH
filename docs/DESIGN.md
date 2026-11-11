# App Stein — Game Design (v0.1)

First-person multiplayer (2–8 players) mining game. Miners descend into a dark mineshaft, collect valuable
steins (stones) and hand them to the boss Goblin for money. Everyone shares the cave and its dangers, but
only one miner wins. Mood: PEAK-style low-poly, but dark and underground. Tone: dark comedy.

## Match structure
- A match is **5 rounds**. Each round is a new, deeper cave assembled from hand-built room modules.
- Each round has a **timer set by the Goblin** (round 1: 6 min, growing to ~12 min by round 5).
- Every round starts and ends at the **Goblin's camp** (the hub room at the cave entrance).
- When the timer ends the Goblin collapses the mine: every miner not inside the camp dies.
- The round also ends immediately if every miner is dead.
- **Shared quota:** the group must deliver a total value each round. If missed, every miner loses 20% of
  their money. This is what makes cooperation worthwhile.
- **Poorest miner punishment:** after each round the Goblin publicly names the miner who delivered the
  least that round and curses them (next round: starts at 60% health). Only the poorest is ever named.
- **Survivor bonus:** if exactly one miner is alive at round end, they get a cash bonus.
- **Winner:** most money after round 5, or an Infinity Stein victory (see below).

## Hidden information (core rule)
- Nobody can see another miner's steins, sack contents, or money. The scoreboard shows only your own money.
- The Goblin reveals only: the group quota progress, the poorest miner, and Infinity Stein warnings.
- When a miner dies, everything they carry drops in a lootable bag. That is the only way to learn it.

## Death
- Dead miners become **ghosts** until the next round: free-fly spectators who can meddle a little
  (blow out a lantern, knock a pebble to make noise) on a cooldown. They respawn alive next round.
- Banked money is kept. Carried ore, power steins and utility items are dropped.

## What a miner carries
| Slots | Holds | Notes |
|---|---|---|
| Sack ×4 | Valuable ore | Delivered to the Goblin for money |
| Stein ×2 | Power steins | Passive/active powers. Never bought — only found |
| Utility ×2 | Tools | Start with Pickaxe + Lantern; others found in the cave and swapped |

Health 100. Fists always available (5 dmg + shove). Direct PvP is allowed everywhere except inside the camp.

## Valuable ore (money)
Mined from veins with a pickaxe (several hits) or found loose. Coal 10, Iron 25, Silver 50, Gold 100,
Ruby 175, Diamond 250. Deeper rooms and later rounds have richer veins.

## Power steins
Every miner starts the match with one random Common stein. More come from trial shrines, rare chests and
monster drops. Luck improves rarity.

- **Common:** Regen (slow heal), Swift (+speed), Stoneskin (−damage taken), Strongarm (faster mining,
  +melee), Owl (see in the dark), Feather (no fall damage, higher jump), Packmule (+2 sack slots),
  Hush (monsters hear you less).
- **Rare:** Luck (better drops), Midas (bonus damage scaling with your money), Ethereal (walk through
  specially marked thin walls, with a cooldown).
- **Legendary:**
  - **Blood** — large boost to health, speed and damage, but you must kill one miner each round; if you
    did not, at round end you lose the stein and half your money.
  - **Infinity** — a single item that looks like six stones fused together and takes one stein slot.
    It gives no power. If one miner is holding it at the end of 3 rounds in a row, they win instantly.
    The Goblin announces "the Infinity Stein has been carried for N rounds" without naming the holder.

## Utility items
Pickaxe (mine + 20 dmg melee), Lantern (light; without it the cave is nearly black), Rope (anchor at a
ledge to climb pits; others can cut it), Gas mask (immune to gas), Map (shows room layout), Dynamite
(opens blocked passages, hurts, triggers rockfalls), Bear trap (placeable).

## Monsters
- **Stone Golem** — slow, hits hard, armoured from the front. Glowing crystal on its back is the weak
  point: one miner distracts, another strikes. Drops gems.
- **Ember Elemental** — fast, burns; dies when lured into water.
- **Gas Wisp** — drifting poison cloud; harmless with a gas mask.
- **Dragon** — sleeps on a hoard in the deepest room of late rounds. Unkillable. Noise wakes it.

## Traps and hazards
- **Built-in:** spike pits, rockfall zones (rumble warning first), gas vents, tripwire darts.
- **Player-activated:** levers that drop gates, collapse ceilings or open trapdoors; cuttable rope
  bridges; placed bear traps.

## Trials (stein shrines)
Two-player pressure plate (one must stay on the plate while the other takes the stein), timed gauntlet,
blood altar (pay health), guardian golem.

## The Goblin
Boss at the camp: takes ore, pays money, announces quota/time/poorest. A goblin caricature in a polo
shirt with swept grey hair.

## Art direction
Low-poly, flat-shaded, built from code and primitive meshes. Very dark; lantern light and glowing ore,
fog, warm camp light versus cold cave.

## Build order
1. **Foundation:** project, networking/lobby, first-person miner, health/melee/death/ghost, slots,
   interaction, round + match loop, goblin deposit, HUD, grey-box test cave, automated smoke test.
2. **Content (parallel):** cave generator + room modules + lighting; ore/steins/utilities/trials;
   monsters; traps/rockfalls; menus/goblin/round-end screens.
3. Balance, sound, polish, Windows build.
