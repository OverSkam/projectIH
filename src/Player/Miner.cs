using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

public struct MinerInput
{
    public Vector2 Move;      // x = right, y = forward
    public bool Jump, Sprint, Crouch, Attack, Interact, InteractHeld, Drop, DropHeld, UseUtility, Ability, SteinCycle, ToggleLantern;
    /// <summary>v2.3: R (shove, just pressed), C (rob a downed miner, just pressed), RMB held (drag a downed miner).</summary>
    public bool Shove, Rob, DragHeld;
    public int UtilSlot;      // -1 none
    public float Yaw, Pitch;  // absolute look angles (radians)
}

public interface IMinerController
{
    MinerInput Poll(float dt, Miner m);
}

/// <summary>Keyboard + mouse.</summary>
public class HumanController : IMinerController
{
    float _yaw, _pitch;
    /// <summary>Radians per mouse pixel; comes from UserSettings (UI settings screen).</summary>
    public float Sensitivity => UserSettings.SensitivityRad;

    /// <summary>Looking is the one thing that stays free during the weighing; the ceremony turns every miner towards the Scale.</summary>
    public void SetLook(float yaw, float pitch) { _yaw = yaw; _pitch = pitch; }

    public void OnInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= mm.Relative.X * Sensitivity;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * Sensitivity, -1.5f, 1.5f);
        }
    }

    public MinerInput Poll(float dt, Miner m)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var i = new MinerInput { Yaw = _yaw, Pitch = _pitch, UtilSlot = -1 };
        if (!captured) return i;
        i.Move = Input.GetVector("move_left", "move_right", "move_back", "move_forward");
        i.Jump = Input.IsActionPressed("jump");
        i.Sprint = Input.IsActionPressed("sprint");
        i.Crouch = Input.IsActionPressed("crouch");
        i.Attack = Input.IsActionPressed("attack");
        i.Interact = Input.IsActionJustPressed("interact");
        i.InteractHeld = Input.IsActionPressed("interact");
        i.ToggleLantern = Input.IsActionJustPressed("toggle_lantern");
        i.Drop = Input.IsActionJustPressed("drop");
        i.DropHeld = Input.IsActionPressed("drop");
        i.UseUtility = Input.IsActionJustPressed("use_utility");
        i.Shove = Input.IsActionJustPressed("shove");
        i.Rob = Input.IsActionJustPressed("rob");
        i.DragHeld = Input.IsActionPressed("use_utility");
        i.Ability = Input.IsActionJustPressed("stein_ability");
        i.SteinCycle = Input.IsActionJustPressed("stein_cycle");
        if (Input.IsActionJustPressed("utility_1")) i.UtilSlot = 0;
        if (Input.IsActionJustPressed("utility_2")) i.UtilSlot = 1;
        return i;
    }
}

/// <summary>First-person miner. Owner peer simulates; everyone else interpolates the transform the owner reports (via the server).</summary>
public partial class Miner : CharacterBody3D
{
    public int PeerId;
    public string PlayerName = "";
    public bool IsLocal;
    public IMinerController Controller;

    // Local-only stats pushed from the server (private OwnState)
    public float SpeedMul = 1f, JumpMul = 1f;
    public bool NoFall;
    public bool HasMask, LanternOut, LanternLow;
    /// <summary>Until this time (seconds) the lantern flame gutters because Wicks are drinking its oil (set by the swarm on every peer).</summary>
    public double GutterUntil;
    float _shake;
    /// <summary>Local camera shake (0..1.5): golem footsteps. Decays by itself.</summary>
    public void AddShake(float amount) { if (IsLocal) _shake = Mathf.Max(_shake, Mathf.Clamp(amount, 0f, 1.5f)); }
    /// <summary>True while the lantern in the hand is burning (the local miner sees only dim blindness shapes otherwise).</summary>
    public bool LightOn => _light != null && IsInstanceValid(_light) && !LanternOut;
    /// <summary>Public: lying downed (crawling, lootable). See Game.Downed.cs.</summary>
    public bool IsDowned { get; private set; }
    /// <summary>Client: the downed miner under the crosshair that E would loot (set by UpdateTarget).</summary>
    public Miner LootTarget;
    /// <summary>Client: 0..1 progress of the hold on G (stuffing the selected stein into the sack).</summary>
    public float HoldProgress;
    /// <summary>Own burden (from the private own state) and sprint stamina 0..1 (local simulation only).</summary>
    public float Burden, Stamina = 1f;
    bool _exhausted; double _noRegenUntil;
    /// <summary>Screenshot staging only.</summary>
    public void DebugHoldProgress(float v) { _dbgHold = v; }
    float _dbgHold;
    public const float HoldStuffSeconds = 1.0f, TapSeconds = 0.35f, DownedSpeedMul = 0.12f;
    public const float SprintSeconds = 12f;   // unburdened: how long a full stamina bar lasts
    float _holdT; bool _holding;
    // thrown by the Goblin: scripted flight (no physics, no input)
    bool _thrown; double _throwT; float _throwLen; Vector3 _throwFrom, _throwPalm, _throwShaft;
    float _lightBase = 1f;
    float _rootLeft, _ropeDetach;
    bool _ethereal;
    Node3D _mask;

