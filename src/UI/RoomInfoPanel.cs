using Godot;

namespace AppStein;

/// <summary>A small padlock (drawn, no font glyph needed): shown next to the lobby title when the room has a password.</summary>
public partial class LockIcon : Control
{
    public LockIcon(float size = 24f) { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(size * 0.8f, size); TooltipText = "This room has a password"; }

    public override void _Draw()
    {
        var s = Size;
        var gold = UiTheme.Gold;
        DrawArc(new Vector2(s.X * 0.5f, s.Y * 0.4f), s.X * 0.3f, Mathf.Pi, Mathf.Tau, 14, gold, 2.6f, true);
        DrawLine(new Vector2(s.X * 0.2f, s.Y * 0.4f), new Vector2(s.X * 0.2f, s.Y * 0.5f), gold, 2.6f);
        DrawLine(new Vector2(s.X * 0.8f, s.Y * 0.4f), new Vector2(s.X * 0.8f, s.Y * 0.5f), gold, 2.6f);
        DrawRect(new Rect2(0, s.Y * 0.48f, s.X, s.Y * 0.5f), gold);
        DrawRect(new Rect2(0, s.Y * 0.48f, s.X, s.Y * 0.5f), new Color(0, 0, 0, 0.7f), false, 1.2f);
        DrawCircle(new Vector2(s.X * 0.5f, s.Y * 0.68f), s.X * 0.1f, new Color(0.2f, 0.14f, 0.04f));
        DrawRect(new Rect2(s.X * 0.46f, s.Y * 0.68f, s.X * 0.08f, s.Y * 0.16f), new Color(0.2f, 0.14f, 0.04f));
    }
}

/// <summary>What the host sees about the room: the internet code and the same-Wi-Fi code (each with a Copy button) and one status line. Used in the lobby and the pause menu.</summary>
public partial class RoomInfoPanel : VBoxContainer
{
    readonly Label _netCap, _netCode, _lanCap, _lanCode, _status;
    readonly Button _netCopy, _lanCopy;
    readonly Control _netRow, _lanRow, _lockRow;
    double _copiedNet, _copiedLan;

    public RoomInfoPanel(int width = 440, int codeSize = 26)
    {
        AddThemeConstantOverride("separation", 8);
        CustomMinimumSize = new Vector2(width, 0);
        var lockRow = UiKit.HBox(8); lockRow.Alignment = BoxContainer.AlignmentMode.Center;
        lockRow.AddChild(new LockIcon(20) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        lockRow.AddChild(UiKit.MakeLabel("Room password is on", 15, UiTheme.Gold));
        _lockRow = lockRow; AddChild(lockRow);
        (_netRow, _netCap, _netCode, _netCopy) = Row("INTERNET CODE  (friends anywhere)", codeSize);
        (_lanRow, _lanCap, _lanCode, _lanCopy) = Row("SAME WI-FI CODE  (friends in this home)", codeSize);
        _netCopy.Pressed += () => { var c = Net.I.Room.InternetCode; if (c != null) { DisplayServer.ClipboardSet(c); _copiedNet = 1.6; } };
        _lanCopy.Pressed += () => { var c = Net.I.Room.LanCode; if (c != null) { DisplayServer.ClipboardSet(c); _copiedLan = 1.6; } };
        _status = UiKit.MakeLabel("", 15, UiTheme.Dim, HorizontalAlignment.Center);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(width, 0);
        AddChild(_status);
    }

    public bool ShowLock;

    (Control row, Label cap, Label code, Button copy) Row(string caption, int codeSize)
    {
        var h = UiKit.HBox(10);
        var v = UiKit.VBox(0); v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var cap = UiKit.MakeLabel(caption, 13, UiTheme.Brass);
        var code = UiKit.MakeLabel("", codeSize, UiTheme.Gold);
        v.AddChild(cap); v.AddChild(code);
        h.AddChild(v);
        var b = UiKit.MakeButton("Copy", () => { }, 90); b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        h.AddChild(b);
        AddChild(h);
        return (h, cap, code, b);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var r = Net.I.Room;
        _copiedNet -= delta; _copiedLan -= delta;
        _lockRow.Visible = ShowLock && Net.I.HasPassword;
        bool off = r.State == RoomNetState.Off;
        _netRow.Visible = !off;
        string ic = r.InternetCode, lc = r.LanCode;
        _netCode.Text = ic ?? (r.State == RoomNetState.Checking ? "..." : "not available");
        _netCode.AddThemeColorOverride("font_color", ic != null ? UiTheme.Gold : UiTheme.Dim);
        _netCopy.Disabled = ic == null;
        _netCopy.Text = _copiedNet > 0 ? "Copied" : "Copy";
        _lanRow.Visible = lc != null;
        _lanCode.Text = lc ?? "";
        _lanCopy.Text = _copiedLan > 0 ? "Copied" : "Copy";
        switch (r.State)
        {
            case RoomNetState.Checking: _status.Text = "Checking your router..."; _status.AddThemeColorOverride("font_color", UiTheme.Dim); break;
            case RoomNetState.Ready: _status.Text = "Ready: friends anywhere can join with this code"; _status.AddThemeColorOverride("font_color", UiTheme.Good); break;
            case RoomNetState.Failed:
                _status.Text = "Your router did not open the port automatically. Friends on other networks may not be able to join. See Guide → Playing online.";
                _status.AddThemeColorOverride("font_color", new Color(1f, 0.62f, 0.38f)); break;
            default: _status.Text = ""; break;
        }
        _status.Visible = _status.Text != "";
    }
}
