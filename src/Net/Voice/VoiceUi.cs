using System;
using Godot;

namespace AppStein;

/// <summary>A small microphone (drawn): the bottom-left HUD glyph while you transmit.</summary>
public partial class MicGlyph : Control
{
    public float Pulse;
    public MicGlyph() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(20, 26); }
    public override void _Draw()
    {
        var c = new Color(UiTheme.Good, 0.75f + 0.25f * Pulse);
        var dark = new Color(0, 0, 0, 0.55f);
        float w = Size.X, h = Size.Y;
        DrawRect(new Rect2(0, 0, w, h), new Color(0, 0, 0, 0f));
        // capsule
        DrawRect(new Rect2(w * 0.32f - 1.5f, h * 0.06f - 1.5f, w * 0.36f + 3f, h * 0.5f + 3f), dark);
        DrawRect(new Rect2(w * 0.32f, h * 0.2f, w * 0.36f, h * 0.36f), c);
        DrawCircle(new Vector2(w * 0.5f, h * 0.2f), w * 0.18f, c);
        DrawCircle(new Vector2(w * 0.5f, h * 0.56f), w * 0.18f, c);
        // holder arc, stem, foot
        DrawArc(new Vector2(w * 0.5f, h * 0.5f), w * 0.34f, 0.15f * Mathf.Pi, 0.85f * Mathf.Pi, 12, c, 2f, true);
        DrawLine(new Vector2(w * 0.5f, h * 0.84f), new Vector2(w * 0.5f, h * 0.95f), c, 2f);
        DrawLine(new Vector2(w * 0.28f, h * 0.95f), new Vector2(w * 0.72f, h * 0.95f), c, 2f);
    }
}

/// <summary>A speaker with sound waves (drawn). Shows only while the player is talking; keeps its width otherwise so rows stay aligned.</summary>
public partial class SpeakerGlyph : Control
{
    readonly int _peer; bool _on;
    public SpeakerGlyph(int peer, float size = 22f) { _peer = peer; MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(size, size); SizeFlagsVertical = SizeFlags.ShrinkCenter; }
    public override void _Ready() { _on = VoiceHub.I != null && VoiceHub.I.IsTalking(_peer); QueueRedraw(); }
    public override void _Process(double delta)
    {
        bool on = VoiceHub.I != null && VoiceHub.I.IsTalking(_peer);
        if (on != _on) { _on = on; QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (!_on) return;
        VoiceGlyphs.DrawSpeaker(this, Size, UiTheme.Good);
    }
}

public static class VoiceGlyphs
{
    /// <summary>Draws the speaker icon (box + cone + two waves) into a CanvasItem.</summary>
    public static void DrawSpeaker(CanvasItem ci, Vector2 s, Color c)
    {
        var dark = new Color(0, 0, 0, 0.55f);
        var body = new[] { new Vector2(s.X * 0.08f, s.Y * 0.38f), new Vector2(s.X * 0.28f, s.Y * 0.38f), new Vector2(s.X * 0.52f, s.Y * 0.18f), new Vector2(s.X * 0.52f, s.Y * 0.82f), new Vector2(s.X * 0.28f, s.Y * 0.62f), new Vector2(s.X * 0.08f, s.Y * 0.62f) };
        ci.DrawColoredPolygon(body, c);
        ci.DrawPolyline(new[] { body[0], body[1], body[2], body[3], body[4], body[5], body[0] }, dark, 1.2f);
        var mid = new Vector2(s.X * 0.52f, s.Y * 0.5f);
        ci.DrawArc(mid, s.X * 0.2f, -0.42f * Mathf.Pi, 0.42f * Mathf.Pi, 10, c, 2f, true);
        ci.DrawArc(mid, s.X * 0.34f, -0.42f * Mathf.Pi, 0.42f * Mathf.Pi, 12, c, 2f, true);
    }