    public string PromptText = "";
    /// <summary>Multi-option prompt (downed miner under the crosshair): lines of "KEY\ttext". Empty = use PromptText.</summary>
    public string[] PromptRows = System.Array.Empty<string>();
    /// <summary>Client: another miner in reach to pickpocket (crouched, behind them), the same geometry ignoring the crouch (for the one-time hint), and the downed miner in reach.</summary>
    public Miner PickTarget, DownedTarget; public bool PickReachable;
    /// <summary>Public coarse sack size of this (remote) miner, 0..4.</summary>
    public int SackStep => _rig?.SackStep ?? 0;
    /// <summary>Peer id of the miner dragging this (downed, local) miner, -1 none.</summary>
    public int DraggedBy = -1;
    float _stagger, _shoveFpT = 1f; double _shoveCd;
    int _holdKind, _holdTarget; double _holdStart; bool _holdBlocked;
    public bool IsStaggered => _stagger > 0f;
    public bool IsCrouching => _crouching;
    public bool IsDragging => _holdKind == Game.HoldDrag;
    public void SetSackStep(int step) => _rig?.SetSackStep(step);
    public void SetDragging(bool on) => _rig?.SetDragging(on);
    public void SetDraggedBy(int by) { DraggedBy = by; }
    /// <summary>Bots: mark that a drag is in progress (halves the walking speed like the human hold does).</summary>
    /// <summary>Screenshot staging: pose the shove animation of this (remote) miner / force the prompt rows.</summary>
    public void DebugShovePose(float u) => _rig?.DebugShove(u);
    public string[] DebugRows;
    public void SetBotHold(int kind) { _holdKind = kind; }

    /// <summary>Another miner's shove animation (and sound) on this peer.</summary>
    public void PlayShove()
    {
        if (!IsLocal) _rig?.Shove();
        else _shoveFpT = 0f;
        Sfx.Play(SfxNames.Shove, GlobalPosition + new Vector3(0, 1.2f, 0), IsLocal ? -2f : 0f);
        Sfx.Play(SfxNames.Grunt, GlobalPosition + new Vector3(0, 1.5f, 0), IsLocal ? -6f : -3f);
    }

