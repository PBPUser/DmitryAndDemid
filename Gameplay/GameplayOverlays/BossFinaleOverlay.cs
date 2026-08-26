using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

/// <summary>The two ways a beaten boss leaves the screen.</summary>
public enum BossFinaleKind
{
    /// <summary>It gets away: rises off the top of the playfield, spinning, shrinking and fading as it goes.</summary>
    Retreat,
    /// <summary>It does not: the sprite flashes, tears into nine pieces and throws them apart.</summary>
    Destruct,
}

/// <summary>
/// The beat between a boss dying and the chapter wrapping up. The boss used to blink out of existence in a
/// single frame; now it gets a send-off — one of two exits (see <see cref="BossFinaleKind"/>) under a shower of
/// glowing motes, with the whole game dropped into slow motion around it (that part is
/// <see cref="GameBox.TimeScale"/>, not this class).
///
/// The boss object itself is removed from the simulation the moment it dies, exactly as before — nothing about
/// collision, culling or the chapter clock changes. What this overlay holds is a SNAPSHOT of how the boss was
/// last drawn (texture, source frame, destination, pivot, angle), which it then animates on its own. Overlays
/// composite over the playfield at the same coordinates, so the hand-off is invisible: the boss appears to
/// carry straight on from where it was standing.
///
/// Two clocks are in play on purpose:
/// • the finale's LENGTH is real time, so the beat is always the same 1.5 seconds however slow the game is;
/// • the motes MOVE on box time, which is the clock being slowed — so they visibly drift through the slowdown
///   rather than falling at normal speed inside it.
/// </summary>
public class BossFinaleOverlay : GameplayOverlay
{
    // Length is box-time and only bounds the base class's own removal; this overlay retires itself off the real
    // clock in Draw. Generous enough that the base can never pull it out from under the animation.
    public BossFinaleOverlay(GameBox box, RuntimeObject boss, BossFinaleKind kind, float realDuration)
        : base(box, 0.1f, 30f)
    {
        Kind = kind;
        RealDuration = realDuration;
        StartedReal = GetTime();
        LastBoxTime = box.GetTime();

        // Snapshot of the boss's last draw, mirroring GameBox's object pass: the destination rect is the pivot
        // point plus size, the origin is the pivot within it, and the entrance scale multiplies both.
        Texture = boss.Texture;
        Source = boss.SourceRectangle;
        float entrance = boss.EntranceScale;
        Rect target = boss.TargetRectangle;
        Dest = target with { Width = target.Width * entrance, Height = target.Height * entrance };
        Origin = boss.Origin * entrance;
        Rotation = boss.RenderRotation * 180f / MathF.PI;

        Star = Runtime.CurrentRuntime.Textures["star.png"];
        StarSource = Helper.GetFullSource(Star);

        // A destruct throws everything out at once; a retreat leaves a trail behind it as it climbs, so its motes
        // are spawned a few at a time in Draw instead.
        if (Kind == BossFinaleKind.Destruct)
            SpawnMotes(MoteBudget, Dest.X, Dest.Y, 150f, -40f);
    }

    private readonly BossFinaleKind Kind;
    private readonly float RealDuration;
    private readonly double StartedReal;
    private double LastBoxTime;

    private readonly BasicTexture Texture;
    private readonly Rect Source;
    private readonly Rect Dest;
    private readonly Vector2 Origin;
    private readonly float Rotation;

    private readonly BasicTexture Star;
    private readonly Rect StarSource;

    /// <summary>How many motes the whole finale is allowed. Fixed, so a boss death costs the same on any machine.</summary>
    private const int MoteBudget = 64;
    private const float MoteGravity = 190f;   // playfield px per second², in the 384x448 space (times ScaleF below)

    private readonly List<Mote> Motes = new(MoteBudget);
    private int MotesSpawned;
    private int Seed = 1;

    /// <summary>One glowing speck: thrown out of the boss, pulled down, burning out as it falls.</summary>
    private struct Mote
    {
        public Vector2 Position, Velocity;
        public float Size, Age, Life, Rotation, Spin;
        public Rgba Color;
    }

