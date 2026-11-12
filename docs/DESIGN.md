# App Stein — Game Design (v2)

First-person multiplayer (2–8 players) horror-comedy. Miners descend into a black, cramped mineshaft, crack
geodes, and carry steins (stones) back to a giant Goblin who eats them. Each round the Goblin weighs every
miner's tribute and throws the lightest miner down the shaft. Thrown miners come back as monsters.
The last miner wins. There is no money and no shared quota.

Mood: low-poly but very dark and grim. The player is prey: weak, short of light, in tunnels one body wide.
Tone: dark comedy carried by the Goblin.

**v1 → v2 summary:** money → tribute weight; quota/poorest-curse/survivor-bonus removed; fixed 5 rounds →
elimination until one miner is left; killable monsters → unkillable; free lantern → lantern oil; wide rooms →
narrow, low tunnels; small Goblin behind a counter → giant Goblin in the Hall of the Scale.

## Match structure
- With N miners there are N−1 rounds. Each round is a new, deeper cave.
- **Round flow:** cage lift down from the Hall → mine → return to the Hall and feed the Goblin → timer ends →
  the weighing → one miner is thrown → next round.
- **Timer:** set by the Goblin. Round 1: 6 min, +1 min per round, max 10 min.
- **Timer end:** the lift leaves. A miner who is not inside the Hall weighs **zero** this round and loses
  everything they carry.
- **The weighing:** each miner's pile is put on the giant Scale one at a time in front of everyone, lightest
  last. The lightest miner is picked up by the Goblin and thrown down the shaft (eliminated).
  - Ties for lightest: all tied miners are thrown, unless that would leave nobody — then one of them, at random.
  - Final two miners: the heavier pile wins the match.
- **Infinity Stein** victory still exists (see steins).

## Hidden information (core rule)
- Nobody sees another miner's tribute weight, sack or steins. You see only your own weight this round.
- Weights become public only during the weighing.
- The Goblin never names who is ahead or behind. (The only hint is the monster "the Owed".)

## Tribute
- Tribute is **weight**, counted per round and reset to zero after each weighing.
- Feed the Goblin by placing stones in his open palm (interact). Sack stones are fed together; a power stein
  can also be fed (deliberate separate action) and weighs a lot.
- Weights: Coal 1, Iron 2, Silver 4, Gold 7, Ruby 10, Diamond 14. Power steins: Common 6, Rare 12,
  Legendary 20. Special objects: gold statue 15, mother geode 30.

## Death in the cave is not elimination
- A miner at 0 health is **downed** for 30 s: cannot act, can crawl very slowly, can be looted. (Step 4 adds
  dragging and reviving.) After 30 s, or if hit again, they die.
- A dead miner drops everything in a bag, haunts as a ghost (can blow out a lantern or knock a pebble) and
  returns next round with only a pickaxe and a lantern. Tribute already fed this round still counts.
- Only the Scale eliminates.

## The Turned (eliminated players)
- A thrown miner returns from the next round on as a monster and stays one for the rest of the match.
- The Turned **cannot tell miners apart**: they see lantern light and sound pulses (footsteps, mining, geode
  cracks), not bodies, names or colours. A miner with the lantern out, standing still, is invisible to them.
- They cannot win. They play for spite.
- (Until the Turned are implemented, eliminated players remain ghosts for the rest of the match.)

## What a miner carries
| Slots | Holds | Notes |
|---|---|---|
| Sack ×4 | Stones (tribute) | Fed to the Goblin |
| Stein ×2 | Power steins | Power while held; heavy tribute if fed |
| Utility ×2 | Tools | Start with Pickaxe + Lantern |

Health 100. Fists and pickaxe hurt **miners**. Nothing a miner carries can kill a monster. No fighting
inside the Hall.

## Light
- The lantern burns **oil**: a full lantern lasts about 4 minutes of light. It starts each round full.
- Oil drums are rare objects in the cave (refill by interacting; each drum has a few refills).
- The lantern can be switched off to save oil or to hide. Without it the cave is black.
- Darkness itself becomes dangerous once the dark-hunting monsters exist.

## Loot (step 2)
- Veins drop sealed **geodes**. Cracking one (pickaxe, takes a moment) plays an escalating reveal: glow through
  cracks → colour of rarity → burst. Most are common stones; rarely a power stein.
- **The rarer the result, the brighter and louder the reveal** — a legendary lights the tunnel and is heard
  far away by miners and monsters.
- **Bright chests** hold several stones or a stein. Some are **mimics** that bite and hold on.
- Until geodes exist, veins drop stones directly as in v1.

## Power steins
Every miner starts the match with one random Common stein. Found in geodes, chests, shrines. Never bought.
Every strong stein has a cost another player can exploit.

