using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Registers the audio engine on every peer at startup.</summary>
[ContentModule]
public static class AudioModule
{
    public static void Register() => Sfx.Init();
}

/// <summary>
/// Turns state that already exists on every peer (miner positions/flags, NetObject Data, monster state, Game events) into sounds.
/// No audio RPCs: nothing here is sent over the network, and private facts (who picked up what) only ever sound for their owner.
/// </summary>
public partial class AudioDirector : Node
{
    public static AudioDirector I;

    class MT { public Vector3 Last; public float Accum, Air, MinVy; public bool Floor = true, Init; }
    class OT { public string Kind, Data; public Vector3 Pos, Last; public MonsterState St; public int Extra; public float Accum; public bool Init; }

    readonly Dictionary<ulong, MT> _miners = new();
    readonly Dictionary<int, OT> _objs = new();
    readonly List<(double t, Action a)> _queue = new();
    readonly HashSet<int> _seen = new();
    World _world;
    float _roundT, _objT, _ambT = 4f, _hbT;
    double _lastVeinBreak = -10, _speechUntil;
    Vector3 _lastVeinPos;
    bool _campMade, _eth;
    GamePhase _lastPhase = GamePhase.Lobby;
    int _prevSack = -1; List<string> _prevSteins = new(), _prevUtils = new();
    static double Now => Time.GetTicksMsec() / 1000.0;
    static readonly Random R = new();

    public override void _Ready()
    {
        I = this;
        var g = Game.I;
        g.WeighChanged += OnWeigh;
        g.PhaseChanged += OnPhase;
        g.OwnChanged += OnOwn;
        GoblinVoice.Said += OnSaid;
    }

    public override void _ExitTree()
    {
        GoblinVoice.Said -= OnSaid;
        if (Game.I != null) { Game.I.WeighChanged -= OnWeigh; Game.I.PhaseChanged -= OnPhase; Game.I.OwnChanged -= OnOwn; }
        if (I == this) I = null;
        Sfx.OnExit();
    }

    void After(float sec, Action a) => _queue.Add((Now + sec, a));

    // ------------------------------------------------------------ main tick
    public override void _PhysicsProcess(double delta)
    {
        try { Tick((float)delta); }
        catch (Exception e) { GD.Print("SFX: director error: " + e.Message); }
    }

    void Tick(float dt)
    {
        var g = Game.I; if (g == null) return;
        if (!Sfx.PollReady()) return;
        double now = Now;
        for (int i = _queue.Count - 1; i >= 0; i--)
            if (_queue[i].t <= now) { var a = _queue[i].a; _queue.RemoveAt(i); a(); }

        var w = g.World;
        if (w != _world) ResetWorld(w);
        bool inRound = w != null && GodotObject.IsInstanceValid(w) && w.IsInsideTree() && w.Cave != null && g.Phase == GamePhase.Round;
        if (inRound) { Sfx.Loop("amb_drone", SfxNames.AmbDrone); _roundT += dt; }
        else { Sfx.StopLoop("amb_drone"); _hbT = 0; }
        if (w == null || !GodotObject.IsInstanceValid(w) || !w.IsInsideTree()) return;

        if (!_campMade && w.Cave != null && w.IsInsideTree()) MakeStaticLoops(w);
        if (inRound) { Ambience(dt, w); LocalChecks(g, dt); }
        Miners(dt, w);
        _objT += dt;
        if (_objT >= 0.1f) { Objects(_objT, w); _objT = 0; }
    }

    void ResetWorld(World w)
    {
        _world = w; _miners.Clear(); _objs.Clear(); _roundT = 0; _campMade = false; _eth = false;
        Sfx.Prune();
    }

    void MakeStaticLoops(World w)
    {
        _campMade = true;
        var camp = new Node3D { Name = "SfxCamp" };
        w.AddChild(camp);
        camp.GlobalPosition = w.Cave.GoblinPos + new Vector3(2.5f, 0.3f, 0f);
        Sfx.Loop("camp", SfxNames.Campfire, camp);
        int i = 0;
        foreach (var z in w.Cave.WaterZones)
        {
            if (i >= 6) break;
            var n = new Node3D { Name = "SfxWater" + i };
            w.AddChild(n);
            n.GlobalPosition = z.GetCenter();
            Sfx.Loop("water" + i, SfxNames.WaterLap, n);
            i++;
        }
    }

