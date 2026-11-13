using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Client side of the chest hold (hold E for 2.5 s): the local timer drives the bar, the server decides and can cancel.</summary>
public static class Loot2Client
{
    public static bool Holding;
    public static int HoldId;
    static double _start;
    static double Now => Time.GetTicksMsec() / 1000.0;
    public static float Progress => Holding ? Mathf.Clamp((float)((Now - _start) / LootTuning.HoldSeconds), 0f, 1f) : 0f;
    /// <summary>Test counter: hold messages this client received (a client that never held anything must stay at 0: nobody else's hold is sent to it).</summary>
    public static int HoldMessages;

    public static void Reset() { Holding = false; HoldId = 0; }
    /// <summary>Screenshot staging (--l2ui=toast): shows the hold bar at this progress.</summary>
    public static void DebugHold(float p) { Holding = true; HoldId = -1; _start = Now - p * LootTuning.HoldSeconds; }

    /// <summary>A hold the server never answered (lost message, chest gone) must not stay on screen: give up shortly after the 2.5 s are over.</summary>
    public static void Watchdog() { if (Holding && Now - _start > LootTuning.HoldSeconds + 2.5) { Holding = false; Util.Pass("chest_hold_timeout"); } }

    public static void Start(int id)
    {
        if (Holding && HoldId == id) return;
        Holding = true; HoldId = id; _start = Now;
        Game.I.SendChestHold(id, true);
    }

    public static void Stop()
    {
        if (!Holding) return;
        Holding = false;
        Game.I.SendChestHold(HoldId, false);
    }

    public static void OnServerHold(int id, int mode)
    {
        HoldMessages++;
        Util.Pass("chest_hold_msg", $"id={id} mode={mode}");
        if (mode == 1) { Holding = true; HoldId = id; _start = Now; }
        else Holding = false;
    }

    /// <summary>Human input: pressing E on a chest starts the hold, releasing E (or looking away from the chest for good) ends it. Returns true when the press was consumed.</summary>
    public static bool Update(NetObject target, MinerInput c)
    {
        if (Holding)
        {
            if (!c.InteractHeld) Stop();
            return true;
        }
        if (c.Interact && target is GlowChestObj ch && ch.Closed) { Start(ch.Id); return true; }
        return false;
    }
}

/// <summary>Local screen feedback of step 2 (never replicated): flash, camera shake, hold bar, sack toasts. Built in code, no renderer-specific features.</summary>
public partial class Loot2Ui : CanvasLayer
{
    public static Loot2Ui I;
    ColorRect _flash;
    Control _bar;
    Label _barLabel;
    VBoxContainer _toasts;
    float _flashA, _flashDur, _flashT;
    Color _flashCol = Colors.White;
    float _shakeAmp, _shakeDur, _shakeT;
    Camera3D _shakeCam; Vector3 _baseRot; float _baseH, _baseV;
    List<string> _prevSack = new(), _prevSteins = new();
    bool _baseline;
    float _quietUntil;   // round timer value: no toasts while more than that is left (the start kit arrives with the first own states)
    readonly RandomNumberGenerator _rng = new();
    public static int ToastsShown;

    public static void Ensure()
    {
        if (I != null && GodotObject.IsInstanceValid(I)) return;
        I = new Loot2Ui { Name = "Loot2Ui" };
        Game.I.AddChild(I);
    }

