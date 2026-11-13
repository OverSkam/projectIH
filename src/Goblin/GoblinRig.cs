using System;
using Godot;

namespace AppStein;

/// <summary>
/// The Goblin: a GIANT (about 9 m tall seated) caricature of a rich financier at the north end of the Hall of the Scale. Green skin, swept-back grey hair,
/// heavy brows, big hooked nose, long ears, heavy lids, smug half-smile, navy quarter-zip with a popped collar. Faces +Z (south, toward the miners).
/// Built from faceted primitives in code (see <see cref="GoblinParts"/>); lit from below by a sickly green-yellow glow so the face is the brightest thing in the game.
/// Animation (all procedural): breathing, blinking, ear twitch, eyes and head slowly follow the nearest miner, the jaw moves while he speaks (GoblinVoice.Said),
/// reactions (happy / angry). The right hand (world -X) is a huge open palm: his resting pose (PalmPos). At the weighing it reaches for each miner's sack in turn, lifts it
/// to the palm and holds it there while the number counts up (HallCeremony shows sack and number, this class publishes HallCeremony.PalmWorld); the left arm is a stretchy
/// two-bone IK arm that reaches the thrown miner, lifts and swings them over the shaft and lets go in sync with Miner.StartThrow (it reads Miner.ThrowU).
/// </summary>
public partial class GoblinRig : Node3D
{
    public static event Action<string> Reaction;
    public static void React(string kind) => Reaction?.Invoke(kind);

    Node3D _torso, _head, _jaw, _earL, _earR, _browL, _browR, _lidL, _lidR, _eyeL, _eyeR;
    readonly Node3D[] _upper = new Node3D[2], _fore = new Node3D[2], _upperHold = new Node3D[2], _foreHold = new Node3D[2], _hand = new Node3D[2], _elbowBall = new Node3D[2];
    readonly Node3D[][] _fingers = new Node3D[2][], _tips = new Node3D[2][];
    readonly Node3D[] _thumb = new Node3D[2];
    OmniLight3D _victimLight, _faceLight;
    Vector3 _wristR = GoblinParts.PalmWrist, _handFrom = GoblinParts.PalmWrist, _handDirR = new(0, -0.05f, 1f);
    double _lastRevealAt = -1; float _sackLean;
    float _t, _earTimer = 2f, _earTwitch, _blinkTimer = 3f, _blink, _speakT, _chewT, _react, _headYaw, _headPitch, _eyeYaw, _eyePitch, _lean, _torsoYaw, _armBlend, _curlL, _roll;
    string _reactKind = "";
    Vector3 _gripLast, _wristL = GoblinParts.RestWristL;
    float _releaseT = -1f;

    public override void _Ready()
    {
        Build();
        Reaction += OnReact;
        if (Game.I != null) Game.I.GoblinCue += OnCue;
        GoblinVoice.Said += OnSaid;
    }

    public override void _ExitTree()
    {
        Reaction -= OnReact;
        GoblinVoice.Said -= OnSaid;
        if (Game.I != null) Game.I.GoblinCue -= OnCue;
    }

    void OnReact(string kind)
    {
        _reactKind = kind; _react = kind == "happy" ? 2.4f : 2.0f;
        Sfx.Play(kind == "happy" ? SfxNames.GoblinLaugh : SfxNames.GoblinGrowl, GlobalPosition + new Vector3(0, 6f, 3f));
    }
    void OnCue(string cat, string seed, string a0, string a1, string mood) { }
    void OnSaid(GoblinSaying s) { _speakT = Mathf.Clamp(0.9f + s.Text.Length * 0.055f, 1.2f, 7f); }

