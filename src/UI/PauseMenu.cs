using Godot;

namespace AppStein;

/// <summary>Esc menu during a round: resume, settings, leave match. The game keeps running (multiplayer). Releases/restores mouse capture.</summary>
public partial class PauseMenu : CanvasLayer
{
    public static PauseMenu I;
    Control _root, _menuBox, _settingsBox;
    public bool IsOpen => _root.Visible;

    public override void _Ready()
    {
        I = this;
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.Apply(new Control { Visible = false });
        UiKit.FullRect(_root);
        AddChild(_root);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.62f) };
        UiKit.FullRect(dim);
        _root.AddChild(dim);
        var center = new CenterContainer(); UiKit.FullRect(center); _root.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        _menuBox = UiKit.VBox(12);
        _menuBox.CustomMinimumSize = new Vector2(340, 0);
        panel.AddChild(_menuBox);
        _menuBox.AddChild(UiKit.MakeTitle("PAUSED", 38));
        _menuBox.AddChild(UiKit.MakeLabel("The cave does not wait for you.", 15, UiTheme.Dim, HorizontalAlignment.Center));
        _menuBox.AddChild(UiKit.Rule());
        _menuBox.AddChild(UiKit.MakeButton("Resume", () => Open(false), 280, true));
        _menuBox.AddChild(UiKit.MakeButton("Settings", () => ShowSettings(true), 280));
        _menuBox.AddChild(UiKit.MakeButton("Leave match", () => { Open(false); Game.I.LeaveToMenu(); }, 280));

        _settingsBox = new SettingsPanel(() => ShowSettings(false)) { Visible = false };
        center.AddChild(_settingsBox);
        panel.Visible = true;
        _menuPanel = panel;
        Game.I.PhaseChanged += OnPhase;
    }

    PanelContainer _menuPanel;

    void ShowSettings(bool on)
    {
        _settingsBox.Visible = on;
        _menuPanel.Visible = !on;
    }

    void OnPhase()
    {
        if (IsOpen && Game.I.Phase != GamePhase.Round) Open(false);
    }

    public void Open(bool on)
    {
        if (on == _root.Visible) return;
        _root.Visible = on;
        ShowSettings(false);
        Sfx.Play(on ? SfxNames.UiOpen : SfxNames.UiClose);
        if (on) Input.MouseMode = Input.MouseModeEnum.Visible;
        else if (DisplayServer.GetName() != "headless" && Game.I.Phase == GamePhase.Round && (Game.I.LocalMiner != null || Game.I.LocalGhost != null))
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent e)
    {
        if (!e.IsActionPressed("pause")) return;
        if (Game.I.Phase != GamePhase.Round || DisplayServer.GetName() == "headless") return;
        if (_settingsBox.Visible) ShowSettings(false); else Open(!IsOpen);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        // another system (new miner spawn) may re-capture the mouse while we are open: keep the cursor free
        if (IsOpen && Input.MouseMode != Input.MouseModeEnum.Visible && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
