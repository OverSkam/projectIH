using Godot;

namespace AppStein;

/// <summary>
/// PLACEHOLDER visuals of the giant Scale in the Hall (a readable low-poly weighing platform with a big number) and of the shaft the thrown miner falls into.
/// Built on every peer from CaveInfo.ScalePos / ShaftPos; driven by Game.Weigh (the replicated ceremony state). The next step replaces the look
/// (keep the contract: <see cref="Show"/> a name and a weight, <see cref="Idle"/> when nothing is on the Scale).
/// </summary>
public partial class HallScale : Node3D
{
    Node3D _pan;
    Label3D _num, _name;
    OmniLight3D _light;
    float _panY = 0.95f;
    string _lastName = "";

    public override void _Ready()
    {
        var cave = Game.I.World.Cave;
        Position = cave.ScalePos;
        var stone = new Color(0.30f, 0.27f, 0.25f);
        var brass = new Color(0.75f, 0.55f, 0.2f);

        var baseDisc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 2.6f, BottomRadius = 2.8f, Height = 0.3f, RadialSegments = 12, Rings = 1 }, MaterialOverride = Util.Mat(stone) };
        baseDisc.Position = new Vector3(0, 0.15f, 0);
        AddChild(baseDisc);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2f + Mathf.Pi / 4f;
            var post = Util.BoxMesh(new Vector3(0.25f, 0.8f, 0.25f), brass.Darkened(0.3f));
            post.Position = new Vector3(Mathf.Cos(a) * 1.7f, 0.7f, Mathf.Sin(a) * 1.7f);
            AddChild(post);
        }
        _pan = new Node3D { Position = new Vector3(0, _panY, 0) };
        AddChild(_pan);
        var plate = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 2.0f, BottomRadius = 2.0f, Height = 0.16f, RadialSegments = 12, Rings = 1 }, MaterialOverride = Util.Mat(brass, 0.25f) };
        _pan.AddChild(plate);
        var rim = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 2.1f, BottomRadius = 2.1f, Height = 0.06f, RadialSegments = 12, Rings = 1 }, MaterialOverride = Util.Mat(new Color(1f, 0.85f, 0.4f), 0.6f) };
        rim.Position = new Vector3(0, 0.08f, 0);
        _pan.AddChild(rim);

        // big number floating above the pan (the number is only shown while a pile is being revealed)
        _num = new Label3D
        {
            Text = "", PixelSize = 0.018f, FontSize = 220, Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 3.5f, 0),
            Modulate = new Color(1f, 0.85f, 0.35f), OutlineSize = 40, OutlineModulate = new Color(0, 0, 0, 0.95f), NoDepthTest = true, Font = UiTheme.Chunky,
        };
        AddChild(_num);
        _name = new Label3D
        {
            Text = "", PixelSize = 0.008f, FontSize = 160, Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 5.6f, 0),
            Modulate = new Color(0.95f, 0.9f, 0.8f), OutlineSize = 30, OutlineModulate = new Color(0, 0, 0, 0.95f), NoDepthTest = true, Font = UiTheme.Chunky,
        };
        AddChild(_name);
        _light = new OmniLight3D { Position = new Vector3(0, 4.5f, 0), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 1.6f, OmniRange = 14f };
        AddChild(_light);

        // the shaft: a black hole in the floor with a rim (placeholder; the thrown miner is animated over it)
        var shaft = new Node3D { Name = "Shaft", TopLevel = true };
        AddChild(shaft);
        shaft.GlobalPosition = cave.ShaftPos;
        var hole = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 0.04f, RadialSegments = 14, Rings = 1 }, MaterialOverride = Util.Mat(Colors.Black) };
        hole.Position = new Vector3(0, 0.03f, 0);
        shaft.AddChild(hole);
        var ring = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.65f, BottomRadius = 1.65f, Height = 0.03f, RadialSegments = 14, Rings = 1 }, MaterialOverride = Util.Mat(new Color(0.55f, 0.12f, 0.1f), 0.5f) };
        ring.Position = new Vector3(0, 0.015f, 0);
        shaft.AddChild(ring);
    }

    /// <summary>Contract for the next step: show a pile (name + weight) on the Scale.</summary>
    public void Show(string name, int weight)
    {
        _num.Text = weight.ToString(); _name.Text = name; _lastName = name;
        _panY = 0.95f - Mathf.Clamp(weight * 0.025f, 0f, 0.55f);
    }

    /// <summary>Nothing on the Scale.</summary>
    public void Idle() { _num.Text = ""; _name.Text = ""; _panY = 0.95f; }

    public override void _Process(double delta)
    {
        var g = Game.I; var w = g.Weigh;
        if (g.Phase == GamePhase.Weighing && w != null && w.Stage == "reveal" && w.CurPeer >= 0)
            Show(Net.I.NameOf(w.CurPeer), w.ShownWeight(Time.GetTicksMsec() / 1000.0));
        else Idle();
        _pan.Position = _pan.Position.Lerp(new Vector3(0, _panY, 0), Mathf.Min(1f, (float)delta * 6f));
    }
}
