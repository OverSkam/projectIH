using System.Collections.Generic;
using System.Reflection;

namespace AppStein;

/// <summary>Every sound name. Call sites use these constants; `--sfxcheck` verifies each has a recipe.</summary>
public static class SfxNames
{
    // movement
    public const string StepRock = "step_rock", StepWood = "step_wood", StepWater = "step_water";
    public const string Jump = "jump", Land = "land", LandHard = "land_hard";
    // combat / mining
    public const string Swing = "swing", PickRock = "pick_rock", VeinBreak = "vein_break", Punch = "punch", HitMiner = "hit_miner";
    public const string PickCoal = "pick_coal", PickIron = "pick_iron", PickSilver = "pick_silver", PickGold = "pick_gold", PickRuby = "pick_ruby", PickDiamond = "pick_diamond";
    public const string Hurt = "hurt", Grunt = "grunt", Death = "death";
    // items
    public const string OrePickup = "ore_pickup", SteinPickup = "stein_pickup", SteinPickupRare = "stein_pickup_rare", SteinPickupLegend = "stein_pickup_legend";
    public const string ItemDrop = "item_drop", SackFull = "sack_full";
    // goblin
    public const string Feed = "feed", GoblinLaugh = "goblin_laugh", GoblinGrowl = "goblin_growl", GoblinBlip = "goblin_blip";
    // ui / round
    public const string UiClick = "ui_click", UiHover = "ui_hover", UiOpen = "ui_open", UiClose = "ui_close";
    public const string RoundStart = "round_start", Warning = "warning", Heartbeat = "heartbeat";
    public const string LiftRumble = "lift_rumble", RockCrash = "rock_crash";
    public const string StingerSafe = "stinger_safe", StingerVerdict = "stinger_verdict", StingerThrow = "stinger_throw", Fanfare = "fanfare";
    // world objects / items in use
    public const string ChestOpen = "chest_open", ShrineHum = "shrine_hum", ShrineSuccess = "shrine_success", ShrineFail = "shrine_fail";
    public const string PlateClick = "plate_click", RopePlace = "rope_place", RopeCut = "rope_cut", FuseHiss = "fuse_hiss", Explosion = "explosion";
    public const string BearTrapSnap = "beartrap_snap", LanternOn = "lantern_on", LanternOut = "lantern_out", EtherealWhoosh = "ethereal_whoosh", GhostPebble = "ghost_pebble";
    // traps
    public const string SpikeClick = "spike_click", SpikeThrust = "spike_thrust", RockfallRumble = "rockfall_rumble", RockfallCrash = "rockfall_crash", RockPebble = "rock_pebble";
    public const string GasHiss = "gas_hiss", DartTwang = "dart_twang", DartWhoosh = "dart_whoosh", DartThud = "dart_thud";
    public const string TrapdoorCreak = "trapdoor_creak", TrapdoorDrop = "trapdoor_drop", LeverClunk = "lever_clunk", GateChains = "gate_chains", GateSlam = "gate_slam";
    // monsters
    public const string GolemStep = "golem_step", GolemWindup = "golem_windup", GolemSlam = "golem_slam", GolemCrack = "golem_crack", GolemStun = "golem_stun";
    public const string EmberCrackle = "ember_crackle", EmberLunge = "ember_lunge", EmberSizzle = "ember_sizzle", WispHiss = "wisp_hiss";
    public const string DragonBreath = "dragon_breath", DragonStir = "dragon_stir", DragonRoar = "dragon_roar", DragonFire = "dragon_fire";
    public const string PlugCreep = "plug_creep", PlugCrush = "plug_crush", WicksChitter = "wicks_chitter", WicksDrain = "wicks_drain";
    public const string LampSnuff = "lamp_snuff", LampGrab = "lamp_grab", AssessorStep = "assessor_step", AssessorStamp = "assessor_stamp";
    // ambience
    public const string AmbDrone = "amb_drone", AmbDrip = "amb_drip", AmbCreak = "amb_creak", AmbSettle = "amb_settle", Campfire = "campfire", WaterLap = "water_lap";

    static List<string> _all;
    /// <summary>All constants above (registry used by --sfxcheck and the recipe table).</summary>
    public static IReadOnlyList<string> All
    {
        get
        {
            if (_all != null) return _all;
            _all = new List<string>();
            foreach (var f in typeof(SfxNames).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string)) _all.Add((string)f.GetRawConstantValue());
            return _all;
        }
    }
}
