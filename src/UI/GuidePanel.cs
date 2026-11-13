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
        Head(b, "HOW A MATCH WORKS");
        Para(b, $"With N miners there are N-1 rounds, each in a new, deeper cave. Round 1 lasts {c.RoundSeconds(1) / 60:0.#} minutes, every later round one minute longer, up to {c.MaxRoundSeconds / 60:0.#}. Mine stones (E takes one) into your one bottomless sack: there is no limit, but nothing ever comes out again. Every stone has a tribute (what it is worth) and a burden (how much it slows you): junk is bulky and worthless, rare stones are light and worth a lot. Speed falls with your total burden (about 55% at burden 30, never below 35%), sprinting tires you faster and your footsteps carry farther, so monsters hear a heavy miner from afar. To use a power stein as tribute, hold G for a second with its slot selected (F picks the slot): it is stuffed into your sack, loses its power for good and counts Common 6 / Rare 12 / Legendary 20, burden 1. The Infinity Stein cannot be stuffed.");
        Para(b, "There is no feeding during the round. When the timer ends the lift leaves: your tribute is whatever is in your sack IF you are standing inside the Hall. Anyone outside weighs nothing and loses their sack. Everyone inside is placed on their glowing tribute ring and the sacks drop in front of them. Then the weighing: the Goblin lifts each sack to his palm in turn and the weight counts up above it, heaviest first, lightest last. The lightest miner is thrown down the shaft (ties: all tied miners, unless that would leave nobody). With two miners left the heavier pile wins. The last miner wins. Holding the Infinity Stein at the end of 3 rounds in a row also wins at once.");
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
        Item(b, "Stone Golem", "Heavy footsteps and dim red eyes. Fast and hits hard; a pickaxe hit on the glowing crystal on its back stuns it. Do not turn your back, run for a crawl.", UiTheme.Parchment);
        Item(b, "The Plug", "A bloated mass that fills a tunnel and creeps toward noise. It cannot be passed or stunned for long: go around. Dynamite shoves it back.", UiTheme.Parchment);
        Item(b, "Wicks", "A swarm of small pale things drawn to a lit lantern from far away. They drink your oil and bite. Switch the lantern off (L) and they lose interest.", UiTheme.Parchment);
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

        Head(b, "SHRINES AND CHESTS");
        Para(b, $"Plate shrine: one miner stands on the floor plate while another takes the stein. Timed shrine: press the switch, then reach the pedestal within {F(ShrineObj.TimedSeconds)} s through a door that opens and closes. Blood altar: pay {F(ShrineObj.BloodCost)} health. Some chests hide behind thin cracked walls: only the Ethereal stein gets through.");
    }
}