    /// <summary>Deterministic 0..1 noise. A seeded walk rather than Random so a replay shows the same shower.</summary>
    private float Next()
    {
        Seed = Seed * 1664525 + 1013904223;
        return ((Seed >> 8) & 0xFFFF) / 65535f;
    }

    private void SpawnMotes(int count, float x, float y, float speed, float bias)
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        for (int i = 0; i < count && MotesSpawned < MoteBudget; i++)
        {
            MotesSpawned++;
            float angle = Next() * MathF.Tau;
            float power = (0.35f + Next() * 0.65f) * speed;
            Motes.Add(new Mote
            {
                // Spread across the boss's own footprint rather than a single point, so the shower has the
                // width of the thing it came out of.
                Position = new Vector2(x + (Next() - 0.5f) * Dest.Width * 0.8f,
                                       y + (Next() - 0.5f) * Dest.Height * 0.6f),
                Velocity = new Vector2(MathF.Cos(angle) * power, MathF.Sin(angle) * power + bias) * sf,
                Size = (3f + Next() * 6f) * sf,
                Life = 0.7f + Next() * 0.8f,
                Rotation = Next() * 360f,
                Spin = (Next() - 0.5f) * 420f,
                // Warm sparks with the odd cool one, so the shower has depth instead of reading as one colour.
                Color = Next() < 0.75f
                    ? new Rgba(255, (byte)(170 + Next() * 85), (byte)(60 + Next() * 90), 255)
                    : new Rgba((byte)(140 + Next() * 80), (byte)(220 + Next() * 35), 255, 255),
            });
        }
    }

    protected override void Draw()
    {
        float progress = (float)((GetTime() - StartedReal) / RealDuration);
        if (progress >= 1f)
        {
            Box.RemoveOverlay(this);
            return;
        }

        // Motes are integrated on the BOX clock — the one the finale slows — so they drift through the slow
        // motion instead of falling past it. Clamped because a pause (or the very first frame) leaves a gap
        // that must not be spent in one step.
        double boxNow = Box.GetTime();
        float boxDelta = (float)Math.Clamp(boxNow - LastBoxTime, 0, 0.1);
        LastBoxTime = boxNow;

        if (Kind == BossFinaleKind.Retreat)
            DrawRetreat(progress, boxDelta);
        else
            DrawDestruct(progress);

        AdvanceMotes(boxDelta);
        DrawMotes();
    }

    /// <summary>
    /// It gets away. The boss climbs off the top of the playfield on an accelerating curve, rocking as it goes,
    /// shrinking with distance and fading out over the back half — shedding motes the whole way, which is what
    /// makes the exit read as damaged rather than as a cutscene.
    /// </summary>
    private void DrawRetreat(float progress, float boxDelta)
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        float climb = progress * progress;                       // slow to leave, then gone
        float y = Dest.Y - climb * (Dest.Y + Dest.Height + 96f * sf);
        float x = Dest.X + MathF.Sin(progress * 9f) * 10f * sf;
        float shrink = 1f - climb * 0.45f;
        float fade = 1f - Math.Clamp((progress - 0.55f) / 0.45f, 0f, 1f);

        // A trail: a couple of motes a frame, dropped where the boss is NOW, thrown gently downward so they
        // fall away behind it.
        if (boxDelta > 0f)
            SpawnMotes(2, x, y, 45f, 30f);

        Rect dest = new(x, y, Dest.Width * shrink, Dest.Height * shrink);
        DrawTexturePro(Texture, Source, dest, Origin * shrink,
            Rotation + MathF.Sin(progress * 7f) * 14f, Rgba.White with { A = (byte)(255 * fade) });
    }

    /// <summary>
    /// It does not. The sprite whites out and shudders in place for the first fifth, then tears into a 3x3 grid
    /// of pieces that fly apart, spinning and fading. Each piece is a crop of the SAME source frame drawn at its
    /// own place in the destination, so the boss comes apart along its own art with nothing authored per boss.
    /// </summary>
    private void DrawDestruct(float progress)
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        const float ShudderUntil = 0.2f;

        if (progress < ShudderUntil)
        {
            // Held together, barely: a hard shake and a wash to white as it goes.
            float t = progress / ShudderUntil;
            float shake = (1f - t) * 5f * sf;
            Vector2 jitter = new(MathF.Sin(progress * 190f) * shake, MathF.Cos(progress * 173f) * shake);
            Rgba tint = Helper.Mix(Rgba.White, new Rgba(255, 245, 210), t);
            DrawTexturePro(Texture, Source, Dest with { X = Dest.X + jitter.X, Y = Dest.Y + jitter.Y },
                Origin, Rotation, tint);
            return;
        }

        float burst = (progress - ShudderUntil) / (1f - ShudderUntil);
        float fade = 1f - burst * burst;
        const int Grid = 3;
        // Signed slice widths: a mirrored sprite has a negative source width, and stepping by the signed value
        // walks the frame the same way the whole-sprite draw does.
        float sw = Source.Width / Grid, sh = Source.Height / Grid;
        float dw = Dest.Width / Grid, dh = Dest.Height / Grid;
        // The grid is laid out in the sprite's OWN unrotated space and then turned about the pivot by the boss's
        // angle, so a boss that was drawn rotated still comes apart along its art rather than along the screen
        // axes — and the first frame of the burst lines up exactly with the last frame of the shudder.
        float radians = Rotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        Vector2 Turn(Vector2 v) => new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

        for (int gy = 0; gy < Grid; gy++)
        for (int gx = 0; gx < Grid; gx++)
        {
            Vector2 direction = new(gx - 1, gy - 1);
            // The middle piece has no outward direction of its own — send it up, so nothing hangs in place.
            direction = direction.LengthSquared() < 0.01f ? new Vector2(0, -1f) : Vector2.Normalize(direction);
            float reach = (70f + (gx * 3 + gy) * 9f) * sf * burst;
            float spin = ((gx * 3 + gy) % 2 == 0 ? 1f : -1f) * burst * 210f;

            // Where this cell's centre sits relative to the pivot, before any rotation.
            Vector2 local = new(gx * dw + dw / 2f - Origin.X, gy * dh + dh / 2f - Origin.Y);
            Vector2 centre = new Vector2(Dest.X, Dest.Y) + Turn(local) + Turn(direction) * reach;

            DrawTexturePro(Texture, new Rect(Source.X + gx * sw, Source.Y + gy * sh, sw, sh),
                new Rect(centre.X, centre.Y, dw, dh), new Vector2(dw / 2f, dh / 2f), Rotation + spin,
                Rgba.White with { A = (byte)(255 * fade) });
        }
    }

    private void AdvanceMotes(float delta)
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        for (int i = Motes.Count - 1; i >= 0; i--)
        {
            Mote m = Motes[i];
            m.Age += delta;
            if (m.Age >= m.Life)
            {
                Motes.RemoveAt(i);
                continue;
            }
            m.Velocity.Y += MoteGravity * sf * delta;
            m.Position += m.Velocity * delta;
            m.Rotation += m.Spin * delta;
            Motes[i] = m;
        }
    }

    /// <summary>Additive, so overlapping motes pile into a bloom rather than flat-shading over each other.</summary>
    private void DrawMotes()
    {
        if (Motes.Count == 0)
            return;
        BeginBlendMode(BlendMode.Additive);
        foreach (Mote m in Motes)
        {
            float left = 1f - m.Age / m.Life;
            float size = m.Size * (0.35f + left * 0.65f);
            DrawTexturePro(Star, StarSource,
                new Rect(m.Position.X, m.Position.Y, size, size),
                new Vector2(size / 2f, size / 2f), m.Rotation,
                m.Color with { A = (byte)(255 * left) });
        }
        EndBlendMode();
    }
}
