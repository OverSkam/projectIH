using System;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Scrollable guide: every stein, utility, monster, trap and the match rules. Generated from the Def registries and tuning classes.</summary>
public partial class GuidePanel : PanelContainer
{
    public GuidePanel(Action onBack)
    {
        CustomMinimumSize = new Vector2(820, 600);
        var v = UiKit.VBox(8);
        AddChild(v);
        v.AddChild(UiKit.MakeTitle("GUIDE", 34));
        v.AddChild(UiKit.Rule());
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(780, 470), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(scroll);
        var body = UiKit.VBox(6);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.CustomMinimumSize = new Vector2(760, 0);
        scroll.AddChild(body);
        Build(body);
        var back = UiKit.MakeButton("Back", () => onBack?.Invoke(), 200, true);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        v.AddChild(back);
    }

    static void Head(Control b, string t) { b.AddChild(UiKit.Spacer(8)); b.AddChild(UiKit.MakeLabel(t, 22, UiTheme.Gold)); b.AddChild(UiKit.Rule(null, 1)); }

    static void Item(Control b, string name, string text, Color nameCol)
    {
        var h = UiKit.HBox(10);
        var n = UiKit.MakeLabel(name, 16, nameCol, HorizontalAlignment.Left, 3);
        n.CustomMinimumSize = new Vector2(170, 0); n.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        h.AddChild(n);
        var t = UiKit.MakeLabel(text, 15, UiTheme.Parchment, HorizontalAlignment.Left, 2);
        t.AutowrapMode = TextServer.AutowrapMode.WordSmart; t.SizeFlagsHorizontal = SizeFlags.ExpandFill; t.CustomMinimumSize = new Vector2(560, 0);
        h.AddChild(t);
        b.AddChild(h);
    }

    static void Para(Control b, string text)
    {
        var t = UiKit.MakeLabel(text, 15, UiTheme.Parchment, HorizontalAlignment.Left, 2);
        t.AutowrapMode = TextServer.AutowrapMode.WordSmart; t.CustomMinimumSize = new Vector2(740, 0);
        b.AddChild(t);
    }