    /// <summary>Called by GoblinObj.Build(): collision (layers 1|4) + the animated rig. The host sits under the resting palm; the body is built at CaveInfo.GoblinSeat.</summary>
    public static void Attach(NetObject host)
    {
        host.CollisionLayer = 1 | 4; host.CollisionMask = 0;
        var cave = Game.I?.World?.Cave;
        var seat = cave != null && cave.GoblinSeat != default ? cave.GoblinSeat : host.GlobalPosition + new Vector3(4.2f, 0, -4.4f);
        var off = seat - host.GlobalPosition;
        host.AddChild(new GoblinRig { Name = "Rig", Position = off });
        void Shape(Vector3 c, Vector3 s) => host.AddChild(Util.BoxShape(s, c + off));
        Shape(new Vector3(0, 0.5f, -0.3f), new Vector3(8.6f, 1.0f, 4.8f));          // throne
        Shape(new Vector3(0, 3.1f, -0.1f), new Vector3(5.6f, 3.6f, 3.6f));           // torso
        Shape(new Vector3(0, 9.0f, 1.5f), new Vector3(6.0f, 6.5f, 5.5f));              // head
        Shape(new Vector3(-1.6f, 1.2f, 3.4f), new Vector3(1.9f, 2.4f, 5.0f));        // right leg
        Shape(new Vector3(1.6f, 1.2f, 3.4f), new Vector3(1.9f, 2.4f, 5.0f));         // left leg
        Shape(new Vector3(-4.2f, 3.2f, -0.2f), new Vector3(1.6f, 3.2f, 1.7f));       // right upper arm
        Shape(new Vector3(-4.3f, 1.9f, 1.3f), new Vector3(1.4f, 1.6f, 2.0f));        // right forearm
        Shape(new Vector3(4.2f, 3.2f, -0.2f), new Vector3(1.6f, 3.2f, 1.7f));        // left upper arm
        Shape(new Vector3(-4.2f, 1.2f, 4.4f), new Vector3(3.0f, 0.5f, 3.4f));        // the palm (top at y = 1.45)
    }

    // ------------------------------------------------------------------ build
    void Build()
    {
        GoblinParts.Throne(this);
        GoblinParts.Legs(this);
        _torso = new Node3D { Position = GoblinParts.TorsoPos, Name = "Torso" };
        AddChild(_torso);
        GoblinParts.Torso(_torso);

        _head = new Node3D { Position = GoblinParts.HeadPos, Name = "Head", Scale = Vector3.One * GoblinParts.HeadScale };
        AddChild(_head);
        GoblinParts.HeadStatic(_head);
        _jaw = GoblinParts.Jaw(_head);
        _eyeL = GoblinParts.Eye(_head, -1); _eyeR = GoblinParts.Eye(_head, 1);
        _lidL = GoblinParts.Lid(_head, -1); _lidR = GoblinParts.Lid(_head, 1);
        _browL = GoblinParts.EyeBrow(_head, -1); _browR = GoblinParts.EyeBrow(_head, 1);
        _earL = GoblinParts.Ear(_head, -1); _earR = GoblinParts.Ear(_head, 1);

        for (int i = 0; i < 2; i++)
        {
            _upper[i] = new Node3D { Name = "Upper" + i }; AddChild(_upper[i]);
            _upperHold[i] = new Node3D(); _upper[i].AddChild(_upperHold[i]);
            GoblinParts.UpperArm(_upperHold[i]);
            GoblinParts.Ball(_upper[i], 0.85f, GoblinParts.NavyLight);
            _fore[i] = new Node3D { Name = "Fore" + i }; AddChild(_fore[i]);
            _foreHold[i] = new Node3D(); _fore[i].AddChild(_foreHold[i]);
            GoblinParts.ForeArm(_foreHold[i]);
            _elbowBall[i] = GoblinParts.Ball(_fore[i], 0.68f, GoblinParts.Navy);
            _hand[i] = new Node3D { Name = "Hand" + i }; AddChild(_hand[i]);
            GoblinParts.Hand(_hand[i], out _fingers[i], out _tips[i], out _thumb[i]);
        }


        // lighting: sickly green-yellow from below, so the face is the brightest, most readable thing
        _faceLight = new OmniLight3D { Position = new Vector3(0, 4.4f, 6.2f), LightColor = new Color(0.86f, 0.94f, 0.52f), LightEnergy = 8.0f, OmniRange = 17f, OmniAttenuation = 1.0f, ShadowEnabled = false };
        AddChild(_faceLight);
        AddChild(new OmniLight3D { Position = new Vector3(0, 3.2f, 7.5f), LightColor = new Color(0.65f, 0.9f, 0.3f), LightEnergy = 0.5f, OmniRange = 9f, ShadowEnabled = false });
        AddChild(new OmniLight3D { Position = new Vector3(-4.2f, 1.2f, 6.2f), LightColor = new Color(0.75f, 0.95f, 0.3f), LightEnergy = 0.5f, OmniRange = 6.5f, ShadowEnabled = false });
        AddChild(new OmniLight3D { Position = new Vector3(0, 11.2f, 0.5f), LightColor = new Color(0.50f, 0.85f, 0.25f), LightEnergy = 1.0f, OmniRange = 8f, ShadowEnabled = false });
        foreach (float s in new[] { -1f, 1f })
            AddChild(new OmniLight3D { Position = new Vector3(5.0f * s, 8.5f, 4.6f), LightColor = new Color(0.55f, 0.82f, 0.25f), LightEnergy = 1.1f, OmniRange = 8f, ShadowEnabled = false });
        // follows the miner being thrown so the throw is easy to see
        _victimLight = new OmniLight3D { LightColor = new Color(1f, 0.93f, 0.62f), LightEnergy = 5f, OmniRange = 10f, Visible = false, TopLevel = true, ShadowEnabled = false };
        AddChild(_victimLight);
    }

