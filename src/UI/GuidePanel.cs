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
        Para(b, $"With N miners there are N-1 rounds, each in a new, deeper cave. Round 1 lasts {c.RoundSeconds(1) / 60:0.#} minutes, every later round one minute longer, up to {c.MaxRoundSeconds / 60:0.#}. Mine stones and carry up to 4 in your sack. Back in the Hall, interact with the Goblin (E) to feed him every stone in your sack: each stone adds its weight to YOUR tribute for this round. To feed a power stein, hold E for a second on the Goblin with that stein's slot selected (F): it weighs a lot, but it is gone.");
        Para(b, "When the timer ends the lift leaves. Anyone not inside the Hall weighs nothing and loses everything they carry. Then the weighing: every pile is revealed one at a time on the Scale, heaviest first, lightest last. The lightest miner is thrown down the shaft (ties: all tied miners, unless that would leave nobody). With two miners left the heavier pile wins. The last miner wins. Holding the Infinity Stein at the end of 3 rounds in a row also wins at once.");
        Para(b, "Nobody can see anyone else's tribute weight, sack or steins until the weighing. Tribute starts again from zero every round. Thrown miners stay in the match as ghosts. The Hall is safe: no fighting, and monsters, traps and explosions do nothing there.");
        Para(b, $"At 0 health you are downed for {c.DownedSeconds:0} seconds: you can only crawl, anyone may rob you (E, one item at a time), and any further hit kills you. Dead miners drop everything in a bag, haunt as ghosts and return next round with only a pickaxe and a lantern. Everyone starts the match with the Pickaxe, the Lantern and one random Common stein.");
        Para(b, $"Your lantern burns oil: about {c.LanternOilSeconds / 60:0.#} minutes when full, and it starts every round full. L switches it off to save oil (or to hide). Oil drums in the cave refill it ({c.OilDrumRefills} refills each). When the oil runs low the light flickers; at zero it goes out.");

        Head(b, "STONES  (weight on the Scale)");
        Para(b, string.Join("   ", Ores.All.Values.Where(o => o.SpawnWeight > 0).OrderBy(o => o.TributeWeight).Select(o => $"{o.Name} {o.TributeWeight}")) + "   |   Gold statue 15 (from Midas kills)   |   Power steins when fed: Common 6, Rare 12, Legendary 20");

        foreach (var r in new[] { Rarity.Common, Rarity.Rare, Rarity.Legendary })
        {
            Head(b, ItemInfo.RarityName(r).ToUpper() + " STEINS" + (r == Rarity.Common ? "  (you start with one; two stein slots, F picks which is swapped)" : r == Rarity.Rare ? "  (shrines, chests, monster drops)" : "  (very rare)"));
            foreach (var d in Steins.All.Values.Where(d => d.Rarity == r)) Item(b, d.Name, d.Description, ItemInfo.RarityColor(r));
        }

        Head(b, "UTILITY ITEMS  (two slots, keys 1 / 2, G drops, right mouse uses)");
        foreach (var d in Utilities.All.Values) Item(b, d.Name, d.Description, UiTheme.Parchment);

        Head(b, "MONSTERS");
        Para(b, "Monsters cannot be killed. Nothing you carry hurts them, but some things stun them for a few seconds.");
        Item(b, "Stone Golem", $"Slow, but its overhead slam ({F(MonsterTuning.GolemWindup)} s wind-up, {F(MonsterTuning.GolemSlamReach)} m reach) does {F(MonsterTuning.GolemSlamDamage)} damage. Hits from the front do nothing; a pickaxe hit on the glowing crystal on its back stuns it for {F(MonsterTuning.GolemStunSeconds)} seconds. One miner distracts, another circles behind. Dynamite and bear traps stun it briefly too.", UiTheme.Parchment);
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