    static ImageTexture _tex;
    /// <summary>The same icon as a texture for the Sprite3D over a talking miner's head (gold, dark outline).</summary>
    public static ImageTexture SpeakerTexture()
    {
        if (_tex != null) return _tex;
        const int N = 64;
        var mask = new bool[N * N];
        bool Tri(float x, float y)   // box + cone
        {
            if (x >= 6 && x <= 20 && y >= 24 && y <= 40) return true;
            if (x > 20 && x <= 34) { float k = (x - 20) / 14f; return y >= 24 - k * 14 && y <= 40 + k * 14; }
            return false;
        }
        bool Wave(float x, float y)
        {
            float dx = x - 34, dy = y - 32; float r = MathF.Sqrt(dx * dx + dy * dy);
            bool front = dx > 0 && MathF.Abs(MathF.Atan2(dy, dx)) < 0.75f;
            return front && ((r > 10 && r < 13.5f) || (r > 19 && r < 22.5f));
        }
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) mask[y * N + x] = Tri(x, y) || Wave(x, y);
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (mask[y * N + x]) { img.SetPixel(x, y, new Color(1f, 0.86f, 0.35f)); continue; }
                bool near = false;
                for (int dy = -2; dy <= 2 && !near; dy++) for (int dx = -2; dx <= 2; dx++) { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < N && yy < N && mask[yy * N + xx]) { near = true; break; } }
                if (near) img.SetPixel(x, y, new Color(0, 0, 0, 0.85f));
            }
        _tex = ImageTexture.CreateFromImage(img);
        return _tex;
    }
}

/// <summary>Small meter bar for the Settings screen: the microphone level. Keeps the mic running (nothing is sent) while it is on screen.</summary>
public partial class MicMeter : Control
{
    public MicMeter() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(200, 16); SizeFlagsVertical = SizeFlags.ShrinkCenter; SizeFlagsHorizontal = SizeFlags.ExpandFill; }
    public override void _Process(double delta)
    {
        if (VoiceHub.I != null && IsVisibleInTree()) VoiceHub.I.MeterUntil = Time.GetTicksMsec() / 1000.0 + 0.3;
        QueueRedraw();
    }
    public override void _Draw()
    {
        var s = Size;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0.03f, 0.03f, 0.03f, 0.9f));
        float lv = VoiceHub.I?.MicLevel ?? 0f;
        float gate = VoiceTuning.OpenMicGate;
        var col = lv < gate ? UiTheme.Dim : (lv > 0.9f ? UiTheme.Danger : UiTheme.Good);
        DrawRect(new Rect2(1, 1, (s.X - 2) * Mathf.Clamp(lv, 0f, 1f), s.Y - 2), col);
        DrawLine(new Vector2(s.X * gate, 0), new Vector2(s.X * gate, s.Y), new Color(UiTheme.Gold, 0.8f), 1.5f);   // below this line open mic sends nothing
        DrawRect(new Rect2(0, 0, s.X, s.Y), UiTheme.BrassDark, false, 1.5f);
    }
}

/// <summary>The voice HUD layer: mic glyph while you transmit, speaker glyphs over talking miners' heads, the Tab overlay cursor release.</summary>
public partial class VoiceHud : CanvasLayer
{
    MicGlyph _mic; Label _tabHint;
    double _tabHeld; bool _freed;

    public override void _Ready()
    {
        Layer = 6;
        var root = UiTheme.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        UiKit.FullRect(root);
        AddChild(root);
        _mic = new MicGlyph { Visible = false };
        _mic.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _mic.OffsetLeft = 19; _mic.OffsetRight = 39; _mic.OffsetTop = -96; _mic.OffsetBottom = -70;
        root.AddChild(_mic);
        _tabHint = UiKit.MakeLabel("Keep holding Tab to free the cursor and mute a player", 14, UiTheme.Dim, HorizontalAlignment.Center, 3);
        _tabHint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _tabHint.GrowHorizontal = Control.GrowDirection.Both; _tabHint.GrowVertical = Control.GrowDirection.Begin;
        _tabHint.OffsetTop = -50; _tabHint.OffsetBottom = -28; _tabHint.Visible = false;
        root.AddChild(_tabHint);
        if (Util.HasArg("screen") && Util.Arg("screen").StartsWith("voice")) AddChild(new VoiceDebug { Name = "VoiceDebug" });
    }