    /// <summary>The server shoved this (local) miner: an impulse the owner applies at once (movement is owner-simulated, so there is no rubber band), a short stagger, a camera jolt.</summary>
    public void GetShoved(Vector3 vel, float stagger)
    {
        _stagger = stagger;
        Velocity = new Vector3(vel.X, Mathf.Max(Velocity.Y, 0f), vel.Z);
        AddShake(1.0f);
        if (_cam != null)
        {
            float side = vel.X * vel.Z > 0 ? 1f : -1f;
            _cam.Rotation = new Vector3(0.14f, 0, 0.07f * side);
            var tw = CreateTween();
            tw.TweenProperty(_cam, "rotation", Vector3.Zero, 0.4f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
    }
    public NetObject Target;

    Node3D _head, _body, _tool, _leftHand;
    MinerRig _rig;
    Label3D _tag;
    Vector3 _toolRest, _toolBase = new Vector3(0.3f, -0.34f, -0.5f);
    float _retract;
    Vector3 _lastPos; float _vel;
    /// <summary>Raised on the local miner when the server knocks it back (HUD damage direction).</summary>
    public static event Action<Vector3> Knocked;
    Camera3D _cam;
    OmniLight3D _light;
    CollisionShape3D _shape;
    string _held = "";

    float _yaw, _pitch;
    Vector3 _netPos; float _netYaw, _netPitch; int _flags;
    float _fallPeak; bool _wasOnFloor = true;
    double _attackCd, _sendTimer;
    bool _crouching;
    Tween _swing;

    public const float Gravity = 22f, WalkSpeed = 5f, SprintSpeed = 8f, CrouchSpeed = 2.5f, JumpVel = 7f;
    public const float FallSafeSpeed = 12f;

    public override void _Ready()
    {
        CollisionLayer = 2; CollisionMask = 1 | Layers.ThinWall;
        _shape = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.32f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) };
        AddChild(_shape);
        _head = new Node3D { Position = new Vector3(0, 1.6f, 0) };
        AddChild(_head);
        _body = new Node3D();
        AddChild(_body);
        var helmetColor = PlayerColors.Of(PeerId);
        _rig = new MinerRig(helmetColor);
        _body.AddChild(_rig);
        _tool = new Node3D();
        if (IsLocal)
        {
            _tool.Position = new Vector3(0.33f, -0.3f, -0.55f);
            _head.AddChild(_tool);
            HeldVisuals.RightHandFp(_tool);
            _leftHand = HeldVisuals.LeftHandFp();
            _head.AddChild(_leftHand);
        }
        else
        {
            _rig.RightHand.AddChild(_tool);
            _tag = new Label3D
            {
                Text = PlayerName, Position = new Vector3(0, 2.25f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0042f, FontSize = 64,
                OutlineSize = 14, OutlineModulate = new Color(0, 0, 0, 0.9f), Modulate = helmetColor.Lightened(0.35f), NoDepthTest = false, Visible = false,
                Font = UiTheme.Chunky, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
            AddChild(_tag);
        }

        if (IsLocal)
        {
            _body.Visible = false;
            _cam = new Camera3D { Current = true, Fov = UserSettings.Fov, Near = 0.05f };
            _head.AddChild(_cam);
            if (DisplayServer.GetName() != "headless") _cam.AddChild(new LocalAtmosphere { Name = "LocalAtmosphere", Lit = () => LightOn });
            if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        _netPos = Position;
        SetHeld(_held);
    }

    public override void _Input(InputEvent e)
    {
        if (IsLocal && Controller is HumanController hc) hc.OnInput(e);
    }

    // ------------------------------------------------------------------ held item (public information)
    public void SetHeld(string id)
    {
        _held = id ?? "";
        if (_tool == null) return;
        foreach (var c in _tool.GetChildren()) if (c.Name != "Hand") c.QueueFree();
        _light = null;
        if (_mask != null && HasMask) { }
        var def = Utilities.Get(_held);
        _toolRest = Vector3.Zero;
        if (IsLocal)
        {
            // first person: a clean right hand holding only the selected item
            if (def != null)
            {
                _light = HeldVisuals.BuildFp(_tool, def.Id, def.Color, out _toolBase, out _toolRest);
                if (_light != null) { _lightBase = _light.LightEnergy; _light.Visible = !LanternOut; }
            }
            else { _toolBase = new Vector3(0.3f, -0.34f, -0.5f); _toolRest = Vector3.Zero; }
            _swing?.Kill();
            _tool.Position = _toolBase; _tool.Rotation = _toolRest;
            return;
        }
        if (def != null)
        {
            if (def.IsPickaxe)
            {
                HeldVisuals.Pickaxe(_tool, def.Color);
                _toolRest = IsLocal ? new Vector3(0.6f, -0.45f, -0.15f) : new Vector3(Mathf.Pi / 2f, 0, 0);
            }
            else if (def.IsLight)
            {
                _light = HeldVisuals.Lantern(_tool);
                _lightBase = _light.LightEnergy;
                _light.Visible = !LanternOut;
            }
            else
            {
                _tool.AddChild(ItemVisuals.Held(def.Id, def.Color));
            }
        }
        _swing?.Kill();
        _tool.Rotation = _toolRest;
    }

    public void SetMask(bool on)
    {
        HasMask = on;
        _mask?.QueueFree(); _mask = null;
        if (on && _body != null) { _mask = ItemVisuals.Mask(); _body.AddChild(_mask); }
    }

    /// <summary>0 lit, 1 out, 2 lit but flickering (low oil). Public information.</summary>
    public void SetLanternState(int state)
    {
        bool o = state == 1;
        if (_light != null && LanternOut != o) Sfx.Play(o ? SfxNames.LanternOut : SfxNames.LanternOn, GlobalPosition + Vector3.Up);
        LanternOut = o; LanternLow = state == 2;
        if (_light != null) _light.Visible = !o;
    }

    public void SetDowned(bool d)
    {
        if (IsDowned == d) return;
        IsDowned = d;
        _rig?.SetDowned(d);
        if (!IsLocal || _shape == null) return;
        ((CapsuleShape3D)_shape.Shape).Height = d ? 0.7f : (_crouching ? 1.0f : 1.8f);
        _shape.Position = new Vector3(0, d ? 0.35f : (_crouching ? 0.5f : 0.9f), 0);
        _head.Position = new Vector3(0, d ? 0.45f : (_crouching ? 0.95f : 1.6f), 0);
    }

    /// <summary>Weighing ceremony: turn to face a direction (the Scale).</summary>
    public void FaceYaw(float yaw)
    {
        _yaw = yaw; _netYaw = yaw; Rotation = new Vector3(0, yaw, 0);
        (Controller as HumanController)?.SetLook(yaw, 0.05f);
    }

    // ---- the throw: the Goblin's arm (GoblinRig) reaches the victim, lifts them to the hold point, swings them over the shaft and lets go.
    // Both read the same timeline (ThrowU) so the hand and the miner stay in sync on every peer.
    public const float ThrowReachEnd = 0.14f, ThrowLiftEnd = 0.36f, ThrowSwingEnd = 0.52f;
    public bool IsThrown => _thrown;
    /// <summary>0..1 progress of the throw, -1 when not being thrown.</summary>
    public float ThrowU => _thrown ? Mathf.Clamp((float)_throwT / _throwLen, 0f, 1f) : -1f;
    public static Vector3 ThrowHold(Vector3 from, Vector3 shaft) => from.Lerp(shaft, 0.45f) + new Vector3(0, 4.6f, 0);
    public static Vector3 ThrowTop(Vector3 shaft) => shaft + new Vector3(0, 3.4f, 0);

    /// <summary>The Goblin picks this miner up, swings them over the shaft and drops them. Runs on every peer; at the end the node is removed.
    /// (palm is kept for the call contract; the hold point is derived from the victim's spot and the shaft.)</summary>
    public void StartThrow(Vector3 palm, Vector3 shaft, float seconds)
    {
        _thrown = true; _throwT = 0; _throwLen = Math.Max(1f, seconds);
        _throwFrom = GlobalPosition; _throwPalm = ThrowHold(_throwFrom, shaft); _throwShaft = shaft;
        CollisionMask = 0; CollisionLayer = 0; Velocity = Vector3.Zero;
    }

    void ThrowStep(float dt)
    {
        _throwT += dt;
        float u = Mathf.Clamp((float)_throwT / _throwLen, 0f, 1f);
        Vector3 pos; var top = ThrowTop(_throwShaft);
        if (u < ThrowReachEnd) pos = _throwFrom;
        else if (u < ThrowLiftEnd) pos = _throwFrom.Lerp(_throwPalm, Mathf.SmoothStep(0f, 1f, (u - ThrowReachEnd) / (ThrowLiftEnd - ThrowReachEnd)));
        else if (u < ThrowSwingEnd) { float k = Mathf.SmoothStep(0f, 1f, (u - ThrowLiftEnd) / (ThrowSwingEnd - ThrowLiftEnd)); pos = _throwPalm.Lerp(top, k) + new Vector3(0, Mathf.Sin(k * Mathf.Pi) * 0.9f, 0); }
        else
        {
            float f = (u - ThrowSwingEnd) / (1f - ThrowSwingEnd);
            pos = top + new Vector3(0, -70f * f * f, 0);
        }
        GlobalPosition = pos;
        if (u < ThrowSwingEnd)   // dangling in the fist
        {
            float k = Mathf.Clamp((u - ThrowReachEnd) / (ThrowSwingEnd - ThrowReachEnd), 0f, 1f);
            Rotation = new Vector3(0.35f * k + Mathf.Sin((float)_throwT * 5f) * 0.12f * k, Rotation.Y, Mathf.Sin((float)_throwT * 3.3f) * 0.2f * k);
        }
        else
        {
            float spin = (u - ThrowSwingEnd) * 40f;
            Rotation = new Vector3(0.35f + Mathf.Sin(spin * 0.9f) * 0.9f, Rotation.Y, spin * 0.6f);
        }
        if (u >= 1f)
        {
            _thrown = false;
            Game.I.NotifyThrownDone(PeerId);
            QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        if (IsLocal && _cam != null && GodotObject.IsInstanceValid(_cam))
        {
            if (_shake > 0.001f)
            {
                _shake = Mathf.MoveToward(_shake, 0f, (float)delta * 2.6f);
                float k = _shake * 0.05f; ulong tk = Time.GetTicksMsec();
                _cam.Position = new Vector3(Mathf.Sin(tk * 0.061f) * k, Mathf.Sin(tk * 0.083f + 1f) * k * 1.4f, 0);
            }
            else if (_cam.Position != Vector3.Zero) _cam.Position = Vector3.Zero;
        }
        // low oil: the light stutters (public: everyone sees a miner's light flicker)
        if (_light == null) return;
        if (LanternOut) { _light.Visible = false; return; }
        double t = Time.GetTicksMsec() / 1000.0;
        if (t < GutterUntil)
        {
            // Wicks are drinking the oil: the flame gutters hard (dips low, nearly dies, flares back)
            float g = (float)(0.5 + 0.5 * Math.Sin(t * 31.0) * Math.Sin(t * 9.1 + PeerId));
            _light.Visible = true;
            _light.LightEnergy = _lightBase * (0.12f + 0.55f * g * g);
            return;
        }
        if (!LanternLow) { _light.Visible = true; _light.LightEnergy = _lightBase; return; }
        float f = (float)(Math.Sin(t * 23.0) * Math.Sin(t * 7.3 + PeerId));
        _light.Visible = f > -0.55f;
        _light.LightEnergy = _lightBase * (0.55f + 0.4f * (float)Math.Abs(Math.Sin(t * 13.0)));
    }

    public float RootLeft => _rootLeft;
    public void Root(float seconds) { _rootLeft = seconds; }

    public void Teleport(Vector3 pos, float yaw) { GlobalPosition = pos; Velocity = Vector3.Zero; }

    bool InsideThinWall()
    {
        var q = Queries.Shape(_shape.Shape, _shape.GlobalTransform, Layers.ThinWall);
        return GetWorld3D().DirectSpaceState.IntersectShape(q, 1).Count > 0;
    }

    void UpdateEthereal()
    {
        bool want = Game.I.OwnEtherealUntil > Time.GetTicksMsec() / 1000.0;
        if (!want && _ethereal && InsideThinWall()) want = true; // never get stuck inside a thin wall
        if (want == _ethereal) return;
        _ethereal = want;
        CollisionMask = want ? 1u : (1u | Layers.ThinWall);
    }

    RopeObj FindRope()
    {
        var w = Game.I.World; if (w == null) return null;
        foreach (var o in w.Objects.Values) if (o is RopeObj r && r.Contains(GlobalPosition)) return r;
        return null;
    }

    public void PlaySwing()
    {
        if (_tool == null) return;
        _rig?.Swing();
        AudioDirector.OnSwing(this);
        if (!IsLocal) return;
        _swing?.Kill();
        _swing = CreateTween();
        bool pick = Utilities.Get(_held)?.IsPickaxe == true;
        if (pick)
        {
            _tool.Rotation = _toolRest + new Vector3(1.0f, 0, 0);
            _swing.TweenProperty(_tool, "rotation", _toolRest + new Vector3(-0.9f, 0.1f, 0), 0.13f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            _swing.TweenProperty(_tool, "rotation", _toolRest, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
        else
        {
            _tool.Rotation = _toolRest + new Vector3(-0.6f, 0, 0);
            _swing.TweenProperty(_tool, "rotation", _toolRest, 0.3f);
        }
    }

    public void ApplyKnockback(Vector3 v) { Velocity += v; Knocked?.Invoke(v); }

    // ------------------------------------------------------------------ remote state
    public void ApplyNetState(Vector3 pos, float yaw, float pitch, int flags)
    {
        _netPos = pos; _netYaw = yaw; _netPitch = pitch; _flags = flags;
    }

    public int NetFlags => _flags;
    public string Held => _held;
    public Vector3 EyePos => GlobalPosition + new Vector3(0, _crouching ? 0.95f : 1.6f, 0);
    public Vector3 LookDir => -_head.GlobalTransform.Basis.Z;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_thrown) { ThrowStep(dt); return; }
        if (!IsLocal)
        {
            GlobalPosition = GlobalPosition.Lerp(_netPos, Mathf.Min(1f, dt * 15f));
            _yaw = Mathf.LerpAngle(_yaw, _netYaw, Mathf.Min(1f, dt * 15f));
            _pitch = Mathf.Lerp(_pitch, _netPitch, Mathf.Min(1f, dt * 15f));
            Rotation = new Vector3(0, _yaw, 0);
            _head.Rotation = new Vector3(_pitch, 0, 0);
            AnimateRemote(dt);
            return;
        }
        if (Controller == null) return;
        if (_cam != null) _cam.Fov = UserSettings.Fov;
        AnimateLocalHands(dt);
        var c = Controller.Poll(dt, this);
        _yaw = c.Yaw; _pitch = c.Pitch;
        // weighing: input is locked except looking; downed: crawl only
        bool weighing = Game.I.Phase == GamePhase.Weighing;
        bool staggered = _stagger > 0f;
        _stagger -= dt;
        if (weighing || IsDowned || staggered)
        {
            if (weighing || staggered) c.Move = Vector2.Zero;
            c.Jump = c.Sprint = c.Crouch = c.Attack = c.Interact = c.InteractHeld = c.Drop = c.DropHeld = c.UseUtility = c.Ability = c.SteinCycle = c.ToggleLantern = false;
            c.UtilSlot = -1;
        }
        Rotation = new Vector3(0, _yaw, 0);
        _head.Rotation = new Vector3(_pitch, 0, 0);

        // crouch
        bool wantCrouch = c.Crouch && !IsDowned;
        if (wantCrouch != _crouching)
        {
            _crouching = wantCrouch;
            ((CapsuleShape3D)_shape.Shape).Height = _crouching ? 1.0f : 1.8f;
            _shape.Position = new Vector3(0, _crouching ? 0.5f : 0.9f, 0);
            _head.Position = new Vector3(0, _crouching ? 0.95f : 1.6f, 0);
        }

        // sprint stamina: a full bar lasts 12 s unburdened and drains faster the heavier the sack; empty = no sprint until 30% is back
        bool wantSprint = c.Sprint && !_crouching && c.Move.LengthSquared() > 0.01f && !IsDowned;
        bool sprint = wantSprint && !_exhausted && Stamina > 0f;
        if (sprint)
        {
            Stamina = Mathf.Max(0f, Stamina - dt / SprintSeconds * AppStein.Burden.SprintDrainMul(Burden));
            _noRegenUntil = Time.GetTicksMsec() / 1000.0 + 0.6;
            if (Stamina <= 0f) _exhausted = true;
        }
        else if (Time.GetTicksMsec() / 1000.0 > _noRegenUntil)
        {
            Stamina = Mathf.Min(1f, Stamina + dt / 6f);
            if (_exhausted && Stamina >= 0.3f) _exhausted = false;
        }
        c.Sprint = sprint;
        float speed = (_crouching ? CrouchSpeed : (sprint ? SprintSpeed : WalkSpeed)) * SpeedMul * AppStein.Burden.SpeedMul(Burden) * (IsDowned ? DownedSpeedMul : 1f);
        if (_holdKind == Game.HoldDrag) speed *= SocialTuning.DragSpeedMul;
        if (Game.I.World?.Cave != null && Game.I.World.Cave.InSqueeze(GlobalPosition)) speed *= 0.55f;   // sideways squeeze: slow going
        var wish = Transform.Basis * new Vector3(c.Move.X, 0, -c.Move.Y);
        wish = wish.LengthSquared() > 1f ? wish.Normalized() : wish;
        UpdateEthereal();
        var v = Velocity;
        bool onFloor = IsOnFloor();
        _rootLeft -= dt; _ropeDetach -= dt;
        var rope = _ropeDetach > 0 ? null : FindRope();
        float accel = staggered ? SocialTuning.StaggerDecel : (onFloor ? 40f : 8f);
        var hv = new Vector3(v.X, 0, v.Z).MoveToward(wish * speed, accel * dt);
        v.X = hv.X; v.Z = hv.Z;
        if (_rootLeft > 0) { v.X = 0; v.Z = 0; }
        if (IsDowned && DraggedBy >= 0 && Game.I.World?.GetMiner(DraggedBy) is Miner drg)
        {
            // dragged: tethered to the dragger, pulled along behind them (collision still applies, so a pit edge or a monster is where the dragger takes you)
            var to = new Vector3(drg.GlobalPosition.X - GlobalPosition.X, 0, drg.GlobalPosition.Z - GlobalPosition.Z);
            float dist = to.Length();
            if (dist > SocialTuning.DragTether)
            {
                var pull = to / dist * Mathf.Min(3.2f, 0.8f + (dist - SocialTuning.DragTether) * 6f);
                v.X = pull.X; v.Z = pull.Z;
            }
        }
        if (rope != null)
        {
            if (c.Jump) { _ropeDetach = 0.5f; v.Y = JumpVel * JumpMul; }
            else
            {
                if (c.Move.Y > 0.1f) v.Y = GlobalPosition.Y > rope.TopY + 0.3f ? 0f : 3.5f;
                else if (c.Move.Y < -0.1f || c.Crouch) v.Y = -3f;
                else v.Y = 0f;
                var to = rope.GlobalPosition - GlobalPosition;
                v.X = Mathf.Clamp(to.X * 6f, -3f, 3f); v.Z = Mathf.Clamp(to.Z * 6f, -3f, 3f);
            }
            _fallPeak = 0;
        }
        else
        {
            if (!onFloor) v.Y -= Gravity * dt; else if (v.Y < 0) v.Y = 0;
            if (c.Jump && onFloor && !_crouching && _rootLeft <= 0) v.Y = JumpVel * JumpMul;
        }
        if (!onFloor) _fallPeak = Math.Max(_fallPeak, -v.Y);
        Velocity = v;
        MoveAndSlide();
        bool nowFloor = IsOnFloor();
        if (nowFloor && !_wasOnFloor)
        {
            if (_fallPeak > FallSafeSpeed && !NoFall) Game.I.ReportFall(_fallPeak);
            _fallPeak = 0;
        }
        if (nowFloor) _fallPeak = 0;
        _wasOnFloor = nowFloor;

        // death plane safety
        if (GlobalPosition.Y < -60f) GlobalPosition = new Vector3(0, 1, 6);

        // actions
        _attackCd -= dt;
        if (c.Attack && _attackCd <= 0)
        {
            bool pick = Utilities.Get(_held)?.IsPickaxe == true;
            _attackCd = pick ? 0.85 : 0.45;
            PlaySwing();
            Game.I.SendAttack(EyePos, LookDir);
        }
        _shoveCd -= dt;
        if (c.Shove && _shoveCd <= 0 && Stamina > 0.03f && !_thrown)
        {
            _shoveCd = SocialTuning.ShoveCooldown;
            Stamina = Mathf.Max(0f, Stamina - SocialTuning.ShoveStamina);
            _noRegenUntil = Time.GetTicksMsec() / 1000.0 + 0.6;
            PlayShove();                       // predicted: the shover never waits for the server to see their own push
            Game.I.SendShove();
        }
        if (c.UtilSlot >= 0) Game.I.SendSelectUtil(c.UtilSlot);
        if (c.UseUtility && !(Controller is HumanController && (DownedTarget != null || _holdKind != 0))) Game.I.SendUseUtility();   // RMB on a downed miner drags instead
        if (c.ToggleLantern) Game.I.SendToggleLantern();
        if (c.Ability) Game.I.SendAbility();
        if (c.SteinCycle) Game.I.SendSteinSel((Game.I.OwnInv.SteinSel + 1) % Inventory.SteinSlots);
        HandleDrop(c, dt);

        if (!(Controller is BotBrain)) UpdateTarget();
        HandleInteract(c);

        // state report ~20 Hz
        _sendTimer -= dt;
        if (_sendTimer <= 0)
        {
            _sendTimer = 0.05;
            int flags = (_crouching ? 1 : 0) | (c.Sprint ? 2 : 0) | (IsOnFloor() ? 4 : 0);
            Game.I.SendState(GlobalPosition, _yaw, _pitch, flags);
        }
    }

    // ------------------------------------------------------------------ visuals: remote miners and first-person hands
    const float TagRange = 16f;

    void AnimateRemote(float dt)
    {
        var d = GlobalPosition - _lastPos; _lastPos = GlobalPosition;
        float spd = dt > 0 ? new Vector2(d.X, d.Z).Length() / dt : 0f;
        _vel = Mathf.Lerp(_vel, Mathf.Min(spd, 10f), Mathf.Min(1f, dt * 12f));
        _rig.Animate(dt, IsDowned ? 0f : (_vel < 0.15f ? 0f : _vel), (_flags & 1) != 0, (_flags & 2) != 0, (_flags & 4) == 0);
        if (_tool != null) _tool.Rotation = _toolRest;
        UpdateTag();
    }

    /// <summary>Name tag only at short range and only when there is a clear line from the local camera (hidden through walls).</summary>
    void UpdateTag()
    {
        if (_tag == null) return;
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) { _tag.Visible = false; return; }
        var from = cam.GlobalPosition; var to = GlobalPosition + new Vector3(0, 1.6f, 0);
        float dist = from.DistanceTo(to);
        if (dist > TagRange) { _tag.Visible = false; return; }
        var q = Queries.Ray(from, to, 1 | 4);
        q.CollideWithAreas = false;
        bool blocked = GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0;
        _tag.Visible = !blocked;
        _tag.Modulate = _tag.Modulate with { A = Mathf.Clamp((TagRange - dist) / 5f, 0.25f, 1f) };
    }

    double _handT;
    void AnimateLocalHands(float dt)
    {
        _handT += dt;
        float move = new Vector2(Velocity.X, Velocity.Z).Length();
        float bob = IsOnFloor() ? Mathf.Sin((float)_handT * 9f) * 0.012f * Mathf.Min(1f, move / 4f) : 0f;
        float sk = 0f;
        if (_shoveFpT < 1f) { _shoveFpT = Mathf.Min(1f, _shoveFpT + dt / 0.45f); sk = Mathf.Sin(Mathf.Pi * Mathf.Min(1f, _shoveFpT * 1.25f)) * (_shoveFpT < 0.8f ? 1f : (1f - _shoveFpT) / 0.2f); }
        if (_leftHand != null) _leftHand.Position = new Vector3(-0.3f + 0.2f * sk, -0.38f + bob + 0.12f * sk, -0.5f + _retract - 0.4f * sk);
        // pull the viewmodel back towards the camera when a wall is close, so it never pokes through
        float want = 0f;
        if (_cam != null)
        {
            var from = _cam.GlobalPosition; var dir = -_cam.GlobalTransform.Basis.Z;
            var q = Queries.Ray(from, from + dir * 1.0f, 1 | 4);
            q.CollideWithAreas = false; q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
            if (hit.Count > 0) want = Mathf.Clamp(0.95f - from.DistanceTo(hit["position"].AsVector3()), 0f, 0.5f);
        }
        _retract = Mathf.Lerp(_retract, want, Mathf.Min(1f, dt * 14f));
        if (_tool != null && (_swing == null || !_swing.IsRunning()))
        {
            bool lantern = Utilities.Get(_held)?.IsLight == true;
            _tool.Position = _toolBase + new Vector3(0.05f * _retract - 0.2f * sk, -0.22f * _retract + bob + 0.12f * sk, _retract - 0.4f * sk);
            _tool.Rotation = _toolRest + (lantern ? new Vector3(Mathf.Sin((float)_handT * 1.7f) * 0.05f + move * 0.006f, 0, Mathf.Sin((float)_handT * 1.3f) * 0.04f) : Vector3.Zero);
        }
    }

    /// <summary>E interacts at once with whatever is under the crosshair (taking a stone is a deliberate press; a bag is poured into the sack).
    /// Other miners: hold E on a downed miner to help them up (3 s), C robs one stone, hold RMB drags them; crouched behind a standing miner, hold E picks their sack (1.6 s).</summary>
    void HandleInteract(MinerInput c)
    {
        var g = Game.I;
        if (!(Controller is BotBrain) && Loot2Client.Update(Target, c)) return;   // bright chests: hold E for 2.5 s (src/Loot2)
        double now = Time.GetTicksMsec() / 1000.0;
        if (!(Controller is BotBrain))
        {
            int want = 0, tgt = 0;
            if (DownedTarget != null && IsInstanceValid(DownedTarget)) { tgt = DownedTarget.PeerId; if (c.DragHeld) want = Game.HoldDrag; else if (c.InteractHeld) want = Game.HoldRevive; }
            else if (PickTarget != null && IsInstanceValid(PickTarget) && c.InteractHeld) { want = Game.HoldPick; tgt = PickTarget.PeerId; }
            if (g.HoldEndedAt > _holdStart && _holdKind != 0) { _holdBlocked = true; _holdKind = 0; }   // the server ended it: release the key before trying again
            if (!c.InteractHeld && !c.DragHeld) _holdBlocked = false;
            if (_holdBlocked) want = 0;
            if (want != _holdKind || (want != 0 && tgt != _holdTarget))
            {
                if (_holdKind != 0) g.SendHold(_holdKind, _holdTarget, false);
                _holdKind = want; _holdTarget = tgt; _holdStart = now;
                if (want != 0) g.SendHold(want, tgt, true);
            }
            float dur = _holdKind == Game.HoldPick ? SocialTuning.PickSeconds : _holdKind == Game.HoldRevive ? SocialTuning.ReviveSeconds : 0f;
            if (dur > 0f) HoldProgress = Mathf.Clamp((float)((now - _holdStart) / dur), 0f, 1f);
            if (c.Rob && DownedTarget != null && IsInstanceValid(DownedTarget)) g.SendLoot(DownedTarget.PeerId);
        }
        if (!c.Interact || _holdKind != 0) return;
        if (Target is GlowChestObj) return;
        if (Target != null) g.SendInteract(Target.Id);
    }

    /// <summary>G: a tap drops the selected utility; holding it for a second stuffs the selected power stein into the sack (anywhere). Stones can never be dropped.</summary>
    void HandleDrop(MinerInput c, float dt)
    {
        var g = Game.I;
        int sel = Mathf.Clamp(g.OwnInv.SteinSel, 0, Math.Max(0, g.OwnInv.Steins.Count - 1));
        if (c.Drop)
        {
            if (g.OwnInv.Steins.Count == 0) g.SendDrop(g.OwnInv.Selected);
            else { _holding = true; _holdT = 0; }
        }
        if (_holding)
        {
            if (c.DropHeld)
            {
                _holdT += dt;
                if (_holdT >= HoldStuffSeconds) { _holding = false; _holdT = 0; g.SendStuffStein(sel); }
            }
            else
            {
                if (_holdT < TapSeconds) g.SendDrop(g.OwnInv.Selected);
                _holding = false; _holdT = 0;
            }
        }
        HoldProgress = _dbgHold > 0f ? _dbgHold : (_holding && _holdT > TapSeconds ? Mathf.Clamp(_holdT / HoldStuffSeconds, 0f, 1f) : 0f);
    }

    void UpdateTarget()
    {
        Target = null; PromptText = ""; LootTarget = null; DownedTarget = null; PickTarget = null; PickReachable = false; PromptRows = System.Array.Empty<string>();
        var g = Game.I;
        if (g.Phase == GamePhase.Round && !IsDowned && g.World != null)
        {
            // pickpocket: right behind a standing miner (120 degree arc behind their facing, 1.3 m), looking at them; crouching is what makes it possible
            var look = new Vector3(LookDir.X, 0, LookDir.Z);
            look = look.LengthSquared() > 0.01f ? look.Normalized() : look;
            foreach (var o in g.World.Miners())
            {
                if (o == this || o.IsDowned || !g.Alive.GetValueOrDefault(o.PeerId)) continue;
                var d = new Vector3(o.GlobalPosition.X - GlobalPosition.X, 0, o.GlobalPosition.Z - GlobalPosition.Z);
                float dist = d.Length();
                if (dist > SocialTuning.PickRange || dist < 0.05f) continue;
                if (look.Dot(d / dist) < 0.35f || !SocialTuning.IsBehind(o.GlobalPosition, o.Rotation.Y, GlobalPosition)) continue;
                PickReachable = true;
                if (_crouching) PickTarget = o;
                break;
            }
        }
        if (DebugRows != null) { PromptRows = DebugRows; return; }
        var space = GetWorld3D().DirectSpaceState;
        var q = Queries.Ray(EyePos, EyePos + LookDir * 3.2f, 1 | 2 | 4 | 8);
        q.CollideWithAreas = false;
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var r = space.IntersectRay(q);
        if (r.Count > 0 && r["collider"].As<GodotObject>() is Miner dm && dm != this && dm.IsDowned && !IsDowned && g.Phase == GamePhase.Round
            && new Vector2(dm.GlobalPosition.X - GlobalPosition.X, dm.GlobalPosition.Z - GlobalPosition.Z).Length() <= SocialTuning.ReviveRange)
        {
            DownedTarget = dm; LootTarget = dm; PickTarget = null;
            PromptRows = new[] { "Hold E\tHelp " + dm.PlayerName + " up", "C\tRob " + dm.PlayerName, "Hold RMB\tDrag" };
            return;
        }
        if (PickTarget != null)
        {
            PromptRows = new[] { "Hold E\t— pick the sack" };
            return;
        }
        if (r.Count == 0) return;
        var hitObj = r["collider"].As<GodotObject>();
        if (IsDowned || g.Phase != GamePhase.Round) return;
        if (hitObj is NetObject o2)
        {
            string p = o2.Prompt(g.OwnInv);
            if (p == null) return;
            PromptText = p;
            if (p.StartsWith("E")) Target = o2;
        }
    }
}