    // ------------------------------------------------------------------ IK + bones
    /// <summary>Two-bone arm with stretchy bones (the giant's arm can reach about 1.5x its resting length). Returns the wrist position actually used.</summary>
    Vector3 SolveArm(int i, Vector3 shoulder, Vector3 wrist, Vector3 pole, Vector3 handDir, float roll, float maxStretch = 1.5f)
    {
        float l1 = GoblinParts.UpperLen, l2 = GoblinParts.ForeLen;
        var d = wrist - shoulder; float dist = d.Length();
        float maxReach = (l1 + l2) * maxStretch;
        if (dist > maxReach) { wrist = shoulder + d / dist * maxReach; dist = maxReach; }
        float stretch = Mathf.Max(1f, dist / ((l1 + l2) * 0.985f));
        float a1 = l1 * stretch, a2 = l2 * stretch;
        var dir = dist > 1e-4f ? d / dist : Vector3.Down;
        float a = (dist * dist + a1 * a1 - a2 * a2) / (2f * Mathf.Max(dist, 1e-3f));
        float h = Mathf.Sqrt(Mathf.Max(0f, a1 * a1 - a * a));
        var perp = pole - dir * pole.Dot(dir);
        perp = perp.LengthSquared() < 1e-4f ? Vector3.Up : perp.Normalized();
        var elbow = shoulder + dir * a + perp * h;
        Orient(_upper[i], shoulder, elbow, _upperHold[i], a1);
        Orient(_fore[i], elbow, wrist, _foreHold[i], a2);
        // the hand continues along the forearm / the requested direction, rolled about its axis
        var hd = handDir.LengthSquared() > 1e-4f ? handDir.Normalized() : (wrist - elbow).Normalized();
        var hb = Basis.LookingAt(hd, Mathf.Abs(hd.Y) > 0.95f ? Vector3.Forward : Vector3.Up) * new Basis(Vector3.Back, roll);
        _hand[i].Transform = new Transform3D(hb, wrist);
        return wrist;
    }

