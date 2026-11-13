using Godot;

namespace AppStein;

/// <summary>Full-screen film grain + vignette (a canvas shader on a layer below the HUD). Toggled in the settings (UserSettings.FilmGrain).</summary>
public partial class FilmGrain : CanvasLayer
{
    public static FilmGrain I;
    ColorRect _rect;

    public override void _Ready()
    {
        I = this;
        UserSettings.Load();
        Layer = 2;
        _rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = Colors.White };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rect.Material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type canvas_item;
float h(vec2 p){ return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment(){
  float g = h(floor(FRAGCOORD.xy * 0.5) + floor(TIME * 24.0) * 17.0);
  float v = smoothstep(0.3, 1.0, length((UV - vec2(0.5)) * vec2(1.0, 0.85)) * 1.4);
  float a = v * 0.75;
  float ga = abs(g - 0.5) * 0.14;
  vec3 c = g > 0.5 ? vec3(1.0) : vec3(0.0);
  COLOR = vec4(mix(vec3(0.0), c, ga / (a + ga + 0.0001)), a + ga * (1.0 - a));
}",
            },
        };
        AddChild(_rect);
        Apply();
    }

    public override void _ExitTree() { I = null; }
    public void Apply() { if (_rect != null) _rect.Visible = UserSettings.FilmGrain; }
}