    public override void _Process(double delta)
    {
        var hub = VoiceHub.I; var g = Game.I;
        if (hub == null) return;
        bool round = g.Phase == GamePhase.Round;
        _mic.Visible = round && hub.IsTalking(Net.I.MyId) && !PauseMenu.I.IsOpen;
        if (_mic.Visible) { _mic.Pulse = Mathf.Clamp(hub.MicLevel, 0f, 1f); _mic.QueueRedraw(); }

        // Tab overlay: held for a moment, the cursor is freed so the mute buttons can be clicked (look and walk pause meanwhile); released, the game gets the mouse back
        bool headless = DisplayServer.GetName() == "headless";
        bool tab = round && !headless && Input.IsActionPressed("scoreboard") && !PauseMenu.I.IsOpen && VoiceHub.UiAvailable && (g.LocalMiner != null || g.LocalGhost != null);
        if (tab)
        {
            _tabHeld += delta;
            if (_tabHeld > 0.6 && !_freed) { _freed = true; Input.MouseMode = Input.MouseModeEnum.Visible; }
        }
        else
        {
            if (_freed)
            {
                _freed = false;
                if (round && !PauseMenu.I.IsOpen && (g.LocalMiner != null || g.LocalGhost != null)) Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            _tabHeld = 0;
        }
        _tabHint.Visible = tab && !_freed && _tabHeld > 0.15;

        UpdateHeadGlyphs(g, hub);
    }

    void UpdateHeadGlyphs(Game g, VoiceHub hub)
    {
        if (g.World?.MinersRoot == null || DisplayServer.GetName() == "headless") return;
        foreach (var n in g.World.MinersRoot.GetChildren())
        {
            if (n is not Miner m || m.IsLocal) continue;
            bool talking = hub.IsTalking(m.PeerId);
            var gl = m.GetNodeOrNull<Sprite3D>("VoiceGlyph");
            if (gl == null)
            {
                if (!talking) continue;
                gl = new Sprite3D
                {
                    Name = "VoiceGlyph", Texture = VoiceGlyphs.SpeakerTexture(), PixelSize = 0.0065f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Position = new Vector3(0, 2.72f, 0), Shaded = false, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
                };
                m.AddChild(gl);
            }
            gl.Visible = talking;
        }
    }

    /// <summary>True when the pointer is over this control and the cursor is free: the Tab list then keeps its rows (a click needs the same button for press and release).</summary>
    public static bool CursorOver(Control c) =>
        Input.MouseMode == Input.MouseModeEnum.Visible && c.GetGlobalRect().HasPoint(c.GetViewport().GetMousePosition());
}

/// <summary>Voice bits of the lobby list, the Tab overlay and the Settings screen.</summary>
public static class VoiceUi
{
    /// <summary>Adds the speaker glyph and (for other players) a local mute button to a player row. Does nothing when voice is not available.</summary>
    public static void ScoreExtras(HBoxContainer row, int peer)
    {
        if (!VoiceHub.UiAvailable) return;
        row.AddChild(new SpeakerGlyph(peer));
        if (peer == Net.I.MyId) { row.AddChild(new Control { CustomMinimumSize = new Vector2(74, 0), MouseFilter = Control.MouseFilterEnum.Ignore }); return; }
        bool muted = VoiceHub.I?.IsMuted(peer) ?? false;
        var b = new Button { Text = muted ? "Muted" : "Mute", ToggleMode = true, ButtonPressed = muted, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(74, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Mute this player's voice (only for you)" };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Toggled += on => { VoiceHub.I?.SetMuted(peer, on); b.Text = on ? "Muted" : "Mute"; Sfx.Play(SfxNames.UiClick); };
        row.AddChild(b);
    }

    /// <summary>The "VOICE CHAT" block of the Settings screen.</summary>
    public static void AddSettings(VBoxContainer v)
    {
        v.AddChild(UiKit.Rule());
        v.AddChild(UiKit.MakeLabel("VOICE CHAT", 14, UiTheme.Brass));
        if (!VoiceHub.UiAvailable)
        {
            v.AddChild(UiKit.MakeLabel("Voice needs Steam", 18, UiTheme.Dim));
            return;
        }
        // mode
        var modes = UiKit.HBox(8);
        var group = new ButtonGroup();
        string[] names = { "Open mic", "Push to talk (V)", "Off" };
        for (int i = 0; i < 3; i++)
        {
            int mi = i;
            var b = new Button { Text = names[i], ToggleMode = true, ButtonGroup = group, ButtonPressed = UserSettings.VoiceMode == i, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddThemeFontSizeOverride("font_size", 17);
            b.Pressed += () => { UserSettings.VoiceMode = mi; UserSettings.Save(); Sfx.Play(SfxNames.UiClick); };
            modes.AddChild(b);
        }
        v.AddChild(modes);
        // mic level
        var meter = UiKit.HBox(14);
        var ml = UiKit.MakeLabel("Microphone", 20); ml.CustomMinimumSize = new Vector2(200, 0); ml.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        meter.AddChild(ml); meter.AddChild(new MicMeter());
        v.AddChild(meter);
        // volume
        var vol = UiKit.HBox(14);
        var vl = UiKit.MakeLabel("Voice volume", 20); vl.CustomMinimumSize = new Vector2(200, 0); vl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var s = new HSlider { MinValue = 0, MaxValue = 1.5, Step = 0.01, Value = UserSettings.VoiceVolume, CustomMinimumSize = new Vector2(190, 28), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None };
        var val = UiKit.MakeLabel(Mathf.RoundToInt(UserSettings.VoiceVolume * 100) + "%", 20, UiTheme.Gold, HorizontalAlignment.Right); val.CustomMinimumSize = new Vector2(64, 0); val.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        s.ValueChanged += x => { UserSettings.VoiceVolume = (float)x; val.Text = Mathf.RoundToInt((float)x * 100) + "%"; };
        s.DragEnded += _ => UserSettings.Save();
        vol.AddChild(vl); vol.AddChild(s); vol.AddChild(val);
        v.AddChild(vol);
        v.AddChild(UiKit.MakeLabel("Open mic sends only while you talk (the gold mark on the meter is the threshold). Voices carry: monsters hear loud talking.", 13, UiTheme.Dim));
    }
}

/// <summary>Screenshot staging for the voice screens (`--screen=voice_talk | voice_score | voice_settings`). Does nothing without the arg.</summary>
public partial class VoiceDebug : Node
{
    string _screen; double _t; bool _done;
    public override void _Ready() { _screen = Util.Arg("screen"); ProcessMode = ProcessModeEnum.Always; ProcessPriority = -100; }
    public override void _Process(double delta)
    {
        _t += delta;
        var g = Game.I; var hub = VoiceHub.I;
        if (hub == null || _done) return;
        bool inRound = g.Phase == GamePhase.Round && g.World?.Cave != null && g.LocalMiner != null;
        switch (_screen)
        {
            case "voice_settings":
                if (_t > 0.8) { MenuUI.I?.ShowSettings(true); _done = true; }
                break;
            case "voice_talk":   // a miner 3 m ahead is talking, and so are you (mic glyph bottom left)
                if (!inRound || _t < 1.0) return;
                {
                    var me = g.LocalMiner;
                    var other = new Miner { Name = "9999", PeerId = 9999, PlayerName = "Gus", IsLocal = false };
                    g.World.MinersRoot.AddChild(other);
                    var fwd = -me.GlobalTransform.Basis.Z;
                    other.GlobalPosition = me.GlobalPosition + fwd * 3.0f + me.GlobalTransform.Basis.X * 0.7f;
                    other.Rotation = new Vector3(0, me.Rotation.Y + 2.8f, 0);
                    other.SetHeld("pickaxe");
                    other.ApplyNetState(other.GlobalPosition, other.Rotation.Y, 0, 0);
                    hub.DebugTalking(9999, true); hub.DebugTalking(Net.I.MyId, true);
                    hub.MicLevel = 0.7f;
                }
                _done = true; break;
            case "voice_score":   // Tab overlay: one miner talking, one muted
                if (!inRound || _t < 1.0) return;
                Net.I.Names[11] = "Cam"; Net.I.Names[22] = "Alice"; Net.I.Names[33] = "Bob";
                g.Status[11] = MinerStatus.Alive; g.Status[22] = MinerStatus.Alive; g.Status[33] = MinerStatus.Dead;
                hub.DebugTalking(22, true); hub.SetMuted(33, true);
                Hud.I.ForceScoreboard = true; _done = true; break;
        }
    }
}
