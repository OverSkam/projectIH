using System;
using Godot;

namespace AppStein;

/// <summary>Settings: mouse sensitivity, FOV, master volume, fullscreen. Saved to user:// and applied immediately.</summary>
public partial class SettingsPanel : PanelContainer
{
    public Action OnBack;

    public SettingsPanel(Action onBack)
    {
        OnBack = onBack;
        CustomMinimumSize = new Vector2(520, 0);
        var v = UiKit.VBox(14);
        AddChild(v);
        v.AddChild(UiKit.MakeTitle("SETTINGS", 34));
        v.AddChild(UiKit.Rule());
        v.AddChild(Row("Mouse sensitivity", 0.2f, 3f, 0.05f, UserSettings.MouseSens, x => { UserSettings.MouseSens = x; }, x => x.ToString("0.00") + "x"));
        v.AddChild(Row("Field of view", 60f, 110f, 1f, UserSettings.Fov, x => { UserSettings.Fov = x; }, x => Mathf.RoundToInt(x) + "°"));
        v.AddChild(Row("Master volume", 0f, 1f, 0.01f, UserSettings.Volume, x => { UserSettings.Volume = x; UserSettings.Apply(); }, x => Mathf.RoundToInt(x * 100) + "%"));

        var fs = new CheckBox { Text = "  Fullscreen", ButtonPressed = UserSettings.Fullscreen, FocusMode = FocusModeEnum.None };
        fs.Toggled += on => { UserSettings.Fullscreen = on; UserSettings.Apply(GetWindow()); UserSettings.Save(); Sfx.Play("ui_click"); };
        v.AddChild(fs);
        v.AddChild(UiKit.Spacer(4));
        var back = UiKit.MakeButton("Back", () => { UserSettings.Save(); OnBack?.Invoke(); }, 200, true);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        v.AddChild(back);
    }

    Control Row(string label, float min, float max, float step, float value, Action<float> set, Func<float, string> fmt)
    {
        var h = UiKit.HBox(14);
        var l = UiKit.MakeLabel(label, 20); l.CustomMinimumSize = new Vector2(200, 0); l.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        h.AddChild(l);
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(190, 28), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None };
        var val = UiKit.MakeLabel(fmt(value), 20, UiTheme.Gold, HorizontalAlignment.Right); val.CustomMinimumSize = new Vector2(64, 0); val.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        s.ValueChanged += x => { set((float)x); val.Text = fmt((float)x); };
        s.DragEnded += _ => UserSettings.Save();
        h.AddChild(s); h.AddChild(val);
        return h;
    }
}
