using Godot;

namespace AppStein;

/// <summary>Small 2D drawing of the Goblin's face for speech bubbles. mood: smug | happy | angry.</summary>
public partial class GoblinPortrait : Control
{
    public string Mood = "smug";

    public GoblinPortrait() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(52, 52); }

    Vector2 P(float x, float y) => new(x * Size.X, y * Size.Y);

    public override void _Draw()
    {
        var skin = new Color(0.40f, 0.68f, 0.28f);
        var dark = new Color(0.28f, 0.5f, 0.2f);
        var hair = new Color(0.82f, 0.82f, 0.86f);
        // frame
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.1f, 0.2f, 0.1f));
        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.Brass, false, 2f);
        // navy collar
        DrawColoredPolygon(new[] { P(.12f, 1f), P(.22f, .8f), P(.5f, .88f), P(.78f, .8f), P(.88f, 1f) }, new Color(0.14f, 0.22f, 0.46f));
        // ears
        DrawColoredPolygon(new[] { P(.28f, .42f), P(.02f, .2f), P(.3f, .6f) }, skin);
        DrawColoredPolygon(new[] { P(.72f, .42f), P(.98f, .2f), P(.7f, .6f) }, skin);
        // head
        DrawColoredPolygon(new[] { P(.28f, .25f), P(.72f, .25f), P(.78f, .55f), P(.62f, .85f), P(.38f, .85f), P(.22f, .55f) }, skin);
        // hair
        DrawColoredPolygon(new[] { P(.24f, .4f), P(.3f, .14f), P(.55f, .06f), P(.78f, .16f), P(.76f, .4f), P(.68f, .26f), P(.32f, .28f) }, hair);
        // nose
        DrawColoredPolygon(new[] { P(.44f, .48f), P(.56f, .48f), P(.62f, .68f), P(.5f, .74f), P(.38f, .66f) }, new Color(0.5f, 0.76f, 0.3f));
        // eyes + brows
        bool angry = Mood == "angry";
        DrawCircle(P(.38f, .45f), Size.X * .045f, new Color(0.95f, 0.93f, 0.8f));
        DrawCircle(P(.62f, .45f), Size.X * .045f, new Color(0.95f, 0.93f, 0.8f));
        DrawCircle(P(.385f, .46f), Size.X * .022f, Colors.Black);
        DrawCircle(P(.625f, .46f), Size.X * .022f, Colors.Black);
        float bl = angry ? 0.06f : -0.01f;
        DrawLine(P(.30f, .36f - bl * 0.5f), P(.46f, .38f + bl), new Color(0.55f, 0.55f, 0.6f), 3.5f, true);
        DrawLine(P(.70f, .36f - bl * 0.5f), P(.54f, .38f + bl), new Color(0.55f, 0.55f, 0.6f), 3.5f, true);
        // half-lids
        DrawLine(P(.33f, .43f), P(.43f, .43f), dark, 2.5f); DrawLine(P(.57f, .43f), P(.67f, .43f), dark, 2.5f);
        // mouth
        if (angry) DrawPolyline(new[] { P(.4f, .8f), P(.5f, .76f), P(.6f, .8f) }, new Color(0.12f, 0.2f, 0.08f), 2.5f, true);
        else if (Mood == "happy") DrawPolyline(new[] { P(.38f, .75f), P(.5f, .82f), P(.62f, .75f) }, new Color(0.12f, 0.2f, 0.08f), 2.5f, true);
        else DrawPolyline(new[] { P(.4f, .79f), P(.52f, .79f), P(.62f, .74f) }, new Color(0.12f, 0.2f, 0.08f), 2.5f, true);
    }
}
