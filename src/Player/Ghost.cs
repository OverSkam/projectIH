using Godot;

namespace AppStein;

/// <summary>Free-fly spectator used by dead players and mid-round joiners. Purely local (no network presence).</summary>
public partial class Ghost : Node3D
{
    Camera3D _cam;
    float _yaw, _pitch;
    double _meddleCd;

    public override void _Ready()
    {
        _cam = new Camera3D { Current = true, Fov = 80f };
        AddChild(_cam);
        // A faint light so the ghost is not completely blind.
        AddChild(new OmniLight3D { LightColor = new Color(0.5f, 0.6f, 1f), LightEnergy = 0.35f, OmniRange = 9f });
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= mm.Relative.X * 0.0025f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.0025f, -1.5f, 1.5f);
        }
    }

    public override void _Process(double delta)
    {
        Rotation = new Vector3(_pitch, _yaw, 0);
        if (Input.MouseMode != Input.MouseModeEnum.Captured) return;
        var v = Input.GetVector("move_left", "move_right", "move_back", "move_forward");
        float up = (Input.IsActionPressed("jump") ? 1 : 0) - (Input.IsActionPressed("crouch") ? 1 : 0);
        float speed = Input.IsActionPressed("sprint") ? 16f : 8f;
        var dir = GlobalTransform.Basis * new Vector3(v.X, 0, -v.Y) + Vector3.Up * up;
        GlobalPosition += dir * speed * (float)delta;
        _meddleCd -= delta;
        if (Input.IsActionJustPressed("use_utility") && _meddleCd <= 0) { _meddleCd = 1; Game.I.SendMeddle(); }
    }
}
