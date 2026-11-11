using System;
using Godot;

namespace AppStein;

public struct MinerInput
{
    public Vector2 Move;      // x = right, y = forward
    public bool Jump, Sprint, Crouch, Attack, Interact, Drop, DropOre;
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
    public float Sensitivity = 0.0025f;

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

    public string PromptText = "";
    public NetObject Target;

    Node3D _head, _body, _tool;
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
        CollisionLayer = 2; CollisionMask = 1;
        _shape = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) };
        AddChild(_shape);
        _head = new Node3D { Position = new Vector3(0, 1.6f, 0) };
        AddChild(_head);
        _tool = new Node3D { Position = new Vector3(0.35f, -0.3f, -0.55f) };
        _head.AddChild(_tool);

        _body = new Node3D();
        AddChild(_body);
        var body = Util.CapsuleMesh(0.35f, 1.4f, new Color(0.7f, 0.3f, 0.2f)); body.Position = new Vector3(0, 0.8f, 0); _body.AddChild(body);
        var head = Util.SphereMesh(0.22f, new Color(0.9f, 0.7f, 0.55f)); head.Position = new Vector3(0, 1.62f, 0); _body.AddChild(head);
        var helmet = Util.BoxMesh(new Vector3(0.5f, 0.18f, 0.5f), new Color(0.9f, 0.8f, 0.1f)); helmet.Position = new Vector3(0, 1.82f, 0); _body.AddChild(helmet);
        if (!IsLocal)
        {
            var label = new Label3D { Text = PlayerName, Position = new Vector3(0, 2.3f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.006f, NoDepthTest = false, Modulate = new Color(1, 1, 1, 0.8f) };
            AddChild(label);
        }

        if (IsLocal)
        {
            _body.Visible = false;
            _cam = new Camera3D { Current = true, Fov = 80f, Near = 0.05f };
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
        foreach (var c in _tool.GetChildren()) c.QueueFree();
        _light = null;
        var def = Utilities.Get(_held);
        if (def == null) return;
        if (def.IsPickaxe)
        {
            var handle = Util.BoxMesh(new Vector3(0.06f, 0.06f, 0.7f), def.Color); _tool.AddChild(handle);
            var h = Util.BoxMesh(new Vector3(0.5f, 0.08f, 0.08f), new Color(0.6f, 0.6f, 0.65f)); h.Position = new Vector3(0, 0, -0.35f); _tool.AddChild(h);
        }
        else if (def.IsLight)
        {
            var box = Util.BoxMesh(new Vector3(0.15f, 0.2f, 0.15f), def.Color, 4f); _tool.AddChild(box);
            _light = new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 2.2f, OmniRange = 14f, OmniAttenuation = 1.2f, ShadowEnabled = false, Position = new Vector3(0, 0.3f, 0) };
            _tool.AddChild(_light);
        }
        else
        {
            _tool.AddChild(Util.BoxMesh(new Vector3(0.2f, 0.2f, 0.3f), def.Color));
        }
    }

    public void PlaySwing()
    {
        _swing?.Kill();
        _swing = CreateTween();
        _tool.Rotation = new Vector3(-0.9f, 0, 0);
        _swing.TweenProperty(_tool, "rotation", Vector3.Zero, 0.3f);
    }

    public void ApplyKnockback(Vector3 v) { Velocity += v; }

    // ------------------------------------------------------------------ remote state
    public void ApplyNetState(Vector3 pos, float yaw, float pitch, int flags)
    {
        _netPos = pos; _netYaw = yaw; _netPitch = pitch; _flags = flags;
    }

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
            return;
        }
        if (Controller == null) return;
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
        var v = Velocity;
        bool onFloor = IsOnFloor();
        float accel = onFloor ? 40f : 8f;
        var hv = new Vector3(v.X, 0, v.Z).MoveToward(wish * speed, accel * dt);
        v.X = hv.X; v.Z = hv.Z;
        if (!onFloor) v.Y -= Gravity * dt; else if (v.Y < 0) v.Y = 0;
        if (c.Jump && onFloor && !_crouching) v.Y = JumpVel * JumpMul;
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
