using System.Linq;
using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// The staff roll: a scripted flight through Parizh in Chelyabinsk oblast — the steppe village that built
/// itself a replica of the Eiffel Tower, a cell mast dressed as the real thing standing a fifth its size over
/// a scatter of low houses.
///
/// The camera comes in low over the village, climbs into Nikitos standing on top of the tower, turns and dives
/// through the KURY-GRIL kiosk at its foot, then flies out along the line of everyone who made the game and
/// finally leaves the ground altogether, ending in space.
///
/// The WORLD (tower, houses, steppe, sky) is raymarched in <c>staff_eiffel.fs</c>. The CAST is billboards: a
/// card per person, standing in the same world and projected here. That split is deliberate — their art
/// already exists as pictures, and a picture on a card keeps its own alpha and detail, which no SDF of a
/// person was going to match. Both use the one camera worked out in <see cref="CameraAt"/>, handed to the
/// shader as uniforms so there is a single source of truth for where the flight is.
///
/// Not a <see cref="Common.StageBackground"/>: that base is bound to a <c>GameBox</c>'s tick and its render
/// targets, and the staff roll has neither.
/// </summary>
public class ParisChelyabinskBackground
{
    /// <summary>The camera's focal length. MUST match the shader's FOCAL, or the cast drifts off the scene.</summary>
    private const float Focal = 1.35f;

    /// <summary>
    /// How long the flight lasts — the length of the roll it plays under. Nine legs share it, so this is also
    /// what sets the pace: at 54 seconds each stop gets six of them to come up, be read and be flown through.
    /// </summary>
    public const double Duration = 54.0;

    /// <summary>The texture groups the cast's art lives in, beyond the "staff" set the roll already loads.</summary>
    public static readonly string[] ArtGroups = ["main", "game", "akob", "qaw"];

    private readonly ShaderHandle Shader;
    private readonly int LocationTime, LocationResolution, LocationPos, LocationRight, LocationUp, LocationFwd;
    private readonly BasicTexture Blank;
    private readonly Stop[] Stops;
    private readonly Vector3[] Path;
    private readonly RenderedTexture UngManName;

    /// <summary>Where the flight ends up; the closing words hang on the same line, further along it.</summary>
    private static readonly Vector3 SpaceEnd = new(60, 340, -580);
    private Vector3 SpaceWordsAt;

    /// <summary>
    /// One thing the flight passes through: a picture standing in the world, with an optional line of text
    /// under it. <see cref="Height"/> is in metres — the width follows from the picture, so nothing stretches.
    /// </summary>
    private sealed class Stop
    {
        public Vector3 Position;
        public BasicTexture Art;
        public Rect Source;
        /// <summary>How tall it stands, in metres — except on a stop that is nothing but words, where it is
        /// how WIDE the line is instead. A line of text is far wider than it is tall, so sizing one by its
        /// height put it hundreds of metres across and the camera inside the middle of a letter.</summary>
        public float Height;
        public RenderedTexture? Caption;
        /// <summary>An extra picture riding in front of this one — the pizza Nikitab is working through.</summary>
        public BasicTexture? Prop;
        public Vector3 PropOffset;
        public float PropHeight;
        /// <summary>Draw the name plate (UngMan, who has no art of his own) instead of <see cref="Art"/>.</summary>
        public bool IsNamePlate;
    }