- **Common:** Regen, Swift, Stoneskin, Strongarm, Owl, Feather, Packmule, Hush.
- **Rare:** Luck (better geode/vein rolls), Ethereal (pass marked thin walls), Glutton (your sack stones weigh
  double on the Scale, but each stone you carry slows you), Parasite (slowly drains tribute weight from the
  nearest miner to you), Lantern-heart (you glow and need no oil, but can never go dark), Echo (leave a
  standing decoy of yourself), Whisper (you hear the heartbeat of legendary steins nearby — and monsters hear
  yours).
- **Legendary:**
  - **Midas** — a miner you kill turns into a gold statue (weight 15) instead of dropping a bag of loot.
  - **Liar** — once, during the weighing, swap your pile with another miner's.
  - **Blood** — large boost to health, speed and damage; you must kill a miner each round or lose the stein
    and half your tribute at the weighing.
  - **Infinity** — one item that looks like six fused stones, no power. Held by the same miner at the end of
    3 rounds in a row → instant win. The Goblin announces the count without naming the holder.

## Utility items
Pickaxe, Lantern, Rope, Gas mask, Map, Dynamite, Bear trap (as v1). Dynamite and bear traps stun monsters
briefly; they never kill them.

## Monsters — all unkillable
- **The Assessor** — the Goblin's bailiff. Drawn to the miner carrying the most weight. Pins them, takes
  their heaviest item back to the Goblin, credited to nobody.
- **The Plug** — fills a narrow tunnel completely and creeps forward. Cannot be passed.
- **The Owed** — visible only to the miner who is currently lightest. Hunts only them.
- **Wicks** — small pale swarm drawn to flame; drink lantern oil. Leave when the flame is out.
- **The Lamplighter** — a tall figure carrying a light identical to a miner's lantern.
- **Hatchlings** — some geodes are eggs; the hatchling rides your back and makes your tribute weigh less.
  Passed on by touching another miner.
- **The Hands** — the giant Goblin's hand reaches down certain shafts and gropes for a miner.
- **Waking veins** — some rich veins are sleeping stone golems.
- **The Dragon** — sleeps on a hoard in the deepest room of late rounds; noise wakes it.
- Removed from v1: Ember Elemental, Gas Wisp. Until the new monsters exist, the v1 Golem (made unkillable:
  a hit on its back crystal only stuns it) and the Dragon are the only monsters.

## Traps and hazards
As v1: spike plates, rockfalls, gas vents, dart tripwires, trapdoor pits, levers (gate, collapse, trapdoor).

## Cooperation and betrayal (step 4)
- **Winch** — one miner lowers another on a rope into a rich pit and decides whether to pull them up.
- **Gate** — one holds it open while the other crawls under, there and back.
- **Mother geode** — needs two carriers; counts for whoever lays it in the Goblin's palm.
- **Pact stone** — two miners bind. At the weighing each secretly picks Share or Steal. Both share: pooled and
  split. One steals: takes all. Both steal: both lose half.
- **Gifting** — during the weighing any miner may throw stones onto another's pile.
- **Single file** — tunnels are one miner wide; a crouching miner blocks a crawl.
- **Downed** — drag a downed miner to safety, revive, or loot and leave.
- **Pickpocket and plant** — from behind, take a stone from a sack or slip an egg into it.

## The cave
- Mostly tunnels **one miner wide** with the ceiling just above head height, crawls (crouch only) and sideways
  squeezes. Open rooms are rare and feel exposed.
- Rooms: Hall of the Scale (hub), cage lift, the Vault (big chest, several miners to open, one prize), the
  Black Gallery (lanterns do not work; glowing ore only), flooded crawl, the Altar (a legendary that unlocks
  when a miner dies on it), plus v1 modules narrowed.

## The Goblin and the Hall of the Scale
- A **giant**: seated, about 9 m tall, filling the end of the Hall; the face alone is about 3 m high and lit
  from below. Green goblin caricature of a rich financier: swept-back grey hair, heavy eyebrows, big nose,
  long pointed ears, smug half-smile, navy quarter-zip with a popped collar. Eyes follow miners.
- One huge open palm rests at miner height: the place to feed him.
- The giant Scale stands in front of him. The shaft he throws miners into is beside it.
- He talks: greedy, bored, cruel-funny.

## Art direction
Low-poly, flat-shaded, **much darker**: near-black caves, desaturated cold rock, strong fog, no fill light.
Light comes from lanterns, glowing ore, geode reveals and the Hall's sickly glow. Film grain and vignette.

## Later
HUD redesign; real recorded sounds (owner will supply); character animation.

## Build order
1. **Core loop:** tribute weight, the Scale and weighing ceremony, elimination, downed state, lantern oil,
   unkillable monsters; then the giant Goblin and Hall, narrow caves, darker look.
2. Geodes, chests, mimics.
3. The Turned and the new monsters.
4. Cooperation pieces.
5. New steins and rooms.