    // ------------------------------------------------------------ ambience
    void Ambience(float dt, World w)
    {
        _ambT -= dt;
        if (_ambT > 0) return;
        _ambT = 2.5f + (float)R.NextDouble() * 5.5f;
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return;
        float a = (float)(R.NextDouble() * Math.PI * 2), d = 6f + (float)R.NextDouble() * 10f;
        var p = cam.GlobalPosition + new Vector3(Mathf.Cos(a) * d, (float)R.NextDouble() * 2.5f - 1f, Mathf.Sin(a) * d);
        double k = R.NextDouble();
        Sfx.Play(k < 0.55 ? SfxNames.AmbDrip : k < 0.75 ? SfxNames.AmbCreak : SfxNames.AmbSettle, p);
    }

    // ------------------------------------------------------------ miners: footsteps, jump, land
    void Miners(float dt, World w)
    {
        if (dt <= 0 || w.MinersRoot == null) return;
        foreach (var c in w.MinersRoot.GetChildren())
        {
            if (c is not Miner m || !m.IsInsideTree()) continue;
            if (!_miners.TryGetValue(m.GetInstanceId(), out var mt)) _miners[m.GetInstanceId()] = mt = new MT { Last = m.GlobalPosition };
            var p = m.GlobalPosition; var d = p - mt.Last; mt.Last = p;
            float hs = new Vector2(d.X, d.Z).Length() / dt, vy = d.Y / dt;
            if (hs > 15f || Math.Abs(vy) > 45f) { mt.Air = 0; continue; }   // teleport / respawn
            bool floor, crouch, sprint;
            if (m.IsLocal) { floor = m.IsOnFloor(); crouch = Input.IsActionPressed("crouch"); sprint = Input.IsActionPressed("sprint"); }
            else { int f = m.NetFlags; crouch = (f & 1) != 0; sprint = (f & 2) != 0; floor = (f & 4) != 0; }
            float vol = m.IsLocal ? -3f : 0f;

            if (floor && hs > 0.8f)
            {
                float stride = crouch ? 1.5f : sprint ? 2.6f : 2.1f;
                mt.Accum += hs * dt;
                if (mt.Accum >= stride) { mt.Accum -= stride; Step(m, w, crouch ? vol - 9f : sprint ? vol + 2f : vol); }
            }
            if (!floor)
            {
                mt.Air += dt; mt.MinVy = Math.Min(mt.MinVy, vy);
                if (mt.Floor && vy > 2.5f) Sfx.Play(SfxNames.Jump, p, vol);
            }
            else if (!mt.Floor)
            {
                if (mt.Air > 0.12f)
                {
                    if (mt.MinVy < -Miner.FallSafeSpeed) Sfx.Play(SfxNames.LandHard, p, vol);
                    else if (mt.MinVy < -3f) Sfx.Play(SfxNames.Land, p, vol);
                }
                mt.Air = 0; mt.MinVy = 0;
            }
            mt.Floor = floor;
        }
    }

