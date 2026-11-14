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
- Veins drop geodes (built, see "Step 2 as built" at the end).

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

## v2.1 changes (owner feedback after the first v2 build) — these OVERRIDE anything above that conflicts
- **The sack.** The 4 sack slots are gone. Every miner has one bottomless sack. Anything put in stays in: a
  stone cannot be dropped, thrown away or taken out by its owner. Each stone has a **burden** (how much it slows
  you) separate from its **tribute weight** (what it is worth). Junk is bulky: Coal burden 3 / tribute 1,
  Iron 3/2, Silver 2/4, Gold 2/7, Ruby 1/10, Diamond 1/14. Speed falls with total burden (no hard cap; at
  burden 30 about 55% speed, never below 35%), sprint drains faster, noise grows. The choice is: take the junk
  in front of you and get slow, or gamble on going deeper for something better.
- A power stein can be stuffed into the sack (hold E in inventory key flow): it loses its power for good and
  counts as tribute (burden 1).
- **No feeding during the round, no giant Scale.** The Goblin's palm is not a deposit point any more. The Hall
  has one **tribute minecart** per miner (v2.2; it was a glowing ring). When the timer ends,
  miners inside the Hall are placed on their ring and their sack drops in front of them. The Goblin's hand
  lifts each sack in turn, weighs it in his palm (the number appears above it), lightest last, then throws the
  lightest miner down the shaft. The big balance Scale model is removed.
- A miner outside the Hall at timer end still weighs zero. A miner who dies drops the sack as a bag; anyone can
  pour it into their own sack (and take its burden). Downed miners can be robbed stone by stone.
- **Monsters must be met.** Monsters roam the whole cave, hunt by light and noise, and there are more of them
  the more miners there are and the later the round. A match must never pass without monster encounters.
- **Caves scale with players**: more and longer branches per miner (2 miners small; 8 miners roughly 3x).
- **Look:** no film grain. Instead drifting dust motes and layered fog lit by the lantern. With the lantern out
  it is not pitch black: like a blindness effect — you see dim shapes about 2.5 m around you and black beyond.
- The game must not feel empty: step 2 (geodes, bright chests, mimics) and the first new monsters come now.

## Step 2 as built (geodes, bright chests, mimics; details in docs/ARCHITECTURE.md "Step 2")
- Veins break off 1-3 sealed geodes; 3 pickaxe hits crack one in place; the reveal scales with rarity (tiers: dull, bright, gem, power stein; stein = pillar of light, 45 m noise). 10% of geodes are duds (gas or bats). Odds: mostly junk; depth, round, geode size and Luck improve them; large geodes only: 4% Common stein, 1% Rare, 0.3% Legendary (deeper).
- Bright chests (wooden / iron / gilded) glow as beacons, open with a 2.5 s hold of E, give 3-6 stones and sometimes a stein, about one per two miners plus a depth bonus. About 20% are mimics (never the chest nearest the Hall): bite and hold 4 s for 30 damage, huge shriek, bait, relocate. Unkillable; stunned by dynamite / bear trap; a second miner's hit frees the victim.

## v2.2 changes (owner feedback after the second playtest) — these OVERRIDE anything above that conflicts
- **Minimal HUD.** Only what is needed right now, small, dark and translucent, fading when unchanged: thin health / oil / run bars (bottom-left), the two steins and two tools as small icons (bottom-centre; name + one line for 3 s on select / pickup / round start), one sack icon that bulges with burden with your tribute number beside it and a "+10 RUBY" toast (bottom-right). No permanent help text, no round box: miners remaining only on the hold-Tab screen and for 4 s at round start. Key help lives in the Guide and the hold-Tab overlay.
- **The timer has no numbers.** A pocket-watch dial with one hand and a shrinking arc shows the fraction of the round left. In the last minute a rope fuse burns down across the top of the screen (sparks, a hiss that rises in the last 10 s, flickering screen edges). In the Hall a big candle beside the Goblin burns down with the round.
- **Tribute minecarts replace the floor rings.** Each miner has an ore cart (1.6 x 1.0 x 1.0 m, on a rail, painted in their colour with a name plate) on an arc in the Hall; they stand empty during the round. At timer end each miner in the Hall is placed beside their cart, the sack drops INTO it and the Goblin lifts it out onto his open palm to weigh it. Standing next to your cart: "Your cart - be here when the fuse burns out".
- **Wicks, one readable rule.** They are drawn only to a lantern that is lit right now (>= 1 s, within 22 m, at most 30 m of tunnel) and fly to where the light is. When it goes out they go to the last lit spot, circle it for 2.5 s and wander off; they never follow a dark miner. They glow faintly, chitter louder and higher as they come, make your flame gutter and your oil bar flash while they drink.
- **The golem is imposing**: 2.9 m standing and broad in rooms, crouching and hunching to fit tunnels; heavy steps shake the camera within 8 m. Stats unchanged.
- **Variety.** A round with 4 or more roamers always has at least 3 kinds (round 1: golem, Lamplighter, Wicks, + Plug from 5 roamers); the director rotates the kind it sends.