    public override void _Ready()
    {
        Layer = 8; ProcessMode = ProcessModeEnum.Always;
        _rng.Randomize();
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_flash);
        _bar = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _bar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bar.Draw += DrawBar;
        AddChild(_bar);
        _barLabel = UiKit.MakeLabel("OPENING...", 20, new Color(1f, 0.9f, 0.55f), HorizontalAlignment.Center, 6);
        _barLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _barLabel.GrowHorizontal = Control.GrowDirection.Both;
        _bar.AddChild(_barLabel);
        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _toasts.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _toasts.GrowHorizontal = Control.GrowDirection.Both;
        _toasts.OffsetTop = 140; _toasts.OffsetBottom = 340;
        _toasts.AddThemeConstantOverride("separation", 2);
        AddChild(_toasts);
        Game.I.OwnChanged += OnOwn;
        if (Util.HasArg("l2ui"))
            GetTree().CreateTimer(Util.Arg("l2uit", "3").ToFloat()).Timeout += () =>
            {
                Loot2Client.DebugHold(0.55f);
                Toast("+10 RUBY", LootTuning.ColorOfItem("ore:ruby"), "tribute +10   burden +1", 2);
                Toast("+4 SILVER", LootTuning.ColorOfItem("ore:silver"), "tribute +4   burden +2", 1);
                Toast("LUCK STEIN", LootTuning.ColorOfItem("stein:luck"), "RARE POWER STEIN", 3);
            };
        _quietUntil = Game.I.TimeLeft - 3.5f;
        Game.I.PhaseChanged += () => { _baseline = false; _quietUntil = Game.I.TimeLeft - 3.5f; Loot2Client.Reset(); };
    }

    public void Flash(Color c, float alpha, float secs)
    {
        if (alpha <= _flashA * (1f - _flashT / Math.Max(0.01f, _flashDur))) return;   // never replace a stronger flash that is still going
        _flashCol = c; _flashA = alpha; _flashDur = secs; _flashT = 0;
    }

    public void Shake(float amp, float secs)
    {
        if (amp < _shakeAmp * (1f - _shakeT / Math.Max(0.01f, _shakeDur))) return;
        _shakeAmp = amp; _shakeDur = secs; _shakeT = 0;
        var cam = GetViewport().GetCamera3D();
        if (cam != null && cam != _shakeCam) { _shakeCam = cam; _baseRot = cam.Rotation; _baseH = cam.HOffset; _baseV = cam.VOffset; }
    }

    void DrawBar()
    {
        var sz = _bar.Size;
        float w = 300, h = 18, x = sz.X / 2f - w / 2f, y = sz.Y * 0.64f;
        _bar.DrawRect(new Rect2(x - 3, y - 3, w + 6, h + 6), new Color(0, 0, 0, 0.7f));
        _bar.DrawRect(new Rect2(x, y, w, h), new Color(0.12f, 0.09f, 0.05f, 0.95f));
        float p = Loot2Client.Progress;
        var c = new Color(1f, 0.78f, 0.2f).Lerp(new Color(1f, 0.97f, 0.7f), p);
        _bar.DrawRect(new Rect2(x, y, w * p, h), c);
        _barLabel.OffsetTop = y - 34;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // flash
        if (_flashA > 0f)
        {
            _flashT += dt;
            float u = Mathf.Clamp(_flashT / Math.Max(0.01f, _flashDur), 0f, 1f);
            float a = _flashA * (u < 0.08f ? u / 0.08f : Mathf.Pow(1f - (u - 0.08f) / 0.92f, 2f));
            _flash.Color = new Color(_flashCol.R, _flashCol.G, _flashCol.B, a);
            if (u >= 1f) { _flashA = 0f; _flash.Color = new Color(1, 1, 1, 0); }
        }
        // shake
        if (_shakeAmp > 0f)
        {
            _shakeT += dt;
            float u = Mathf.Clamp(_shakeT / Math.Max(0.01f, _shakeDur), 0f, 1f);
            float a = _shakeAmp * (1f - u) * (1f - u);
            if (_shakeCam != null && GodotObject.IsInstanceValid(_shakeCam))
            {
                _shakeCam.HOffset = _baseH + _rng.RandfRange(-1, 1) * a; _shakeCam.VOffset = _baseV + _rng.RandfRange(-1, 1) * a;
                _shakeCam.Rotation = _baseRot + new Vector3(_rng.RandfRange(-1, 1) * a * 0.6f, _rng.RandfRange(-1, 1) * a * 0.6f, _rng.RandfRange(-1, 1) * a * 1.6f);
            }
            if (u >= 1f)
            {
                _shakeAmp = 0f;
                if (_shakeCam != null && GodotObject.IsInstanceValid(_shakeCam)) { _shakeCam.Rotation = _baseRot; _shakeCam.HOffset = _baseH; _shakeCam.VOffset = _baseV; }
                _shakeCam = null;
            }
        }
        Loot2Client.Watchdog();
        // hold bar
        bool show = Loot2Client.Holding && Game.I.Phase == GamePhase.Round;
        if (show != _bar.Visible) _bar.Visible = show;
        if (show) _bar.QueueRedraw();
    }

    // ------------------------------------------------------------------ sack toasts
    void OnOwn()
    {
        var g = Game.I; var inv = g.OwnInv;
        if (inv == null || g.Phase != GamePhase.Round) return;
        if (g.TimeLeft > _quietUntil || !_baseline || inv.Sack.Count < _prevSack.Count) { _baseline = true; _prevSack = new List<string>(inv.Sack); _prevSteins = new List<string>(inv.Steins); return; }
        var added = inv.Sack.Skip(_prevSack.Count).ToList();
        var steinsNew = new List<string>(inv.Steins);
        foreach (var s in _prevSteins) steinsNew.Remove(s);
        _prevSack = new List<string>(inv.Sack); _prevSteins = new List<string>(inv.Steins);
        if (added.Count > 3)
        {
            int w = added.Sum(Ores.WeightOf), b = added.Sum(Ores.BurdenOf);
            Toast($"+{added.Count} STONES", new Color(1f, 0.85f, 0.4f), $"tribute +{w}   burden +{b}", 1);
        }
        else foreach (var id in added)
        {
            var d = Ores.Get(id);
            string item = "ore:" + id;
            Toast(LootTuning.LabelOf(item), LootTuning.ColorOfItem(item), $"tribute +{d?.TributeWeight ?? 0}   burden +{d?.Burden ?? 1}", LootTuning.TierOfItem(item));
        }
        foreach (var s in steinsNew)
        {
            string item = "stein:" + s; var d = Steins.Get(s);
            Toast(LootTuning.LabelOf(item), LootTuning.ColorOfItem(item), (d?.Rarity.ToString().ToUpperInvariant() ?? "") + " POWER STEIN", 3);
        }
    }

    public void Toast(string text, Color col, string sub, int tier)
    {
        ToastsShown++;
        Util.Pass("loot_toast", $"text={text.Replace(' ', '_')} tier={tier}");
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        int size = tier switch { 0 => 22, 1 => 30, 2 => 38, _ => 46 };
        var l = UiKit.MakeLabel(text, size, col, HorizontalAlignment.Center, tier >= 2 ? 10 : 7);
        var s = UiKit.MakeLabel(sub, 15, new Color(col.R, col.G, col.B, 0.8f).Lerp(UiTheme.Parchment, 0.4f), HorizontalAlignment.Center, 5);
        box.AddChild(l); box.AddChild(s);
        _toasts.AddChild(box);
        while (_toasts.GetChildCount() > 4) { var old = _toasts.GetChild(0); _toasts.RemoveChild(old); old.QueueFree(); }
        var t = CreateTween();
        t.TweenProperty(box, "modulate:a", 1f, 0.1f);
        t.TweenInterval(tier >= 2 ? 2.4f : 1.5f);
        t.TweenProperty(box, "modulate:a", 0f, 0.5f);
        t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(box)) box.QueueFree(); }));
    }
}