    public ParisChelyabinskBackground()
    {
        Shader = Runtime.CurrentRuntime.Shaders["staff_eiffel"];
        LocationTime = GetShaderLocation(Shader, "time");
        LocationResolution = GetShaderLocation(Shader, "resolution");
        LocationPos = GetShaderLocation(Shader, "camPos");
        LocationRight = GetShaderLocation(Shader, "camRight");
        LocationUp = GetShaderLocation(Shader, "camUp");
        LocationFwd = GetShaderLocation(Shader, "camFwd");
        Blank = Runtime.CurrentRuntime.Textures["star.png"];   // content ignored; the world is procedural

        UngManName = Text("UngMan", 110, translate: false);
        Stops = BuildStops();

        // The flight is a smooth curve THROUGH the first seven stops, so it really does fly into each rather
        // than cutting to it. The climb point between the last of them and the finish is what keeps the curve
        // off the ground: heading straight from Dmitry up to space made it dip below the steppe first, which
        // is how a Catmull-Rom pays for a sharp corner.
        Path =
        [
            new Vector3(0, 26, -330),        // come in over the village
            ..Stops[..8].Select(x => x.Position),
            new Vector3(30, 130, -520),      // pull up
            SpaceEnd,                        // and coast, with the closing words still ahead
        ];

        // Put the closing words exactly on the line the flight ends up travelling, so they sit dead centre as
        // it coasts toward them. Guessing a point near the end put them off the top of the frame instead.
        Vector3 endDir = Vector3.Normalize(Path[^1] - Path[^2]);
        Stops[^1].Position = Path[^1] + endDir * 110f;
        SpaceWordsAt = Stops[^1].Position;
    }

    /// <summary>The frame of a dialog-art sheet to stand a character on: the sheets are strips of 768x1024
    /// frames and the first is the neutral pose.</summary>
    private static Rect Frame0(BasicTexture sheet) =>
        new(0, 0, MathF.Min(sheet.Height * 768f / 1024f, sheet.Width), sheet.Height);

    /// <summary>
    /// One plate of text. The roll's lines are translation.json keys, so they come out in the game's own
    /// lettering (and pick between their variants, which is why no two runs of the roll read quite the same);
    /// a name is passed through as it is written.
    /// </summary>
    private static RenderedTexture Text(string s, int size, bool translate = true) =>
        Helper.DrawText(translate ? Helper.Translate(s) : s, size, 10, 8, 2,
            Runtime.CurrentRuntime.Fonts["googlesans"], Rgba.White, "shadow");

    private Stop[] BuildStops()
    {
        var textures = Runtime.CurrentRuntime.Textures;

        Stop Picture(float x, float y, float z, string key, float height, string caption) =>
            new()
            {
                Position = new Vector3(x, y, z),
                Art = textures[key],
                Source = Helper.GetFullSource(textures[key]),
                Height = height,
                Caption = caption.Length > 0 ? Text(caption, 64) : null,
            };

        Stop Person(float x, float y, float z, string key, float height, string caption)
        {
            Stop stop = Picture(x, y, z, key, height, caption);
            stop.Source = Frame0(stop.Art);
            return stop;
        }

        // The roll opens on the game's own logo, hanging over the village on the way in - the first thing the
        // flight goes through, before it reaches the tower.
        Stop logo = Picture(0, 34, -232, "game_logo.png", 4.6f, "");   // ~45 m across, at its own aspect

        // From there each stop is further back over the village than the last, so the flight keeps moving out
        // and whoever is coming up next is always dead ahead of it.
        Stop nikitos = Picture(0, 62, 0, "nikitos_boss_art.png", 26f, "credits.nikitos");   // on the tower
        Stop kiosk = Picture(0, 12, -54, "microsoft.png", 24f, "credits.kiosk");            // at its foot
        Stop akob = Person(-38, 17, -132, "akob_dialog_arts.png", 27f, "credits.akob");
        Stop qaw = Person(38, 17, -212, "qaw_dialog_arts.png", 27f, "credits.qaw");
        Stop nikitab = Person(-34, 17, -292, "nikitab_dialog_art.png", 27f, "credits.nikitab");
        // pizza.png has no alpha at all (its transparency is a checkerboard baked into the pixels), so the
        // slice — which is a real cutout — is what he is eating.
        nikitab.Prop = textures["pizzaslice.png"];
        nikitab.PropOffset = new Vector3(4.0f, -1.5f, 6.5f);                           // held up to his mouth
        nikitab.PropHeight = 9f;

        var ungman = new Stop
        {
            Position = new Vector3(34, 19, -372),
            Height = 26f,                          // the name plate is sized by its width too
            IsNamePlate = true,
            Caption = Text("credits.ungman", 64),
        };
        Stop dmitry = Person(0, 17, -452, "dima_dialog_arts.png", 27f, "credits.dmitry");
        // The last one is out in space, and unlike the rest the flight does NOT pass through it: it is what
        // the camera is still coming up on when the roll fades, so the words are on screen at the end rather
        // than a memory of something flown through. Its position is pinned to the flight's own last heading
        // once the path is known (see the constructor), so it sits dead centre as the camera coasts at it.
        var space = new Stop
        {
            Position = SpaceEnd,
            Height = 88f,                          // metres ACROSS, out where there is room for it
            Caption = Text("credits.thanks", 58),
        };

        return [logo, nikitos, kiosk, akob, qaw, nikitab, ungman, dmitry, space];
    }