## v2.3 — other miners (first batch of direct player-to-player interaction) — these OVERRIDE anything above that conflicts
The owner's verdict on the second playtest: "insanely boring, there is no place for player interactions". Before this step the only contact was LMB with the pickaxe (damage, 4 m/s knockback, kills a downed miner), E robbing a downed miner (one stone per press, most valuable first, no way to revive or move them) and a sack model that never changed. Now:
- **Shove (R).** A two-handed push, first-person thrust and a third-person lunge. Range 1.6 m in a 110 degree cone, 1.2 s cooldown, 8% of the run bar, no damage. The victim is thrown about 3.5 m (11 m/s, braking at 14 m/s^2; 14 m/s, about 5.4 m, when the shover sprinted), staggers 0.4 s (cannot act), gets a camera jolt; shover and victim grunt. The server picks the victim and sends the impulse to the victim's own client, which applies it at once (movement is owner-simulated, so there is no rubber band); the shover plays the animation before the server answers. Pushing into pits, traps, ledges, monsters and out of a cart spot all just works because it is ordinary movement. A scuffle is noise (7 m).
- **Spill.** A shove from inside the 120 degree arc behind the victim's facing knocks the TOP (most recently picked) stone out of the sack: it hops out as a normal pickup 1.4-2.2 m behind them with a loud clatter (noise 10 m) and anyone may take it. The victim gets the red "-10 RUBY" toast and the sack icon twitches. Front and side shoves never spill. Together with pickpocketing and looting the downed this is the only way stones leave a sack; the owner still cannot empty it.
- **Shoving and the fuse.** Shoving is refused outside the round phase (so never during the weighing; the refusal is logged). It is allowed in the Hall while the round runs. The rule "be beside your cart when the fuse burns out" is implemented as "inside the Hall box at the fuse", judged on the server's last reported position of each miner at the instant the timer reaches 0 (the server positions miners beside their carts afterwards). A shove in the last 5 s is logged with who was shoved, whether they were inside, and the weight that counted (`shove_at_fuse`).
- **Pickpocket (Ctrl + hold E).** Crouched, within 1.3 m, inside the back arc of a standing miner and looking at them: prompt "Hold E - pick the sack" and the hold bar; 1.6 s later ONE random stone moves from their sack into yours (tribute and burden included; it may be coal). A soft cloth rustle at the victim's position (audible 4.5 m) starts with the hold. The victim gets no prompt and no text: 1.5 s after success the red "-10 RUBY" toast and the sack twitch (their own-state update is held back so nothing arrives early). If the thief is no longer behind them (more than about 107 degrees from their facing), too far (1.85 m), crouches up, is hit or is shoved the attempt fails; a failure after more than half the hold clatters loudly (noise 22 m, monsters hear) and locks pickpocketing for 6 s. Downed miners cannot be pickpocketed, empty sacks are refused with a quiet notice.
- **The sack is information.** Every miner wears a burlap sack on their back that grows in four steps by BURDEN (0: under 3, 1: under 12, 2: under 24, 3: under 36, 4: from 36, i.e. half speed): from a small flat pack to a bag wider than the miner hanging to the thighs, stones pushing against the cloth from step 2 on. Heavy miners also trudge: lower hips, forward lean, slower and shorter steps. The coarse step 0..4 is replicated to everyone; the exact burden and tribute stay private.
- **Downed miners.** A standing miner looking at a downed one within 2.4 m sees three lines: **Hold E** help up (3 s, the helper must stay within about 3.6 m and not be hit; the miner gets up with 35 health), **C** rob (one stone per press, most valuable first, then steins and tools), **hold RMB** drag (0.5x speed, a 1.25 m tether, a drop on release, shove or distance over 4.5 m). RMB is also "use the held tool"; looking at a downed miner it drags instead (nothing else uses RMB there). The downed miner sees "<Name> is helping you up", "<Name> is robbing you", "<Name> is dragging you" in the downed overlay. Both vulnerable: any hit on the downed miner still finishes them.
- **No ping.** The owner dropped the middle-mouse ping from this batch.
- **Hints and keys.** Three one-time hints: standing behind a miner (Crouch + hold E), the first spill, the first downed miner in reach. The new keys are in the hold-Tab list and in a Guide section "Other miners".