    void Step(Miner m, World w, float db)
    {
        var p = m.GlobalPosition;
        string s = SfxNames.StepRock;
        if (w.Cave.InWater(p + Vector3.Up * 0.1f)) s = SfxNames.StepWater;
        else
        {
            var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.3f, p + Vector3.Down * 0.8f, 1 | 4);
            var hit = w.GetWorld3D().DirectSpaceState.IntersectRay(q);
            if (hit.Count > 0 && hit["collider"].As<GodotObject>() is NetObject) s = SfxNames.StepWood;   // rope bridge / platform
        }
        Sfx.Play(s, p, db);
    }

    // ------------------------------------------------------------ swing impact (called from Miner.PlaySwing on every peer)
    public static void OnSwing(Miner m)
    {
        if (!Sfx.Active || I == null) return;
        I.DoSwing(m);
    }

    void DoSwing(Miner m)
    {
        Sfx.Play(SfxNames.Swing, m.EyePos, m.IsLocal ? -3f : 0f);
        After(0.12f, () => Impact(m));
    }

    void Impact(Miner m)
    {
        if (!GodotObject.IsInstanceValid(m) || !m.IsInsideTree()) return;
        bool pick = Utilities.Get(m.Held)?.IsPickaxe == true;
        var o = m.EyePos; var d = m.LookDir;
        var q = PhysicsRayQueryParameters3D.Create(o, o + d * (pick ? 3.0f : 2.3f), 1 | 2 | 4 | 32);
        q.Exclude = new Godot.Collections.Array<Rid> { m.GetRid() };
        var hit = m.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return;
        var pos = hit["position"].AsVector3();
        var col = hit["collider"].As<GodotObject>();
        if (col is Miner vm)
        {
            Sfx.Play(pick ? SfxNames.HitMiner : SfxNames.Punch, pos);
            if (!vm.IsLocal) Sfx.Play(SfxNames.Grunt, vm.GlobalPosition + Vector3.Up * 1.5f);
        }
        else if (col is VeinObj v)
            Sfx.Play(pick ? OreSound(v.OreId) : SfxNames.Punch, pos, pick ? 0f : -4f);
        else if (col is MonsterObj) Sfx.Play(SfxNames.HitMiner, pos);
        else if (col is NetObject) return;
        else Sfx.Play(pick ? SfxNames.PickRock : SfxNames.Punch, pos, pick ? 0f : -6f);
    }

    static string OreSound(string ore) => ore switch
    {
        "iron" => SfxNames.PickIron, "silver" => SfxNames.PickSilver, "gold" => SfxNames.PickGold,
        "ruby" => SfxNames.PickRuby, "diamond" => SfxNames.PickDiamond, _ => SfxNames.PickCoal,
    };

    // ------------------------------------------------------------ networked objects and monsters (all peers see the same Data)
    static string Get(string data, string key)
    {
        foreach (var part in data.Split(';'))
        {
            int i = part.IndexOf('=');
            if (i > 0 && part.Substring(0, i) == key) return part.Substring(i + 1);
        }
        return "";
    }

    void Objects(float dt, World w)
    {
        _seen.Clear();
        bool fresh = _roundT > 1.5f;
        foreach (var kv in w.Objects)
        {
            var o = kv.Value;
            if (o == null || !GodotObject.IsInstanceValid(o) || !o.IsInsideTree()) continue;
            _seen.Add(kv.Key);
            var pos = o.GlobalPosition;
            if (!_objs.TryGetValue(kv.Key, out var t))
            {
                _objs[kv.Key] = t = new OT { Kind = o.Kind, Data = o.Data, Pos = pos, Last = pos };
                if (o is MonsterObj m0) { t.St = m0.State; t.Extra = m0.Extra; }
                if (fresh) OnNew(o, t);
            }
            else if (o.Data != t.Data) { OnData(o, t, o.Data); t.Data = o.Data; }
            t.Pos = pos;
            if (o is MonsterObj m) Monster(m, t, dt);
            Loops(o, t);
        }
        List<int> gone = null;
        foreach (var id in _objs.Keys) if (!_seen.Contains(id)) (gone ??= new()).Add(id);
        if (gone != null)
            foreach (var id in gone)
            {
                var t = _objs[id]; _objs.Remove(id);
                Sfx.StopLoop("o" + id);
                if (!fresh) continue;
                if (t.Kind == "vein") { Sfx.Play(SfxNames.VeinBreak, t.Pos + Vector3.Up * 0.7f); _lastVeinBreak = Now; _lastVeinPos = t.Pos; }
                else if (t.Kind == "rope") Sfx.Play(SfxNames.RopeCut, t.Pos);
            }
    }

    void OnNew(NetObject o, OT t)
    {
        var p = t.Pos;
        switch (t.Kind)
        {
            case "pickup":
                if (Now - _lastVeinBreak < 0.6 && p.DistanceTo(_lastVeinPos) < 3f) break;   // ore popping out of a broken vein
                Sfx.Play(SfxNames.ItemDrop, p); break;
            case "rope": Sfx.Play(SfxNames.RopePlace, p); break;
            case "beartrap": Sfx.Play(SfxNames.PlateClick, p, -4f); break;
        }
    }

    void OnData(NetObject o, OT t, string now)
    {
        var p = t.Pos + Vector3.Up * 0.5f;
        switch (t.Kind)
        {
            case "chest": if (Get(now, "open") == "1" && Get(t.Data, "open") != "1") Sfx.Play(SfxNames.ChestOpen, p); break;
            case "shrine":
                string a = Get(t.Data, "state"), b = Get(now, "state");
                if (b == "done" && a != "done") Sfx.Play(SfxNames.ShrineSuccess, p);
                else if (a == "running" && b != "running" && b != "done") Sfx.Play(SfxNames.ShrineFail, p);
                break;
            case "shrineplate":
                bool pa = Get(t.Data, "pressed") == "1", pb = Get(now, "pressed") == "1";
                if (pa != pb) Sfx.Play(SfxNames.PlateClick, p, pb ? 0f : -6f);
                break;
            case "beartrap": if (Get(now, "armed") == "0" && Get(t.Data, "armed") != "0") Sfx.Play(SfxNames.BearTrapSnap, p); break;
        }
    }

    void Loops(NetObject o, OT t)
    {
        string want = null;
        switch (t.Kind)
        {
            case "shrine": if (Get(o.Data, "state") != "done") want = SfxNames.ShrineHum; break;
            case "dynamite": want = SfxNames.FuseHiss; break;
            case "ember": if (o is MonsterObj e && e.State != MonsterState.Stunned && e.State != MonsterState.Dispersed) want = SfxNames.EmberCrackle; break;
            case "wisp": if (o is MonsterObj ws && ws.State != MonsterState.Dispersed) want = SfxNames.WispHiss; break;
            case "dragon": if (o is MonsterObj dr && dr.State == MonsterState.Asleep) want = SfxNames.DragonBreath; break;
        }
        string key = "o" + o.Id;
        if (want != null) Sfx.Loop(key, want, o); else if (Sfx.IsLooping(key)) Sfx.StopLoop(key);
    }

    void Monster(MonsterObj m, OT t, float dt)
    {
        var p = m.GlobalPosition + Vector3.Up * 1f;
        var st = m.State; var old = t.St;
        bool alive = st != MonsterState.Stunned && st != MonsterState.Dispersed;
        switch (t.Kind)
        {
            case "golem":
                if (st != old)
                {
                    if (st == MonsterState.Windup) Sfx.Play(SfxNames.GolemWindup, p);
                    else if (st == MonsterState.Attack) Sfx.Play(SfxNames.GolemSlam, m.GlobalPosition);
                    else if (st == MonsterState.Stunned) Sfx.Play(SfxNames.GolemStun, p);
                }
                if (st == MonsterState.Stunned && old != MonsterState.Stunned) Sfx.Play(SfxNames.GolemCrack, p + Vector3.Up);
                if (alive && (st == MonsterState.Wander || st == MonsterState.Investigate || st == MonsterState.Chase || st == MonsterState.Return))
                {
                    var d = m.GlobalPosition - t.Last;
                    float hs = new Vector2(d.X, d.Z).Length() / dt;
                    if (hs > 0.3f && hs < 12f) { t.Accum += hs * dt; if (t.Accum >= 2.4f) { t.Accum -= 2.4f; Sfx.Play(SfxNames.GolemStep, m.GlobalPosition); } }
                }
                break;
            case "ember":
                if (st != old)
                {
                    if (st == MonsterState.Attack) Sfx.Play(SfxNames.EmberLunge, p);
                    else if (st == MonsterState.Stunned) Sfx.Play(SfxNames.EmberSizzle, p);
                }
                break;
            case "dragon":
                if (old == MonsterState.Asleep && st != MonsterState.Asleep) Sfx.Play(SfxNames.DragonRoar, p);
                else if (st == MonsterState.Attack && old != MonsterState.Attack) Sfx.Play(SfxNames.DragonFire, p);
                if (st == MonsterState.Asleep && m.Extra > t.Extra) Sfx.Play(SfxNames.DragonStir, p, m.Extra > 1 ? 2f : -3f);
                break;
        }
        t.Last = m.GlobalPosition; t.St = st; t.Extra = m.Extra;
    }

    // ------------------------------------------------------------ own-player checks
    void LocalChecks(Game g, float dt)
    {
        // final-ten-seconds heartbeat
        if (g.TimeLeft > 0 && g.TimeLeft <= 10f && g.LocalMiner != null)
        {
            _hbT -= dt;
            if (_hbT <= 0) { Sfx.PlayUi(SfxNames.Heartbeat); _hbT = Mathf.Lerp(1.0f, 0.55f, (10f - g.TimeLeft) / 10f); }
        }
        else _hbT = 0;

        // sack full: pressing E on loose ore with no room (client-side prediction of the server's refusal)
        var lm = g.LocalMiner;
        if (lm != null && Input.IsActionJustPressed("interact") && lm.Target is PickupObj po && po.Data.StartsWith("ore:") && g.OwnInv.Sack.Count >= g.OwnSackCap)
            Sfx.PlayUi(SfxNames.SackFull);

        // ethereal stein activated (owner only)
        bool eth = g.OwnEtherealUntil > Now;
        if (eth && !_eth && lm != null) Sfx.Play(SfxNames.EtherealWhoosh, lm.GlobalPosition + Vector3.Up);
        _eth = eth;
    }

    // ------------------------------------------------------------ game events
    void OnOwn()
    {
        var g = Game.I; var inv = g.OwnInv;
        if (inv == null) return;
        bool settled = _roundT > 2f && g.Phase == GamePhase.Round && _prevSack >= 0;
        if (settled)
        {
            foreach (var s in inv.Steins)
                if (!_prevSteins.Contains(s))
                {
                    var r = Steins.Get(s)?.Rarity ?? Rarity.Common;
                    Sfx.PlayUi(r == Rarity.Legendary ? SfxNames.SteinPickupLegend : r == Rarity.Rare ? SfxNames.SteinPickupRare : SfxNames.SteinPickup);
                }
            bool got = inv.Sack.Count > _prevSack;
            foreach (var u in inv.Utils) if (!string.IsNullOrEmpty(u) && !_prevUtils.Contains(u)) got = true;
            if (got) Sfx.PlayUi(SfxNames.OrePickup);
        }
        _prevSack = inv.Sack.Count; _prevSteins = new List<string>(inv.Steins); _prevUtils = new List<string>(inv.Utils);
    }

    string _lastWeighStage = ""; int _lastReveals;

    /// <summary>Weighing ceremony sounds: the lift leaving, a tick per pile, the verdict stinger, the throw.</summary>
    void OnWeigh()
    {
        var w = Game.I.Weigh;
        if (w == null) { _lastWeighStage = ""; _lastReveals = 0; return; }
        if (w.Stage == "intro" && _lastWeighStage != "intro")
        {
            Sfx.Play(SfxNames.LiftRumble);
            After(1.4f, () => Sfx.Play(SfxNames.RockCrash));
        }
        if (w.Revealed.Count > _lastReveals) Sfx.PlayUi(w.CurWeight == 0 ? SfxNames.StingerVerdict : SfxNames.Feed);
        _lastReveals = w.Revealed.Count;
        if (w.Stage != _lastWeighStage)
        {
            if (w.Stage == "verdict") Sfx.PlayUi(SfxNames.StingerVerdict);
            else if (w.Stage == "throw") After(0.3f, () => Sfx.PlayUi(SfxNames.StingerThrow));
            else if (w.Stage == "remain") Sfx.PlayUi(SfxNames.StingerSafe);
        }
        _lastWeighStage = w.Stage;
    }

    void OnPhase()
    {
        var g = Game.I;
        var ph = g.Phase;
        if (ph == _lastPhase) return;
        _lastPhase = ph;
        if (ph == GamePhase.Round) { _prevSack = -1; _roundT = 0; }
        if (ph == GamePhase.MatchEnd) After(0.5f, () => Sfx.PlayUi(SfxNames.Fanfare));
    }

    void OnSaid(GoblinSaying s)
    {
        if (s?.Text == null) return;
        int n = Mathf.Clamp(s.Text.Length / 5, 3, 12);
        double start = Math.Max(Now, _speechUntil);
        _speechUntil = start + n * 0.085 + 0.25;
        Vector3? pos = null;
        var w = Game.I.World;
        if (w?.Cave != null && GodotObject.IsInstanceValid(w)) pos = w.Cave.GoblinPos + Vector3.Up * 1.4f;
        for (int i = 0; i < n; i++)
        {
            double delay = start - Now + i * 0.085 + R.NextDouble() * 0.02;
            After((float)delay, () => { if (pos is Vector3 p) Sfx.Play(SfxNames.GoblinBlip, p); else Sfx.PlayUi(SfxNames.GoblinBlip); });
        }
    }
}