    static void Orient(Node3D bone, Vector3 from, Vector3 to, Node3D holder, float len)
    {
        var dir = to - from;
        if (dir.LengthSquared() < 1e-6f) return;
        bone.Transform = new Transform3D(Basis.LookingAt(dir.Normalized(), Mathf.Abs(dir.Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up), from);
        holder.Scale = new Vector3(1, 1, len);
    }

    void Curl(int i, float c1, float c2, float thumb)
    {
        for (int k = 0; k < 4; k++)
        {
            _fingers[i][k].Rotation = new Vector3(c1 * (0.9f + 0.1f * k), 0, 0);
            _tips[i][k].Rotation = new Vector3(c2 * (0.9f + 0.05f * k), 0, 0);
        }
        _thumb[i].Rotation = new Vector3(thumb * 0.8f, -0.5f + thumb * 0.5f, 0);
    }

    // ------------------------------------------------------------------ animation
    public override void _Process(double delta)
    {
        if (_head == null) return;
        float dt = Mathf.Min((float)delta, 0.1f);
        _t += dt;
        if (_react > 0) { _react -= dt; if (_react <= 0) _reactKind = ""; }
        _speakT = Mathf.Max(0, _speakT - dt); _chewT = Mathf.Max(0, _chewT - dt);

        // who is being thrown / who is nearest
        Miner victim = null; Vector3? look = null;
        var world = Game.I?.World;
        float nearest = float.MaxValue;
        if (world?.MinersRoot != null)
            foreach (var m in world.Miners())
            {
                if (!IsInstanceValid(m) || !m.IsInsideTree()) continue;
                if (m.IsThrown) { victim = m; continue; }
                float dd = m.GlobalPosition.DistanceTo(GlobalPosition);
                if (dd < nearest && dd < 40f) { nearest = dd; look = m.GlobalPosition + new Vector3(0, 1.6f, 0); }
            }
        if (victim != null) look = victim.GlobalPosition + new Vector3(0, 1.0f, 0);

        // breathing
        float br = Mathf.Sin(_t * 1.5f);
        _torso.Scale = new Vector3(1f + 0.006f * br, 1f + 0.014f * br, 1f + 0.01f * br);
        float bob = _react > 0 && _reactKind == "happy" ? Mathf.Abs(Mathf.Sin(_t * 9f)) * 0.08f : 0f;
        _torso.Position = GoblinParts.TorsoPos + new Vector3(0, bob, 0);

        // lean + twist while reaching for the victim
        float u = victim != null ? victim.ThrowU : -1f;
        float leanT = 0f, twistT = 0f;
        if (victim != null)
        {
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(u / Miner.ThrowReachEnd, 0f, 1f));
            float down = u > Miner.ThrowSwingEnd ? 1f - Mathf.SmoothStep(0f, 1f, (u - Miner.ThrowSwingEnd) / 0.18f) : 1f;
            leanT = 0.55f * up * down;
            var lv = ToLocal(victim.GlobalPosition);
            twistT = Mathf.Clamp(Mathf.Atan2(lv.X - 0f, Mathf.Max(2f, lv.Z)) * 0.5f, -0.3f, 0.3f) * up * down;
        }
        // the weighing: which sack is he reaching for (the current reveal), and how far along is the hand
        var gv = Game.I; var wv = gv?.Weigh;
        bool sackCeremony = victim == null && gv != null && gv.Phase == GamePhase.Weighing && wv != null && wv.CurPeer >= 0 && HallCeremony.I != null && (wv.Stage == "reveal" || wv.Stage == "verdict" || wv.Stage == "throw");
        float rk1 = 0f, rk2 = 0f;
        if (sackCeremony)
        {
            double tr = wv.Stage == "reveal" ? Time.GetTicksMsec() / 1000.0 - wv.CurAt : 999.0;
            rk1 = Mathf.Clamp((float)(tr / Mathf.Max(0.05f, wv.GrabDelay)), 0f, 1f);
            rk2 = Mathf.Clamp((float)((tr - wv.GrabDelay) / Mathf.Max(0.05f, wv.CountDelay - wv.GrabDelay)), 0f, 1f);
            _sackLean = Mathf.Lerp(_sackLean, 0.5f * Mathf.SmoothStep(0f, 1f, rk1) * (1f - 0.55f * Mathf.SmoothStep(0f, 1f, rk2)), Mathf.Min(1f, dt * 10f));
        }
        else _sackLean = Mathf.Lerp(_sackLean, 0f, Mathf.Min(1f, dt * 3f));
        leanT += _sackLean;
        _lean = Mathf.Lerp(_lean, GoblinParts.RestLean + leanT, Mathf.Min(1f, dt * 8f));
        _torsoYaw = Mathf.Lerp(_torsoYaw, twistT, Mathf.Min(1f, dt * 6f));
        _torso.Rotation = new Vector3(_lean + 0.006f * br, _torsoYaw, 0);

        // head and eyes follow
        float ty = 0.25f * Mathf.Sin(_t * 0.35f), tp = 0.04f;
        if (look.HasValue)
        {
            var l = ToLocal(look.Value) - GoblinParts.HeadPos;
            ty = Mathf.Clamp(Mathf.Atan2(l.X, Mathf.Max(0.5f, l.Z)), -0.7f, 0.7f);
            tp = Mathf.Clamp(Mathf.Atan2(-l.Y, new Vector2(l.X, l.Z).Length()), -0.35f, 0.5f);
        }
        _headYaw = Mathf.Lerp(_headYaw, ty * 0.8f, Mathf.Min(1f, dt * 1.4f));
        _headPitch = Mathf.Lerp(_headPitch, tp * 0.7f, Mathf.Min(1f, dt * 1.4f));
        float shake = _react > 0 && _reactKind == "angry" ? Mathf.Sin(_t * 38f) * 0.012f : 0f;
        float nod = _react > 0 && _reactKind == "happy" ? Mathf.Sin(_t * 9f) * 0.03f : 0f;
        _head.Position = GoblinParts.HeadPos + _torso.Position - GoblinParts.TorsoPos + new Vector3(0, 0.03f * br - _lean * 0.9f, (_lean - GoblinParts.RestLean) * 4.2f);
        _head.Rotation = new Vector3(_headPitch + _lean * 0.35f + nod, _headYaw + shake + _torsoYaw * 0.4f, shake * 2f + 0.03f);
        float ey = Mathf.Clamp(ty - _headYaw, -0.4f, 0.4f), ep = Mathf.Clamp(tp - _headPitch, -0.3f, 0.3f);
        _eyeYaw = Mathf.Lerp(_eyeYaw, ey, Mathf.Min(1f, dt * 7f)); _eyePitch = Mathf.Lerp(_eyePitch, ep, Mathf.Min(1f, dt * 7f));
        _eyeL.Rotation = _eyeR.Rotation = new Vector3(_eyePitch, _eyeYaw, 0);

        // blink (heavy lids)
        _blinkTimer -= dt;
        if (_blinkTimer <= 0) { _blinkTimer = 2.5f + (float)GD.RandRange(0, 4.5); _blink = 0.2f; }
        _blink = Mathf.Max(0, _blink - dt);
        float closed = _blink > 0 ? Mathf.Sin(_blink / 0.2f * Mathf.Pi) : 0f;
        float lidDrop = 0.0f + closed * 0.2f + (_reactKind == "angry" && _react > 0 ? 0.04f : 0f);
        _lidL.Position = new Vector3(-0.5f, 0.32f - lidDrop, 1.02f); _lidR.Position = new Vector3(0.5f, 0.32f - lidDrop, 1.02f);
        _lidL.Scale = _lidR.Scale = new Vector3(1f, 1f + closed * 0.9f, 1f);

        // brows: smug (one raised), angry (inner ends down), happy (both up)
        float raiseL = 0.02f, raiseR = 0.11f, slant = 0.1f;
        if (_react > 0 && _reactKind == "happy") { raiseL = raiseR = 0.14f; slant = 0f; }
        else if (_react > 0 && _reactKind == "angry") { raiseL = raiseR = -0.04f; slant = -0.32f; }
        _browL.Position = new Vector3(-0.52f, 0.7f + raiseL, 1.08f); _browR.Position = new Vector3(0.52f, 0.7f + raiseR, 1.08f);
        _browL.Rotation = new Vector3(0, 0, -slant); _browR.Rotation = new Vector3(0, 0, slant);

        // jaw: closed smirk; speaking = syllable flaps; chewing = slow grind; laughing/growling = wide
        float jaw = 0f, jawYaw = 0f;
        if (_speakT > 0) jaw = 0.05f + 0.16f * Mathf.Abs(Mathf.Sin(_t * 8.5f + Mathf.Sin(_t * 3.1f) * 2f)) * Mathf.Min(1f, _speakT * 2f);
        else if (_chewT > 0) { jaw = 0.04f + 0.1f * (0.5f + 0.5f * Mathf.Sin(_t * 7f)); jawYaw = Mathf.Sin(_t * 3.5f) * 0.05f; }
        if (_react > 0) jaw = Mathf.Max(jaw, _reactKind == "happy" ? 0.1f + 0.1f * Mathf.Abs(Mathf.Sin(_t * 10f)) : 0.14f);
        _jaw.Rotation = new Vector3(jaw, jawYaw, 0);

        // ears
        _earTimer -= dt;
        if (_earTimer <= 0) { _earTimer = 2.5f + (float)GD.RandRange(0, 4.5); _earTwitch = 0.35f; }
        _earTwitch = Mathf.Max(0, _earTwitch - dt);
        float tw = _earTwitch > 0 ? Mathf.Sin(_earTwitch * 55f) * 0.16f * (_earTwitch / 0.35f) : 0f;
        _earL.Rotation = new Vector3(0, 0, -tw); _earR.Rotation = new Vector3(0, 0, tw * 0.4f);

        // ---- arms
        var tm = _torso.Transform;
        var shR = tm * (GoblinParts.ShoulderR - GoblinParts.TorsoPos); var shL = tm * (GoblinParts.ShoulderL - GoblinParts.TorsoPos);
        // right: the open palm, palm up, fingers toward the miners (resting pose). At the weighing it fetches the current sack: reach (palm under the sack), lift to the hold point.
        Vector3 restR = GoblinParts.PalmWrist + new Vector3(0, 0.03f * br, 0);
        float curlR = 0.22f + 0.04f * br; float stretchR = 1.5f;
        if (sackCeremony)
        {
            if (wv.Stage == "reveal" && wv.CurAt != _lastRevealAt) { _lastRevealAt = wv.CurAt; _handFrom = _wristR; }
            var floor = HallCeremony.I.SackFloor(wv.CurPeer);
            var s = ToLocal(floor);
            var toS = new Vector3(s.X - shR.X, 0f, s.Z - shR.Z); toS = toS.LengthSquared() < 1e-3f ? Vector3.Back : toS.Normalized();
            var hdS = new Vector3(toS.X, -0.12f, toS.Z).Normalized();
            var palmS = s + new Vector3(0, 0.04f, 0);   // the open palm slides under the sack that lies in the cart
            var wristS = palmS - hdS * 1.15f;
            var hdH = new Vector3(0.12f, 0.08f, 1f).Normalized();
            var palmH = HoldPoint;
            var wristH = palmH - hdH * 1.15f;
            Vector3 w0;
            if (wv.Stage == "reveal" && rk1 < 1f) { w0 = _handFrom.Lerp(wristS, Mathf.SmoothStep(0f, 1f, rk1)); _handDirR = _handDirR.Lerp(hdS, Mathf.Min(1f, dt * 8f)); }
            else { w0 = wristS.Lerp(wristH, Mathf.SmoothStep(0f, 1f, rk2)) + new Vector3(0, Mathf.Sin(rk2 * Mathf.Pi) * 0.9f, 0); _handDirR = hdS.Lerp(hdH, Mathf.SmoothStep(0f, 1f, rk2)).Normalized(); }
            if (rk2 >= 1f) w0 += new Vector3(0, 0.05f * Mathf.Sin(_t * 1.8f), 0);
            _wristR = w0; stretchR = 2.5f; curlR = Mathf.Lerp(0.12f, 0.0f, rk2);   // fingers flat: the sack lies on top of the open palm
        }
        else
        {
            _wristR = _wristR.Lerp(restR, Mathf.Min(1f, dt * 2.5f)); _handDirR = _handDirR.Lerp(new Vector3(0, -0.05f, 1f), Mathf.Min(1f, dt * 2.5f));
        }
        SolveArm(0, shR, _wristR, new Vector3(-1f, -0.25f, -0.7f), _handDirR, 0f, stretchR);
        Curl(0, curlR, 0.18f, 0.2f);
        // publish the palm (top surface, centre) so the sack and the number follow the hand
        HallCeremony.PalmWorld = ToGlobal(_wristR + _handDirR.Normalized() * 1.15f + new Vector3(0, 0.26f, 0)); HallCeremony.PalmValid = true;
        // left: resting on his thigh, or reaching/lifting/swinging/releasing the victim
        Vector3 wrist = GoblinParts.RestWristL; Vector3 hdir = new(0, -0.1f, 1f); float roll = Mathf.Pi; float curl = 0.25f;
        if (victim != null)
        {
            var v = ToLocal(victim.GlobalPosition + new Vector3(0, 1.0f, 0));
            var dirv = (v - shL); dirv = dirv.LengthSquared() > 1e-3f ? dirv.Normalized() : Vector3.Forward;
            var grip = v - dirv * 1.2f;
            if (u < Miner.ThrowSwingEnd)
            {
                float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(u / Miner.ThrowReachEnd, 0f, 1f));
                wrist = GoblinParts.RestWristL.Lerp(grip, reach); hdir = dirv; roll = Mathf.Lerp(Mathf.Pi, 0.0f, reach);
                curl = u < Miner.ThrowReachEnd * 0.8f ? 0.15f : 1.15f;
                _gripLast = grip; _releaseT = 0f;
            }
            else
            {
                _releaseT += dt;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_releaseT / 0.9f, 0f, 1f));
                wrist = _gripLast.Lerp(GoblinParts.RestWristL, k); hdir = dirv.Lerp(new Vector3(0, -0.1f, 1f), k); roll = Mathf.Lerp(0f, Mathf.Pi, k);
                curl = Mathf.Lerp(0.2f, 0.25f, k);
            }
            _armBlend = 1f;
        }
        else if (_armBlend > 0f)
        {
            // victim node is gone: glide the hand back home
            _armBlend = Mathf.Max(0f, _armBlend - dt * 1.4f);
            float k = 1f - Mathf.SmoothStep(0f, 1f, _armBlend);
            wrist = _gripLast.Lerp(GoblinParts.RestWristL, k); roll = Mathf.Lerp(0f, Mathf.Pi, k);
        }
        _curlL = Mathf.Lerp(_curlL, curl, Mathf.Min(1f, dt * 9f));
        _wristL = wrist;
        Vector3 poleL = victim != null ? new Vector3(1.0f, 0.5f, -0.2f) : new Vector3(1f, -0.4f, -0.6f);
        SolveArm(1, shL, wrist, poleL, hdir, roll);
        Curl(1, _curlL, _curlL * 0.9f, _curlL * 0.8f);

        // victim spotlight
        _victimLight.Visible = victim != null;
        if (victim != null) _victimLight.GlobalPosition = victim.GlobalPosition + new Vector3(0, 2.2f, 2.0f);

        _faceLight.LightEnergy = 7.0f * (1f + 0.04f * Mathf.Sin(_t * 1.7f) + (_react > 0 && _reactKind == "angry" ? 0.25f * Mathf.Sin(_t * 30f) : 0f));
    }

    /// <summary>Where the sack is held while it is weighed: in front of his chest, in rig space.</summary>
    static readonly Vector3 HoldPoint = new(-1.2f, 3.6f, 5.6f);

    /// <summary>Debug / screenshots: pretend a reaction happened.</summary>
    public void Debug(string kind) => OnReact(kind);
    public void DebugSpeak(float seconds) => _speakT = seconds;
}
