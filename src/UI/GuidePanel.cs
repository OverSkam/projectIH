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
        Para(b, $"{c.Rounds} rounds, each a new deeper cave. Round 1 lasts {c.RoundSeconds(1) / 60:0.#} minutes, round {c.Rounds} lasts {c.RoundSeconds(c.Rounds) / 60:0.#}. When the timer ends the Goblin collapses the mine: everyone outside the camp dies. The camp is safe: no PvP, and monsters, traps and explosions do nothing there.");
        Para(b, $"Mine ore, carry up to 4 pieces in your sack and deliver it to the Goblin (E) for money. The group must deliver a quota (${c.QuotaPerPlayer} per miner in round 1, +{c.QuotaGrowth * 100:0}% each round). If the quota is missed everyone loses {c.QuotaMissMoneyLoss * 100:0}% of their money. The miner who delivered least is cursed: next round they start with {c.CurseHealthFraction * 100:0}% health. If exactly one miner is alive at round end they get a ${c.SurvivorBonus} bonus. Winner: most money after the last round, or the Infinity Stein holder.");
        Para(b, "Nobody can see your money, sack or steins. When you die everything you carry drops in a bag any living miner can loot; you become a ghost until next round (fly with WASD/Space/Ctrl, right mouse rattles something). Everyone starts with the Pickaxe, the Lantern and one random Common stein.");

        Head(b, "ORE");
        Para(b, string.Join("   ", Ores.All.Values.OrderBy(o => o.Value).Select(o => $"{o.Name} ${o.Value}")));

        foreach (var r in new[] { Rarity.Common, Rarity.Rare, Rarity.Legendary })
        {
            Head(b, ItemInfo.RarityName(r).ToUpper() + " STEINS" + (r == Rarity.Common ? "  (you start with one; two stein slots, F picks which is swapped)" : r == Rarity.Rare ? "  (shrines, chests, monster drops)" : "  (very rare)"));
            foreach (var d in Steins.All.Values.Where(d => d.Rarity == r)) Item(b, d.Name, d.Description, ItemInfo.RarityColor(r));
        }

        Head(b, "UTILITY ITEMS  (two slots, keys 1 / 2, G drops, right mouse uses)");
        foreach (var d in Utilities.All.Values) Item(b, d.Name, d.Description, UiTheme.Parchment);

        Head(b, "MONSTERS");
        Item(b, "Stone Golem", $"{F(MonsterTuning.GolemHp)} health. Slow, but its overhead slam ({F(MonsterTuning.GolemWindup)} s wind-up, {F(MonsterTuning.GolemSlamReach)} m reach) does {F(MonsterTuning.GolemSlamDamage)} damage. Armoured from the front (only {MonsterTuning.GolemFrontMul * 100:0}% damage); hits to the glowing crystal on its back do {MonsterTuning.GolemBackMul * 100:0}%. One miner distracts, another circles behind. Drops gems.", UiTheme.Parchment);
        Item(b, "Ember Elemental", $"{F(MonsterTuning.EmberHp)} health, fast ({F(MonsterTuning.EmberChase)} m/s). Lunges for {F(MonsterTuning.EmberLungeDamage)} and sets you on fire ({F(MonsterTuning.BurnDps)}/s for {F(MonsterTuning.EmberBurnSeconds)} s). Dies in water, and water puts you out.", UiTheme.Parchment);
        Item(b, "Gas Wisp", $"A drifting poison cloud, {F(MonsterTuning.WispGasDps)} damage per second inside it. Harmless with a gas mask; dynamite scatters it.", UiTheme.Parchment);
        Item(b, "Dragon", $"Round 3 and later, asleep on a hoard in the deepest room. Cannot be killed. Noise wakes it (mining, sprinting, dynamite). Its breath hits {F(MonsterTuning.DragonBreathRange)} m ahead for {F(MonsterTuning.DragonBreathDps)} damage per second after a {F(MonsterTuning.DragonWindup)} s wind-up: break line of sight. Monsters see a lit lantern from far away ({F(MonsterTuning.SightLantern)} m instead of {F(MonsterTuning.SightDark)} m).", UiTheme.Parchment);

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