    /// <summary>
    /// Where the camera is and which way it faces, <paramref name="time"/> seconds into the roll. The position
    /// is a Catmull-Rom curve through the stops — smooth, and passing exactly through each — and the facing is
    /// the curve's own tangent, so the turn onto the next one is flown rather than cut.
    /// </summary>
    private (Vector3 Pos, Vector3 Right, Vector3 Up, Vector3 Fwd) CameraAt(double time)
    {
        // Never below the rooftops, whatever the curve wants: an overshoot that put the camera under the
        // steppe filled the screen with dirt for a whole leg.
        Vector3 pos = OnPath(time);
        pos.Y = MathF.Max(pos.Y, 9f);
        Vector3 fwd = OnPath(time + 0.35) - OnPath(time - 0.35);
        fwd = fwd.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(fwd);
        // Straight up (the climb into space) leaves nothing to cross with world up, so the path's own sideways
        // run stands in for it there.
        Vector3 reference = MathF.Abs(fwd.Y) > 0.985f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, reference));
        return (pos, right, Vector3.Cross(right, fwd), fwd);
    }

    /// <summary>The flight's position at a time in seconds, clamped at both ends so the tangent can be taken
    /// either side of the start and the finish.</summary>
    private Vector3 OnPath(double time)
    {
        int legs = Path.Length - 1;
        double u = Math.Clamp(time / Duration, 0, 1) * legs;
        int i = Math.Clamp((int)u, 0, legs - 1);
        float f = (float)(u - i);
        Vector3 p0 = Path[Math.Max(i - 1, 0)];
        Vector3 p1 = Path[i];
        Vector3 p2 = Path[Math.Min(i + 1, Path.Length - 1)];
        Vector3 p3 = Path[Math.Min(i + 2, Path.Length - 1)];
        float f2 = f * f, f3 = f2 * f;
        return 0.5f * (2f * p1 + (-p0 + p2) * f
                       + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2
                       + (-p0 + 3f * p1 - 3f * p2 + p3) * f3);
    }

    /// <summary>Paints the scene and everyone in it over <paramref name="destination"/>.</summary>
    public void Render(Rect destination, double time)
    {
        (Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd) = CameraAt(time);

        SetShaderValue(Shader, LocationTime, (float)time, UniformType.Float);
        SetShaderValue(Shader, LocationResolution, destination.Size, UniformType.Vec2);
        SetShaderValue(Shader, LocationPos, pos, UniformType.Vec3);
        SetShaderValue(Shader, LocationRight, right, UniformType.Vec3);
        SetShaderValue(Shader, LocationUp, up, UniformType.Vec3);
        SetShaderValue(Shader, LocationFwd, fwd, UniformType.Vec3);
        BeginShaderMode(Shader);
        DrawTexturePro(Blank, Helper.GetFullSource(Blank), destination, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();

        // Farthest first: the cast shares no depth buffer with the raymarched world, so it is composited over
        // it in painter's order. They stand in open ground, where nothing in the world is in front of them.
        var order = new List<(float Depth, Stop Stop)>();
        foreach (Stop stop in Stops)
        {
            float depth = Vector3.Dot(stop.Position - pos, fwd);
            if (depth > 1.5f)
                order.Add((depth, stop));
        }
        order.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach ((_, Stop stop) in order)
            DrawStop(stop, destination, pos, right, up, fwd);
    }

    /// <summary>Projects a world point onto the target, with how many pixels a metre is at that depth.</summary>
    private static (Vector2 Screen, float PixelsPerMetre, float Depth) Project(Vector3 world, Rect destination,
        Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd)
    {
        Vector3 v = world - pos;
        float depth = MathF.Max(Vector3.Dot(v, fwd), 0.01f);
        // The shader builds its ray as sp.x * right - sp.y * up + FOCAL * fwd, with sp measured in units of
        // half the target's HEIGHT. This is that, inverted.
        float sx = Focal * Vector3.Dot(v, right) / depth;
        float sy = -Focal * Vector3.Dot(v, up) / depth;
        return (new Vector2(destination.X + destination.Width * 0.5f + sx * destination.Height,
                            destination.Y + destination.Height * 0.5f + sy * destination.Height),
                Focal * destination.Height / depth, depth);
    }

    private void DrawStop(Stop stop, Rect destination, Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd)
    {
        (Vector2 screen, float ppm, float depth) = Project(stop.Position, destination, pos, right, up, fwd);
        // Fade in as it comes up out of the haze and out again in the last few metres before the camera goes
        // through it, so a leg does not end on a wall of pixels across the whole frame.
        float alpha = Math.Clamp((190f - depth) / 70f, 0f, 1f) * Math.Clamp((depth - 4f) / 11f, 0f, 1f);
        if (stop.Position == SpaceWordsAt)
            alpha = Math.Clamp((520f - depth) / 240f, 0f, 1f);   // and stay up until the screen goes
        if (alpha <= 0.004f)
            return;
        var tint = Rgba.White with { A = (byte)(alpha * 255) };

        float h = stop.Height * ppm;
        BasicTexture art = stop.IsNamePlate ? UngManName.Texture : stop.Art;
        if (art.Width > 0)
        {
            Rect source = stop.IsNamePlate ? Helper.GetFullSource(art) : stop.Source;
            // A name is as wide as the stop says and as tall as it needs; a person is the other way round.
            float w = stop.IsNamePlate ? stop.Height * ppm : h * source.Width / source.Height;
            float th = stop.IsNamePlate ? w * source.Height / source.Width : h;
            DrawTexturePro(art, source, new Rect(screen.X - w / 2f, screen.Y - th / 2f, w, th),
                Vector2.Zero, 0, tint);
            h = th;
        }

        if (stop.Prop is { } prop)
        {
            (Vector2 ps, float propPpm, _) = Project(stop.Position + stop.PropOffset, destination,
                pos, right, up, fwd);
            float ph = stop.PropHeight * propPpm, pw = ph * prop.Width / prop.Height;
            DrawTexturePro(prop, Helper.GetFullSource(prop),
                new Rect(ps.X - pw / 2f, ps.Y - ph / 2f, pw, ph), Vector2.Zero, 0, tint);
        }

        if (stop.Caption is not { } caption)
            return;
        BasicTexture text = caption.Texture;
        bool captionIsTheStop = art.Width <= 0;
        // A stop that is nothing but words IS its caption, so that stands where the picture would have; one
        // under a picture is a plate at its feet.
        float cw, ch;
        if (captionIsTheStop)
        {
            cw = stop.Height * ppm;               // Height is the line's WIDTH on a words-only stop
            ch = cw * text.Height / text.Width;
        }
        else
        {
            ch = stop.Height * 0.30f * ppm;
            cw = ch * text.Width / text.Height;
        }
        float y = captionIsTheStop ? screen.Y - ch / 2f : screen.Y + h * 0.54f;
        DrawTexturePro(text, Helper.GetFullSource(text),
            new Rect(screen.X - cw / 2f, y, cw, ch), Vector2.Zero, 0, tint);
    }

    public void Dispose()
    {
        foreach (Stop stop in Stops)
            if (stop.Caption is { } caption)
                UnloadRenderTexture(caption);
        UnloadRenderTexture(UngManName);
    }
}
