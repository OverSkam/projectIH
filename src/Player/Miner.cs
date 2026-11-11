using System;
using Godot;

namespace AppStein;

public struct MinerInput
{
    public Vector2 Move;      // x = right, y = forward
    public bool Jump, Sprint, Crouch, Attack, Interact, Drop, DropOre, UseUtility, Ability, SteinCycle;
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
        i.Drop = Input.IsActionJustPressed("drop");
        i.DropOre = Input.IsActionJustPressed("drop_ore");
        i.UseUtility = Input.IsActionJustPressed("use_utility");
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
    public bool HasMask, LanternOut;
    float _rootLeft, _ropeDetach;
    bool _ethereal;
    Node3D _mask;

    public string PromptText = "";
    public NetObject Target;

    Node3D _head, _body, _tool, _leftHand;
    MinerRig _rig;
    Label3D _tag;
    Vector3 _toolRest;
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
        _shape = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) };
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

    public void SetLanternOut(bool o) { if (_light != null && LanternOut != o) Sfx.Play(o ? SfxNames.LanternOut : SfxNames.LanternOn, GlobalPosition + Vector3.Up); LanternOut = o; if (_light != null) _light.Visible = !o; }

    public float RootLeft => _rootLeft;
    public void Root(float seconds) { _rootLeft = seconds; }

    public void Teleport(Vector3 pos, float yaw) { GlobalPosition = pos; Velocity = Vector3.Zero; }

    bool InsideThinWall()
    {
        var q = new PhysicsShapeQueryParameters3D { Shape = _shape.Shape, Transform = _shape.GlobalTransform, CollisionMask = Layers.ThinWall };
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
    public Vector3 EyePos => GlobalPosition + new Vector3(0, _crouching ? 1.1f : 1.6f, 0);
    public Vector3 LookDir => -_head.GlobalTransform.Basis.Z;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
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
        Rotation = new Vector3(0, _yaw, 0);
        _head.Rotation = new Vector3(_pitch, 0, 0);

        // crouch
        bool wantCrouch = c.Crouch;
        if (wantCrouch != _crouching)
        {
            _crouching = wantCrouch;
            ((CapsuleShape3D)_shape.Shape).Height = _crouching ? 1.2f : 1.8f;
            _shape.Position = new Vector3(0, _crouching ? 0.6f : 0.9f, 0);
            _head.Position = new Vector3(0, _crouching ? 1.1f : 1.6f, 0);
        }

        float speed = (_crouching ? CrouchSpeed : (c.Sprint ? SprintSpeed : WalkSpeed)) * SpeedMul;
        var wish = Transform.Basis * new Vector3(c.Move.X, 0, -c.Move.Y);
        wish = wish.LengthSquared() > 1f ? wish.Normalized() : wish;
        UpdateEthereal();
        var v = Velocity;
        bool onFloor = IsOnFloor();
        _rootLeft -= dt; _ropeDetach -= dt;
        var rope = _ropeDetach > 0 ? null : FindRope();
        float accel = onFloor ? 40f : 8f;
        var hv = new Vector3(v.X, 0, v.Z).MoveToward(wish * speed, accel * dt);
        v.X = hv.X; v.Z = hv.Z;
        if (_rootLeft > 0) { v.X = 0; v.Z = 0; }
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
        if (c.UtilSlot >= 0) Game.I.SendSelectUtil(c.UtilSlot);
        if (c.UseUtility) Game.I.SendUseUtility();
        if (c.Ability) Game.I.SendAbility();
        if (c.SteinCycle) Game.I.SendSteinSel((Game.I.OwnInv.SteinSel + 1) % Inventory.SteinSlots);
        if (c.Drop) Game.I.SendDrop("util", Game.I.OwnInv.Selected);
        if (c.DropOre) Game.I.SendDrop("ore", Game.I.OwnInv.Sack.Count - 1);

        if (!(Controller is BotBrain)) UpdateTarget();
        if (c.Interact && Target != null) Game.I.SendInteract(Target.Id);

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
        _rig.Animate(dt, _vel < 0.15f ? 0f : _vel, (_flags & 1) != 0, (_flags & 2) != 0, (_flags & 4) == 0);
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
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1 | 4);
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
        if (_leftHand != null) _leftHand.Position = new Vector3(-0.3f, -0.32f + bob, -0.5f);
        if (_tool != null && (_swing == null || !_swing.IsRunning()))
        {
            bool lantern = Utilities.Get(_held)?.IsLight == true;
            _tool.Position = lantern ? new Vector3(0.3f, -0.12f + bob, -0.6f) : new Vector3(0.33f, -0.3f + bob, -0.55f);
            _tool.Rotation = _toolRest + (lantern ? new Vector3(Mathf.Sin((float)_handT * 1.7f) * 0.05f + move * 0.006f, 0, Mathf.Sin((float)_handT * 1.3f) * 0.04f) : Vector3.Zero);
        }
    }

    void UpdateTarget()
    {
        Target = null; PromptText = "";
        var space = GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(EyePos, EyePos + LookDir * 3.2f, 1 | 4 | 8);
        q.CollideWithAreas = false;
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var r = space.IntersectRay(q);
        if (r.Count == 0) return;
        if (r["collider"].As<GodotObject>() is NetObject o)
        {
            string p = o.Prompt(Game.I.OwnInv);
            if (p == null) return;
            PromptText = p;
            if (p.StartsWith("E")) Target = o;
        }
    }
}