    static string F(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    void Build(Control b)
    {
        var c = MatchConfig.Default;
        Head(b, "PLAYING ONLINE");
        Para(b, "The game plays online through Steam, like most Steam co-op games. Steam must be running on every PC (start Steam first, then the game). While the game has no store page of its own, Steam shows it as \"Spacewar\": that is normal.");
        Para(b, "To host: type your name, optionally a password, and press Host game. If you tick \"Open to anyone with the code\" people who are not on your Steam friends list can join too, otherwise only your Steam friends can. In the lobby (and in the Esc menu during a match) press Invite friends: a list of your Steam friends opens inside the game. Friends who are in this game come first, then friends who are online, then away; tick \"Show offline\" to see the rest, or type part of a name to filter. Press Invite next to a friend: it shows Invited for ten seconds. The Steam overlay (Shift+Tab) is NOT needed. Your friend gets a normal Steam invite. If their game is already running, a small banner \"<name> invited you - Join / Dismiss\" appears in the menu or lobby for 30 seconds; Join takes them in (and leaves their current room first). Friends can also just paste the STEAM ROOM CODE under the invite button into Room code (a friends-only room accepts friends who have the code), or right-click your name in Steam and choose Join Game. Note: accepting the invite in Steam starts the game by itself only when the game is in your Steam library; a game started by double-clicking the exe must already be running (the banner or the code then does the job).");
        Para(b, "To join: accept the Steam invite, or paste the code into Room code, type the password if the room has one (a small lock beside LOBBY shows that it does) and press Join game. If a room has a password and you came in through an invite, the code is filled in for you: type the password under Join and press Join game. A room holds up to 8 miners. No router setup, no port forwarding and no IP addresses: Steam connects you, through its own relay servers if needed.");
        Para(b, "If joining fails the red line under the menu says why: \"room not found\" (the host left or the code is wrong), \"room is full\", \"friends only\" (you are not on the host's friends list: ask for \"Open to anyone with the code\"), \"Wrong password\" or a version mismatch (both need the same version of the game). If the menu says \"Steam is not running\", start Steam and restart the game: only the advanced direct connection (below) works without Steam.");
        Para(b, "Direct connection (advanced): under the menu, \"Direct connection (advanced)\" gives the older way: Host game makes an INTERNET CODE and a SAME WI-FI CODE with an IP address inside; it needs your router to allow it (UPnP, or forward UDP port 7777) and Windows Firewall to allow the game. Friends paste that code (or an IP address / IP:port) into Room code. Use it only if Steam does not work for you.");
        Para(b, "Passwords are never sent over the internet as text and are not part of the code. After five wrong tries a person is locked out for a minute. The game does not encrypt the match itself, so do not reuse a password you use anywhere else.");
        Head(b, "VOICE CHAT");
        Para(b, "Voice chat uses Steam's voice, so it needs Steam and a microphone (Windows: Settings, Privacy, Microphone: let desktop apps use it). In Settings choose Open mic (default: it sends only while you talk), Push to talk (hold V) or Off; the meter shows what the microphone hears, and Voice volume sets how loud others are. Who hears whom: in the lobby everybody hears everybody. In a round living miners hear each other by distance, at full volume within 6 metres and fading to nothing at 28; a downed miner can still whisper, but only 10 metres. During the weighing in the Hall everybody still alive hears everybody still alive. Thrown or dead miners (ghosts) hear the living from anywhere and each other, but the living never hear ghosts. A small microphone shows bottom-left while you transmit, a speaker shows over the head of a talking miner and next to names in the Tab list, where you can mute a player (only for you; hold Tab a moment to free the cursor). Voices carry: monsters hear loud talking, whispering is safe.");
        Head(b, "ON SCREEN");
        Para(b, "The screen is kept nearly empty. Bottom-left: thin bars for health, lantern oil and (while it is not full) run stamina. Bottom-centre: your two steins and two tools, the selected one outlined; the name of an item shows for a few seconds when you pick or select it. Bottom-right: your sack, which bulges the heavier it gets, with your tribute number beside it (only you see it). Top-centre: a pocket-watch dial shows how much of the round is left, with no numbers; in the LAST MINUTE a rope fuse burns across the top and the Hall candle beside the Goblin burns down with the round. When the fuse reaches the end the lift leaves: be in the Hall, beside your cart.");
        Para(b, "Keys: W A S D move, Shift run (tires you), Ctrl crouch, Space jump, E take / open / use, left mouse mine or strike, right mouse use the tool in hand, 1 and 2 choose a tool, L lantern on / off, F choose stein slot, Q stein power, G drop the tool (hold G: stuff a stein into the sack), hold Tab for the miner list and key help, Esc for this menu.");
        Head(b, "HOW A MATCH WORKS");
        Para(b, $"With N miners there are N-1 rounds, each in a new, deeper cave. Round 1 lasts {c.RoundSeconds(1) / 60:0.#} minutes, every later round one minute longer, up to {c.MaxRoundSeconds / 60:0.#}. Mine stones (E takes one) into your one bottomless sack: there is no limit, but nothing ever comes out again. Every stone has a tribute (what it is worth) and a burden (how much it slows you): junk is bulky and worthless, rare stones are light and worth a lot. Speed falls with your total burden (about 55% at burden 30, never below 35%), sprinting tires you faster and your footsteps carry farther, so monsters hear a heavy miner from afar. To use a power stein as tribute, hold G for a second with its slot selected (F picks the slot): it is stuffed into your sack, loses its power for good and counts Common 6 / Rare 12 / Legendary 20, burden 1. The Infinity Stein cannot be stuffed.");
        Para(b, "There is no feeding during the round. When the timer ends the lift leaves: your tribute is whatever is in your sack IF you are standing inside the Hall. Anyone outside weighs nothing and loses their sack. Everyone inside is placed beside their tribute minecart and the sacks drop into the carts. Then the weighing: the Goblin lifts each sack to his palm in turn and the weight counts up above it, heaviest first, lightest last. The lightest miner is thrown down the shaft (ties: all tied miners, unless that would leave nobody). With two miners left the heavier pile wins. The last miner wins. Holding the Infinity Stein at the end of 3 rounds in a row also wins at once.");
        Para(b, "Nobody can see anyone else's tribute, sack or steins until the weighing. After the weighing every sack is emptied: tribute starts again from zero every round. Thrown miners stay in the match as ghosts. The Hall is safe: no fighting, and monsters, traps and explosions do nothing there.");
        Para(b, $"At 0 health you are downed for {c.DownedSeconds:0} seconds: you can only crawl, anyone may rob you (E, one stone per press, the most valuable first), and any further hit kills you. A dead miner's sack lies there as a bag: another miner interacts (E) and pours ALL of it into their own sack, burden included. The dead haunt as ghosts and return next round with only a pickaxe and a lantern. Everyone starts the match with the Pickaxe, the Lantern and one random Common stein.");
        Para(b, $"Your lantern burns oil: about {c.LanternOilSeconds / 60:0.#} minutes when full, and it starts every round full. L switches it off to save oil (or to hide). Oil drums in the cave refill it ({c.OilDrumRefills} refills each). When the oil runs low the light flickers; at zero it goes out.");

        Head(b, "STONES  (tribute / burden)");
        Para(b, string.Join("   ", Ores.All.Values.Where(o => o.SpawnWeight > 0).OrderBy(o => o.TributeWeight).Select(o => $"{o.Name} {o.TributeWeight}/{o.Burden}")) + "   |   Gold statue 15/5 (from Midas kills)   |   Stuffed power steins: Common 6/1, Rare 12/1, Legendary 20/1");

        foreach (var r in new[] { Rarity.Common, Rarity.Rare, Rarity.Legendary })
        {
            Head(b, ItemInfo.RarityName(r).ToUpper() + " STEINS" + (r == Rarity.Common ? "  (you start with one; two stein slots, F picks which is swapped or stuffed)" : r == Rarity.Rare ? "  (shrines, chests, monster drops)" : "  (very rare)"));
            foreach (var d in Steins.All.Values.Where(d => d.Rarity == r)) Item(b, d.Name, d.Description, ItemInfo.RarityColor(r));
        }

        Head(b, "UTILITY ITEMS  (two slots, keys 1 / 2, tap G drops, right mouse uses)");
        foreach (var d in Utilities.All.Values) Item(b, d.Name, d.Description, UiTheme.Parchment);

        Head(b, "MONSTERS");
        Para(b, "Monsters roam the whole cave and hunt by light and noise: a lit lantern in sight draws them from far away, noise brings them to the spot, and a miner without light is noticed only close by (about 2 m when standing still). Nothing you carry hurts them; some things stun them. They never enter the Hall, and only the Wicks fit through crawl spaces: a crawl is a safe escape.");
        Item(b, "Stone Golem", "A 3 m giant that crouches to fit tunnels. Heavy footsteps shake the ground, dim red eyes. Fast and hits hard; a pickaxe hit on the glowing crystal on its back stuns it. Do not turn your back, run for a crawl.", UiTheme.Parchment);
        Item(b, "The Plug", "A bloated mass that fills a tunnel and creeps toward noise. It cannot be passed or stunned for long: go around. Dynamite shoves it back.", UiTheme.Parchment);
        Item(b, "Wicks", "A swarm of small pale, faintly glowing things drawn ONLY to a lantern that is lit right now (about 22 m, through the tunnels). They fly to where the light is, drink your oil and bite. The moment you switch the lantern off (L) they lose you: they circle the last lit spot for a few seconds and drift away. A flash of light shorter than a second does not call them.", UiTheme.Parchment);
        Item(b, "The Lamplighter", "A tall thin figure with a light that looks just like a miner's lantern. Do not walk up to a lone light in the dark: it snuffs the flame and grabs you.", UiTheme.Parchment);
        Item(b, "The Assessor", "The Goblin's bailiff, from round 2. It walks, never runs, and wants the best stone of the miner carrying the most. Keep moving or stay in the Hall.", UiTheme.Parchment);
        Item(b, "Dragon", $"Round 3 and later, asleep on a hoard in the deepest room. Cannot be stopped. Noise wakes it (mining, sprinting, dynamite). Its breath hits {F(MonsterTuning.DragonBreathRange)} m ahead for {F(MonsterTuning.DragonBreathDps)} damage per second after a {F(MonsterTuning.DragonWindup)} s wind-up: break line of sight. Monsters see a lit lantern from far away ({F(MonsterTuning.SightLantern)} m instead of {F(MonsterTuning.SightDark)} m).", UiTheme.Parchment);

        Head(b, "TRAPS AND THEIR TELLS");
        Item(b, "Spike plate", $"A loose floor plate that clicks ({F(TrapTuning.SpikeClickDelay)} s) before the spikes come up for {F(TrapTuning.SpikeDamage)} damage. Step off at the click.", UiTheme.Parchment);
        Item(b, "Rockfall", $"Dust and a rumble ({F(TrapTuning.RockRumble)} s) before the ceiling drops for {F(TrapTuning.RockCrushDamage)} damage. Loud noise can set it off. Run out of the circle.", UiTheme.Parchment);
        Item(b, "Gas vent", $"Hisses for {F(TrapTuning.GasWarn)} s, then vents for {F(TrapTuning.GasVent)} s: {F(TrapTuning.GasTickDamage)} damage every {F(TrapTuning.GasTick)} s. A gas mask makes you immune.", UiTheme.Parchment);
        Item(b, "Dart tripwire", $"A thin wire low across the passage. Crossing it fires {TrapTuning.DartCount} darts of {F(TrapTuning.DartDamage)} damage. Jump or crouch-step over the wire.", UiTheme.Parchment);
        Item(b, "Trapdoor", $"Floor that creaks ({F(TrapTuning.TrapdoorCreak)} s) when you stand on it, then opens a {F(TrapTuning.PitDepth)} m pit. Move on at the creak.", UiTheme.Parchment);
        Item(b, "Levers", "Pull with E. They drop a gate, collapse a ceiling or open a trapdoor some metres down the passage (a chain shows which). Using one on a rival counts as your kill (Blood Stein). Cuttable rope bridges are cut with the pickaxe.", UiTheme.Parchment);

        Head(b, "GEODES, BRIGHT CHESTS AND MIMICS");
        Para(b, $"A mined vein no longer drops stones: it breaks off 1 to 3 sealed geodes (small, medium, large by how rich the vein was). Crack one where it lies with the pickaxe: {LootTuning.HitsToCrack} hits, each louder, with more light leaking through the cracks. The last hit reveals what was inside: the stones fly out and land as pickups (taking them into your sack is still your choice, E). Most geodes hold junk. Depth, later rounds, bigger geodes and the Luck stein improve the odds; about 4% of large geodes hold a Common power stein, 1% a Rare one, deep ones sometimes a Legendary. About one geode in ten is a dud: a puff of gas (a gas mask protects) or a swarm of bats that make a lot of noise.");
        Para(b, $"The rarer the result, the brighter and louder the reveal: coal and iron thud and glow for a metre or two, silver and gold flash and chime (about 6 m), rubies and diamonds blaze in colour and ring (about 14 m), and a power stein sends up a pillar of light that fills the tunnel and is heard {F(LootTuning.RevealNoise[3])} m away, by miners and monsters alike. Every hit and every reveal is noise.");
        Para(b, $"Bright chests glow gold in the dark: a beacon you can see far down a tunnel. Wooden (dim), iron, and gilded (brightest, deep). HOLD E for {F(LootTuning.HoldSeconds)} s to open one (damage or walking away cancels it; it is noisy): the lid swings up, light pours out and 3 to 6 stones jump out one by one, sometimes a power stein at the end. There are only a few per cave, roughly one per two miners, so miners race for them. Beware: about one chest in five is a MIMIC. It looks the same, except its glow slowly breathes, and if you stand still within {F(LootTuning.BreathRange)} m you can hear it breathe. When you open or hit a mimic it bites and holds you for {F(LootTuning.BiteSeconds)} s ({F(LootTuning.BiteDamage)} damage, you cannot move, your lantern goes out) and shrieks so loudly that every monster comes. Another miner hitting the mimic frees you early. Then it spits out a couple of stones as bait, sprouts legs and scuttles away to settle somewhere else as a chest again. It cannot be killed; dynamite and bear traps stun it for {F(LootTuning.MimicStun)} s.");

        Head(b, "SHRINES AND CHESTS");
        Para(b, $"Plate shrine: one miner stands on the floor plate while another takes the stein. Timed shrine: press the switch, then reach the pedestal within {F(ShrineObj.TimedSeconds)} s through a door that opens and closes. Blood altar: pay {F(ShrineObj.BloodCost)} health. Some chests hide behind thin cracked walls: only the Ethereal stein gets through.");
    }
}
