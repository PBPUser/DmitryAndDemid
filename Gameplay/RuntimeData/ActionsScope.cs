using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Collections.Frozen;
using System.Numerics;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Utils;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public static class ActionsScope
{
    public static FrozenDictionary<string, RuntimeChapterReferenceAction> ChapterActions;
    public static FrozenDictionary<string, RuntimeObjectReferenceAction> ObjectActions;

    // Yellow, brown, green-yellow — the palette for the two-toilet colour-spam spell (Hard & Max only).
    private static readonly int[] SpamColors = { 0xFFFF00, 0x8B4513, 0xADFF2F };

    // The Nikitab boss and its bullets live in stage 2's entity table at these indices (added to
    // Assets/Data/StagesJson/stage2.json). Kept as named constants so the boss action reads clearly.
    /// <summary>Radius (playfield px) of the ring the mystical toilet lays its swallowed hoard out on when it dies.</summary>
    private const float ToiletHoardRingRadius = 28f;

    // The toilet's visit is on a clock: it wanders for ToiletEscapeTick, then spends the last ~1.5s climbing off
    // the top of the screen, and is gone for good by ToiletLifetimeTick (12s at 60 TPS). The speed is sized to
    // clear the top edge inside that window from anywhere in its wander band (y 64..128).
    public const int ToiletLifetimeTick = 720;
    public const int ToiletEscapeTick = 630;
    private const float ToiletEscapeSpeed = 4.5f, ToiletEscapeAcceleration = 0.06f;

    private const int NikitabEntityIndex = 10;
    private const int NikitabMicroBulletIndex = 11;
    private const int NikitabPizzaIndex = 12;       // triangular pizza-slice sprite (visual only, not lethal)
    private const int NikitabPizzaGlowIndex = 13;   // orange round emit behind the slice — the lethal hitbox
    private const int NikitabZigzagIndex = 14;      // red zig-zag bullet

    // Nikita Bukin's act — the boss that arrives after the toilet spell. A BRAND-NEW boss (BossId 2) so it does
    // not inherit the toilet-act boss's state, plus the bullets his three cards use. All added to stage2.json.
    private const int NikitaBossIndex = 15;         // the Nikita Bukin boss (Visual "nikitab", BossId 2)
    private const int NikitaPizzaMountIndex = 16;   // the big pizza he rides in on (Visual "nikitab_pizza")
    private const int NikitaLargeBulletIndex = 17;  // large round bullet (colourable) — "large shots", yellow anchors
    private const int NikitaWatermelonIndex = 18;   // watermelon sprite bullet
    private const int NikitaShakeLineIndex = 19;    // descending bullet that wobbles side to side ("shaked lines")
    private const int NikitaPlainCircleIndex = 20;  // plain round bullet (colourable) for the spiral / rings
    private const int NikitaGrayPentaIndex = 21;    // pentabullet (colourable) — the gray rain / orange retargets
    private const int NikitaVioletMicroIndex = 11;  // reuse the existing micro visual for the Max crumble shards
    private const int NikitaBossId = 2;

    // A custom role tag kept in an otherwise-unused Header slot so the gray-penta reaction can recognise the
    // yellow "large" bullets it reacts to, and mark a penta that has already transformed (so it never re-reacts).
    private const int RoleHeaderIndex = 0x60;
    private const int RoleYellowLarge = 1;
    private const int RoleReactedPenta = 2;

    /// <summary>
    /// A cheap integer hash used to draw a pseudo-random value straight from the internal tick. Deterministic
    /// (same tick -> same value), so the Nikitab pattern is identical every run and stays replay-safe — unlike
    /// System.Random, whose seed would have to be threaded through the sim. Returns a non-negative int.
    /// </summary>
    /// <summary>
    /// Adds the boss "takes a spell card" splash overlay. Uses Dmitry's two-part animation when
    /// <paramref name="artKey"/> is "dmitry" and both halves exist; otherwise a single-art sweep of the given
    /// art. Nothing shows if the texture is missing, so a boss without splash art is simply silent (never throws).
    /// </summary>
    private static void ShowBossSplash(GameBox box, string artKey = "nikitab_dialog_art.png")
    {
        var tex = Runtime.CurrentRuntime.Textures;
        if (artKey == "dmitry" && tex.TryGetValue("dmitry_top.png", out var top)
                               && tex.TryGetValue("dmitry_bottom.png", out var bottom))
            box.AddOverlay(new GameplayOverlays.BossSplashOverlay(box, top, bottom, 2.2f));
        else if (tex.TryGetValue(artKey, out var art))
            box.AddOverlay(new GameplayOverlays.BossSplashOverlay(box, art, 2.0f));
    }

    private static int TickHash(int x)
    {
        unchecked
        {
            x = (x ^ 61) ^ (x >> 16);
            x += x << 3;
            x ^= x >> 4;
            x *= 0x27d4eb2d;
            x ^= x >> 15;
            return x & 0x7fffffff;
        }
    }

    /// <summary>
    /// The Nikitab boss: rains micro-bullets in bursts whose timing and directions come from the internal tick
    /// (TickHash), so it fires on an irregular, pseudo-random cadence rather than a fixed metronome.
    /// </summary>
    /// <summary>Ease-out elastic: 0 → overshoots past 1 → settles at 1. Gives the "budge out and in" pop.</summary>
    private static float EaseOutElastic(float x)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        const float c4 = 2f * MathF.PI / 3f;
        return MathF.Pow(2f, -10f * x) * MathF.Sin((x * 10f - 0.75f) * c4) + 1f;
    }

    private static readonly RuntimeObjectReferenceAction NikitabMicroSpam = c =>
    {
        // Entrance: over the first ~30 ticks Nikitab scales up with an elastic budge (overshoots out, settles
        // in) from nothing, so it "pops" into the fight.
        int age = c.Box.CurrentTick - c.Header[0x17];
        c.EntranceScale = age >= 30 ? 1f : EaseOutElastic(age / 30f);

        int tick = c.Box.ChapterTick;
        if (tick <= 30)   // brief grace while it budges in
        {
            c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
            return;
        }

        // Fire on roughly 1-in-8 ticks, chosen pseudo-randomly by the tick hash.
        if ((TickHash(tick) & 7) == 0)
        {
            int diff = Math.Clamp(c.Box.Difficulty, 0, 3);
            int count = 2 + TickHash(tick + 17) % (2 + diff);   // more bullets on harder tiers
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(NikitabMicroBulletIndex);
                b.X = c.X;
                b.Y = c.Y;
                float angle = TickHash(tick * 7 + k * 131) % 3600 / 3600f * (MathF.PI * 2f);
                b.FacingRotation = b.RenderRotation = angle;   // FacingRotation drives MoveLinearByDirection
                b.Speed = 1.8f + TickHash(tick + k * 53) % 100 / 100f * (1.2f + diff * 0.4f);
            }
        }
        c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
    };

    /// <summary>
    /// Nikitab's first non-spell (all difficulties): about every 2 s it lobs a pizza slice at the player — an
    /// orange round emit (the lethal hitbox) with the slice sprite drawn on top, both aimed where the player was.
    /// The bullets enlarge and recoil as they spawn (see the "nikitab#pizza#move" mover).
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitabNonspell1 = c =>
    {
        int born = c.Box.CurrentTick - c.Header[0x17];
        c.EntranceScale = born >= 30 ? 1f : EaseOutElastic(born / 30f);
        int tick = c.Box.ChapterTick;
        if (tick <= 30)
        {
            c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
            return;
        }
        if ((tick - 30) % 120 == 0)   // ~2 s between pizzas
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            float speed = 1.5f + Math.Clamp(c.Box.Difficulty, 0, 4) * 0.22f;
            var glow = c.Box.SpawnObject(NikitabPizzaGlowIndex, 0xFF8800);   // orange emit, drawn first (behind)
            var slice = c.Box.SpawnObject(NikitabPizzaIndex);                // slice sprite on top
            foreach (var b in new[] { glow, slice })
            {
                b.X = c.X;
                b.Y = c.Y;
                b.FacingRotation = aim;
                b.Speed = speed;
                b.CreatedAt = c.Box.CurrentTick;
            }
        }
        c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
    };

    /// <summary>
    /// Nikitab's second non-spell (all difficulties): every ~3 s it fans TWO lasers around the player, and the
    /// whole time streams red bullets that zig-zag their way down (see the "nikitab#zigzag#move" mover).
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitabNonspell2 = c =>
    {
        int born = c.Box.CurrentTick - c.Header[0x17];
        c.EntranceScale = born >= 30 ? 1f : EaseOutElastic(born / 30f);
        int tick = c.Box.ChapterTick;
        if (tick <= 30)
        {
            c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
            return;
        }
        int t = tick - 30;
        if (t % 180 == 0)   // two lasers, fanned around the player, every ~3 s
        {
            float baseAim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            for (int s = -1; s <= 1; s += 2)
            {
                var laser = RuntimeObject.MakeLaser(c.Box, c.Position, baseAim + s * 0.32f, 560f, 10f, 45, 90, 20);
                c.Box.AddObject(laser);
            }
        }
        if (t % 12 == 0)   // steady red zig-zag stream at the player
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            var b = c.Box.SpawnObject(NikitabZigzagIndex, 0xFF0000);
            b.X = c.X;
            b.Y = c.Y;
            b.FacingRotation = aim;
            b.Speed = 1.9f + Math.Clamp(c.Box.Difficulty, 0, 4) * 0.18f;
            b.CreatedAt = c.Box.CurrentTick;
        }
        c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
    };

    // ============================ Nikita Bukin's act (the second half of stage 2) ============================

    /// <summary>
    /// The entrance: while the boss is still riding up on the pizza (move-to-target in flight) it renders half
    /// transparent; the moment it settles at its post it "solidifies" from half to full opacity over ~18 ticks.
    /// The pizza itself is handled by the "nikitab#pizzamount#move" mover, which glues to the boss then drops.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaAppear = c =>
    {
        if ((c.Header[0] & RuntimeObject.FlagIsMovingToTarget) == RuntimeObject.FlagIsMovingToTarget)
        {
            c.RenderAlpha = 0.5f;                                   // riding in — see-through
            c.RenderRotation = MathF.Sin(c.Box.CurrentTick * 0.14f) * 0.14f;
        }
        else
        {
            c.FloatingPoints[0x32] = MathF.Min(18f, c.FloatingPoints[0x32] + 1f);
            float k = c.FloatingPoints[0x32] / 18f;
            c.RenderAlpha = 0.5f + 0.5f * k;                        // solidify into the fight
            c.RenderRotation = MathF.Sin(c.Box.CurrentTick * 0.1f) * 0.06f * (1f - k);
        }
    };

    /// <summary>
    /// Nikita's first non-spell: a steadily-rotating spiral of small bullets, with large shots aimed straight at
    /// the player on a slower beat. On Hard &amp; Max he also fires a laser every ~2 s, each with a ~1 s telegraph.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaSpiral = c =>
    {
        c.RenderAlpha = 1f;
        int tick = c.Box.ChapterTick;
        if (tick <= 20) { c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.2f; return; }
        int t = tick - 20;
        int diff = Math.Clamp(c.Box.Difficulty, 0, 3);

        if (t % 3 == 0)   // the spiral itself: base angle advances each emit, tracing sweeping arms
        {
            int arms = 2 + (diff >= 2 ? 1 : 0);
            float baseAng = t * 0.19f;
            for (int a = 0; a < arms; a++)
            {
                var b = c.Box.SpawnObject(NikitaPlainCircleIndex, 0x33CCFF);
                b.X = c.X; b.Y = c.Y;
                b.FacingRotation = b.RenderRotation = baseAng + a * (MathF.PI * 2f / arms);
                b.Speed = 2.0f + diff * 0.15f;
            }
        }
        if (t % 68 == 0)   // large shot straight at the player
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            var big = c.Box.SpawnObject(NikitaLargeBulletIndex, 0xFF5533);
            big.X = c.X; big.Y = c.Y; big.FacingRotation = aim; big.Speed = 2.6f + diff * 0.2f;
        }
        if (diff >= 2 && t % 120 == 0)   // Hard & Max: laser every 2 s, 1 s (60-tick) telegraph before it bites
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            var laser = RuntimeObject.MakeLaser(c.Box, c.Position, aim, 560f, 12f, 60, 75, 20);
            c.Box.AddObject(laser);
        }
        c.RenderRotation = MathF.Sin(tick * 0.08f) * 0.12f;
    };

    /// <summary>
    /// Nikita's first spell: watermelons lobbed at the player, a rotating ring of round bullets, and descending
    /// columns of bullets that wobble ("shaked lines") — 3 / 4 / 6 / 8 columns by difficulty. On Hard &amp; Max,
    /// large shots also drop from random x positions seeded off the player's position.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaWatermelon = c =>
    {
        c.RenderAlpha = 1f;
        int tick = c.Box.ChapterTick;
        if (tick <= 20) { c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.2f; return; }
        int t = tick - 20;
        int diff = Math.Clamp(c.Box.Difficulty, 0, 3);

        if (t % 46 == 0)   // watermelons at the player
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            for (int k = -1; k <= 1; k++)
            {
                var w = c.Box.SpawnObject(NikitaWatermelonIndex, 0x4CAF50);   // watermelon-green disc + shaking-line effect
                w.X = c.X; w.Y = c.Y;
                w.FacingRotation = w.RenderRotation = aim + k * 0.16f;
                w.Speed = 2.0f + diff * 0.12f;
            }
        }
        if (t % 84 == 0)   // rotating "bullet-circle" ring
        {
            int ring = 12 + diff * 4;
            float off = t * 0.045f;
            for (int k = 0; k < ring; k++)
            {
                var b = c.Box.SpawnObject(NikitaPlainCircleIndex, 0x66DD55);
                b.X = c.X; b.Y = c.Y;
                b.FacingRotation = b.RenderRotation = off + k * (MathF.PI * 2f / ring);
                b.Speed = 1.9f;
            }
        }
        int lines = diff == 0 ? 3 : diff == 1 ? 4 : diff == 2 ? 6 : 8;   // shaked lines, difficulty-dependent
        if (t % 22 == 0)
        {
            for (int L = 0; L < lines; L++)
            {
                float px = 28f + L * (328f / MathF.Max(1, lines - 1));
                var b = c.Box.SpawnObject(NikitaShakeLineIndex, 0xFF5599);
                b.X = px; b.Y = 6;
                b.FacingRotation = MathF.PI / 2f;
                b.Speed = 2.0f;
                b.CreatedAt = c.Box.CurrentTick;
                b.FloatingPoints[0x30] = L * 0.7f;   // per-column shake phase
            }
        }
        if (diff >= 2 && t % 38 == 0)   // Hard & Max: large shots from random x, seeded off the player's position
        {
            int seed = (int)(c.Box.Player.X * 7f + c.Box.Player.Y * 13f) + t;
            int rx = TickHash(seed) % 360 + 12;
            var big = c.Box.SpawnObject(NikitaLargeBulletIndex, 0xFFCC33);
            big.X = rx; big.Y = 4;
            big.FacingRotation = MathF.PI / 2f;
            big.Speed = 2.4f;
        }
        c.RenderRotation = MathF.Sin(tick * 0.08f) * 0.1f;
    };

    /// <summary>
    /// Nikita's last spell: big slow yellow bullets (tagged as "anchors") drift outward while gray pentabullets
    /// rain from the top. The reaction lives in "nikitab#graypenta#move": a gray penta near a yellow anchor
    /// retargets at the player and turns orange (Normal+); on Max, one that touches an anchor shatters into
    /// little violet microbullets.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaYellow = c =>
    {
        c.RenderAlpha = 1f;
        int tick = c.Box.ChapterTick;
        if (tick <= 20) { c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.2f; return; }
        int t = tick - 20;
        int diff = Math.Clamp(c.Box.Difficulty, 0, 3);

        if (t % 64 == 0)   // yellow "large" anchors
        {
            int count = 2 + diff / 2;
            float off = t * 0.02f;
            for (int k = 0; k < count; k++)
            {
                var y = c.Box.SpawnObject(NikitaLargeBulletIndex, 0xFFD400);
                y.X = c.X; y.Y = c.Y;
                y.FacingRotation = y.RenderRotation = off + k * (MathF.PI * 2f / count);
                y.Speed = 0.85f;
                y.Header[RoleHeaderIndex] = RoleYellowLarge;
            }
        }
        if (t % 6 == 0)   // gray penta rain
        {
            int rx = TickHash(t * 31 + 5) % 372 + 6;
            var g = c.Box.SpawnObject(NikitaGrayPentaIndex, 0x888888);
            g.X = rx; g.Y = 2;
            g.FacingRotation = MathF.PI / 2f;                       // travels straight down
            g.RenderRotation = MathF.PI / 2f + MathF.PI;            // but the sprite is flipped 180° (points up)
            g.Speed = 2.2f + diff * 0.2f;
            g.CreatedAt = c.Box.CurrentTick;
        }
        c.RenderRotation = MathF.Sin(tick * 0.08f) * 0.1f;
    };

    /// <summary>
    /// Nikita's FINAL card (last spell of stage 2). He lobs slow "huge" bullets at the player; each huge bullet
    /// rotates 180° and sprays yellow microbullets facing its turning direction (see <see cref="NikitaFinalHuge"/>).
    /// On Hard/Max he also sends a stream of bullets rising up from the bottom edge, so the player is pressured
    /// from both sides.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaFinal = c =>
    {
        c.RenderAlpha = 1f;
        int tick = c.Box.ChapterTick;
        if (tick <= 20) { c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.2f; return; }
        int t = tick - 20;
        int diff = Math.Clamp(c.Box.Difficulty, 0, 3);

        // A "huge" bullet aimed at the player, which becomes a microbullet emitter (its UpdateAction is swapped).
        if (t % 110 == 0)
        {
            var huge = c.Box.SpawnObject(NikitaLargeBulletIndex, 0xFFC400);
            huge.X = c.X; huge.Y = c.Y;
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            huge.FacingRotation = huge.RenderRotation = aim;
            huge.Speed = 1.05f + diff * 0.05f;
            huge.CreatedAt = c.Box.CurrentTick;
            huge.UpdateAction = NikitaFinalHuge;
        }

        // Hard/Max only: bullets rising from the bottom edge (playfield is 384x448; +Y is down, so -PI/2 is up).
        if (diff >= 2 && t % 16 == 0)
        {
            int rx = TickHash(t * 17 + 3) % 372 + 6;
            var b = c.Box.SpawnObject(NikitaPlainCircleIndex, 0x66CCFF);
            b.X = rx; b.Y = 446;
            b.FacingRotation = b.RenderRotation = -MathF.PI / 2f;
            b.Speed = 1.8f + diff * 0.2f;
        }
        c.RenderRotation = MathF.Sin(tick * 0.08f) * 0.1f;
    };

    /// <summary>
    /// The "huge" bullet of the final card. It ROTATES 180° over its first N = 30 + 10*(4-difficulty) ticks while
    /// travelling along its original heading, and each tick of that turn it sheds a yellow microbullet facing its
    /// CURRENT rotation. The microbullets then fly straight (their template's MoveLinearByDirection), so the
    /// spray fans out across the half-turn. Removes itself once well off the playfield.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaFinalHuge = obj =>
    {
        int diff = Math.Clamp(obj.Box.Difficulty, 0, 3);
        float n = 30 + 10 * (4 - diff);                               // half-turn duration: easy 70 .. max 40 ticks
        int age = obj.Box.CurrentTick - obj.CreatedAt;
        float k = Math.Clamp(age / n, 0f, 1f);
        float start = obj.FacingRotation;                            // fixed original heading (set at spawn)
        float rot = start + MathF.PI * k;                            // the huge bullet's rotation sweeps 180°
        obj.RenderRotation = rot;
        obj.Position += Helper.GetDirection(start) * obj.Speed;      // travels along its original heading

        // Shed a microbullet facing the huge bullet's CURRENT rotation; it flies straight from there.
        if (age <= n && age % 3 == 0)
        {
            var m = obj.Box.SpawnObject(NikitabMicroBulletIndex, 0xFFE000);
            m.X = obj.X; m.Y = obj.Y;
            m.FacingRotation = m.RenderRotation = rot;
            m.Speed = 1.6f;                                          // straight, via the micro template's mover
        }

        if (obj.X < -24 || obj.X > 408 || obj.Y < -24 || obj.Y > 472)
            obj.Box.RemoveObject(obj);
    };

    /// <summary>Stage-3 pizza formation: centre it rotates about, the off-screen start radius and the small
    /// on-screen ("orange") end radius, how long the inward contraction takes, and the spin rate.</summary>
    private static readonly Vector2 PizzaCenter = new Vector2(192, 210);
    private const float PizzaRStart = 360f;              // spawns off-screen (bigger than the playfield)
    private const float PizzaREnd = 110f;                // settles to the small on-screen circle
    private const int PizzaContractTicks = 300;          // ticks for the first inward move
    private const float PizzaSpin = 0.012f;

    /// <summary>
    /// Stage 3, Nikita's "rotating pizza" spell. A whole circular FORMATION of bullets is spawned at once (in the
    /// create script): a green ring drawn out of bullets plus red inner dots (micro / default / huge). It spawns
    /// off-screen at a big radius and contracts inward to the small on-screen circle over the first
    /// <see cref="PizzaContractTicks"/> ticks, then breathes back out and in on a loop — and the ENTIRE formation
    /// (ring and inner dots) rotates about the centre the whole time. The boss just idles at the centre; the
    /// bullets drive themselves (see <see cref="NikitaPizzaFormationBullet"/>). Nikitab is invincible; on
    /// Hard/Max a laser sweeps from the centre (spawned in the create script).
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaStage3Pizza = c =>
    {
        c.RenderAlpha = 1f;
        int tick = c.Box.ChapterTick;
        c.RenderRotation = MathF.Sin(tick * 0.06f) * 0.15f;   // gentle idle wobble at the centre
    };

    /// <summary>Boss behaviour for the stage-3 first spellcard: idles in place, and drops its invincibility the
    /// instant the player dies or bombs (IsFailed covers both and is reset at chapter start).</summary>
    private static readonly RuntimeObjectReferenceAction NikitaStage3LaserBoss = c =>
    {
        c.RenderAlpha = 1f;
        c.RenderRotation = MathF.Sin(c.Box.ChapterTick * 0.06f) * 0.12f;   // gentle idle wobble
        if (c.Box.IsFailed)
            c.Header[0] &= ~RuntimeObject.FlagInvincible;
    };

    /// <summary>Nikitab's stage-3 sweep laser: rotates its beam clockwise every tick (increasing angle = clockwise
    /// in the y-down playfield) so it sweeps around the boss, and removes itself at the end of its life.</summary>
    private const float NikitaStage3SweepSpeed = 0.013f;
    private static readonly RuntimeObjectReferenceAction NikitaStage3SweepLaser = laser =>
    {
        laser.RenderRotation += NikitaStage3SweepSpeed;   // clockwise
        if (laser.Box.CurrentTick - laser.CreatedAt >= laser.LaserLifetime)
            laser.Box.RemoveObject(laser);
    };

    /// <summary>The current formation radius at chapter tick <paramref name="t"/>: starts off-screen, eases to the
    /// small circle over PizzaContractTicks, then breathes back out/in on a cosine loop.</summary>
    private static float PizzaFormationRadius(int t)
    {
        float phase = MathF.Cos(MathF.PI * t / PizzaContractTicks);   // +1 at t=0 (off-screen) -> -1 at t=300 (small)
        return PizzaREnd + (PizzaRStart - PizzaREnd) * (0.5f + 0.5f * phase);
    }

    /// <summary>
    /// One bullet of the stage-3 pizza formation. Its slot on the circle (base angle + radius fraction) is fixed;
    /// each tick it sits at centre + dir(baseAngle + spin) * (frac * R(t)), so the whole formation rotates and
    /// contracts/expands together. Purely a function of the chapter tick and its stored slot — replay-safe, no
    /// per-bullet state.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitaPizzaFormationBullet = obj =>
    {
        int t = obj.Box.ChapterTick;
        float ang = obj.FloatingPoints[0x30] + t * PizzaSpin;   // base angle + global spin
        float frac = obj.FloatingPoints[0x31];                  // 1 = on the ring, <1 = an inner dot
        obj.Position = PizzaCenter + Helper.GetDirection(ang) * (frac * PizzaFormationRadius(t));
        obj.RenderRotation = ang;
    };

    /// <summary>
    /// Spawns the whole stage-3 pizza formation once: a green ring of micro bullets (frac = 1) plus red inner
    /// dots — a mix of micro/default/huge bullets at deterministic random angles and radii. Every bullet is given
    /// the <see cref="NikitaPizzaFormationBullet"/> mover, so they rotate and contract as one.
    /// </summary>
    private static void SpawnPizzaFormation(GameBox box, int diff)
    {
        const int ring = 32;
        for (int i = 0; i < ring; i++)
        {
            var b = box.SpawnObject(NikitabMicroBulletIndex, 0x55DD55);   // green ring (the pizza outline)
            float a = i * (MathF.PI * 2f / ring);
            b.FloatingPoints[0x30] = a;
            b.FloatingPoints[0x31] = 1f;
            b.UpdateAction = NikitaPizzaFormationBullet;
            b.PersistOffscreen = true;                                   // spawns off-screen — must survive the cull
            b.Position = PizzaCenter + Helper.GetDirection(a) * PizzaRStart;
        }
        int inner = 10 + diff * 2;
        for (int i = 0; i < inner; i++)
        {
            int which = i % 3;   // 0 = huge, 1 = micro, 2 = default — "micro/default and huge bullets"
            int index = which == 0 ? NikitaLargeBulletIndex : which == 1 ? NikitabMicroBulletIndex : NikitaPlainCircleIndex;
            var b = box.SpawnObject(index, 0xFF3333);                    // red inner dots
            float a = TickHash(i * 733 + 11) % 3600 / 3600f * (MathF.PI * 2f);
            float frac = 0.2f + TickHash(i * 977 + 3) % 1000 / 1000f * 0.65f;
            b.FloatingPoints[0x30] = a;
            b.FloatingPoints[0x31] = frac;
            b.UpdateAction = NikitaPizzaFormationBullet;
            b.PersistOffscreen = true;                                   // also starts off-screen while contracting
            b.Position = PizzaCenter + Helper.GetDirection(a) * (frac * PizzaRStart);
        }
    }

    /// <summary>Toilet behaviour for that spell: fires a slowly-rotating ring of bullets, cycling the palette.</summary>
    private static readonly RuntimeObjectReferenceAction ColorSpamToilet = obj =>
    {
        if (obj.Box.ChapterTick < 30 || obj.Box.ChapterTick % 20 != 0)
            return;
        const int ring = 10;
        int color = SpamColors[(obj.Box.ChapterTick / 20) % SpamColors.Length];
        float baseAngle = obj.Box.ChapterTick * 0.12f;
        for (int k = 0; k < ring; k++)
        {
            var b = obj.Box.SpawnObject(0, color);
            b.X = obj.X;
            b.Y = obj.Y;
            b.FacingRotation = b.RenderRotation = baseAngle + k * (MathF.PI * 2f / ring);
            b.Speed = 2.2f;
        }
    };

    // Stage 1's bullet-entity slots used by the nikitos non-spell attacks below.
    private const int NikitosNonspellPentaIndex = 8;    // pentabullet
    private const int NikitosNonspellLightIndex = 10;   // light bullet (added to stage1.json)
    private const int Nokia8EntityIndex = 11;           // Nokia 8 turret (added to stage1.json)

    /// <summary>
    /// Nikitos first non-spell: streams pentabullets aimed at the player, and on a tick-driven pseudo-random
    /// cadence throws light circle bullets out in every direction.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitosNonspell1 = c =>
    {
        int tick = c.Box.ChapterTick;
        if (tick <= 0)
        {
            c.RenderRotation = 0;
            return;
        }
        int diff = Math.Clamp(c.Box.Difficulty, 0, 3);

        // Aimed pentabullets at the player, faster/wider with difficulty. (Tuned up: quicker cadence, a wider
        // fan of more bullets and a touch more speed, so the non-spell actually pressures the player.)
        if (tick % Math.Max(6, 22 - diff * 5) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 2 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(NikitosNonspellPentaIndex);
                b.X = c.X;
                b.Y = c.Y;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.19f;
                b.Speed = 3.3f + diff * 0.5f;
            }
        }

        // Random light circle bullets, direction and timing straight from the tick hash — now twice as dense
        // and spat out in opposing pairs so the whole field fills in faster.
        if ((TickHash(tick) & 7) == 0)
        {
            float ang = TickHash(tick * 3) % 3600 / 3600f * (MathF.PI * 2f);
            for (int s = 0; s < 2; s++)
            {
                var b = c.Box.SpawnObject(NikitosNonspellLightIndex);
                b.X = c.X;
                b.Y = c.Y;
                b.FacingRotation = b.RenderRotation = ang + s * MathF.PI;   // opposite directions
                b.Speed = 1.6f + diff * 0.4f;
            }
        }
        c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
    };

    /// <summary>Nikitos second non-spell: the same attack, plus a steady rain of light bullets from the top.</summary>
    private static readonly RuntimeObjectReferenceAction NikitosNonspell2 = c =>
    {
        NikitosNonspell1(c);
        int tick = c.Box.ChapterTick;
        // A heavier, faster rain: more frequent (every 3 ticks) and two columns per drop, dropping quicker.
        if (tick > 0 && tick % 3 == 0)
        {
            int diff = Math.Clamp(c.Box.Difficulty, 0, 3);
            for (int s = 0; s < 2; s++)
            {
                var b = c.Box.SpawnObject(NikitosNonspellLightIndex);
                b.X = TickHash(tick * 7 + s * 131) % 384;
                b.Y = 0;
                b.FacingRotation = b.RenderRotation = MathF.PI / 2f;   // straight down
                b.Speed = 2.6f + diff * 0.5f;
            }
        }
    };

    /// <summary>How many points along the top of the playfield the midboss rain falls from.</summary>
    private const int NikitosMidbossEmitters = 3;

    /// <summary>
    /// Stage 1's midboss non-spell: nikitos turns up halfway through the level and drizzles light bullets from
    /// a few points strung across the top of the playfield.
    ///
    /// The emitters are positions, not objects — each is a sine of the chapter tick on its own phase, so they
    /// slide past each other and the columns never settle into fixed lanes the player can just stand between.
    /// Deriving them from the tick rather than spawning carrier entities keeps the whole attack replay-safe
    /// (nothing to desync) and costs the stage no entity-table slots.
    ///
    /// Deliberately the lightest thing in the stage: it opens the level, it is over in twenty seconds, and it
    /// sits before a player has any power. Slow bullets, a wide cadence, and only a few degrees of spread — the
    /// pressure comes later, from the two non-spells at the boss.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction NikitosMidbossRain = c =>
    {
        int tick = c.Box.ChapterTick;
        if (tick <= 0)
        {
            c.RenderRotation = 0;
            return;
        }
        int diff = Math.Clamp(c.Box.Difficulty, 0, 3);
        c.RenderRotation = MathF.Sin(tick * 0.07f) * 0.22f;

        if (tick % Math.Max(8, 17 - diff * 3) != 0)
            return;
        for (int e = 0; e < NikitosMidbossEmitters; e++)
        {
            float phase = tick * 0.013f + e * (MathF.PI * 2f / NikitosMidbossEmitters);
            var b = c.Box.SpawnObject(NikitosNonspellLightIndex);
            b.X = 192f + MathF.Sin(phase) * 150f;
            b.Y = 16f;
            // Straight down, give or take a couple of degrees — enough that the rain scatters instead of
            // falling in three clean columns.
            b.FacingRotation = b.RenderRotation =
                MathF.PI / 2f + (TickHash(tick * 31 + e * 17) % 100 - 50) / 500f;
            b.Speed = 1.4f + diff * 0.25f;
        }
    };

    // ---------------------------------------------------------------------------------------------------
    // EXTRA STAGE — Dmitry (three cards) and Demid (eight, the last two being the OBS logo and the window).
    // The entity table these index into is Assets/Data/StagesJson/extra1.json; the bosses are two distinct
    // BossIds so Demid's arrival can retire Dmitry the way stage 2 retires its first act.
    // ---------------------------------------------------------------------------------------------------
    private const int ExtraMicroIndex = 0;        // micro bullet
    private const int ExtraCircleIndex = 1;       // plain round bullet (colourable)
    private const int ExtraLargeIndex = 2;        // large round bullet (colourable)
    private const int ExtraPentaIndex = 3;        // pentabullet
    private const int ExtraRhombusIndex = 4;      // rhombus bullet
    private const int ExtraOvalIndex = 5;         // oval bullet
    private const int ExtraLightIndex = 6;        // light bullet
    private const int ExtraBubbleIndex = 7;       // bubble bullet
    private const int DmitryBossIndex = 8;        // Dmitry  (Visual "dmitry", BossId 0)
    private const int DemidBossIndex = 9;         // Demid   (Visual "demid",  BossId 1)
    private const int ExtraObsPixelIndex = 10;    // formation bullet held in the OBS logo
    private const int ExtraWindowPixelIndex = 11; // formation bullet held in the window frame
    private const int DmitryBossId = 0;
    private const int DemidBossId = 1;

    /// <summary>Where the OBS logo and the window are centred / anchored, in the 384x448 playfield.</summary>
    private static readonly Vector2 ObsLogoCenter = new(192, 224);

    /// <summary>
    /// Per-tick housekeeping every card boss shares (the Extra stage's two, and Dmitry's stage-3 act): the
    /// elastic pop-in over its first 30 ticks and a slow idle sway. Returns the chapter-local tick with
    /// <paramref name="graceTicks"/> subtracted — negative while the boss is still budging in, so a caller can
    /// simply bail on a negative value.
    /// </summary>
    private static int BossCardTick(RuntimeObject c, int graceTicks = 30)
    {
        int born = c.Box.CurrentTick - c.Header[0x17];
        c.EntranceScale = born >= 30 ? 1f : EaseOutElastic(born / 30f);
        int tick = c.Box.ChapterTick;
        c.RenderRotation = MathF.Sin(tick * 0.08f) * 0.22f;
        return tick - graceTicks;
    }

    /// <summary>Difficulty 0..4 — the campaign runs 0..3 (Easy..Max), Extra mode always plays at 4, and spell
    /// practice offers whichever tiers the card was authored for.</summary>
    private static int CardDiff(GameBox box) => Math.Clamp(box.Difficulty, 0, 4);

    /// <summary>Dmitry standing in place for his arrival chapter: he only talks, so all this does is pose him.</summary>
    private static readonly RuntimeObjectReferenceAction DmitryIdle = c => BossCardTick(c);

    /// <summary>Demid doing nothing but talking — the chapter between the OBS card and the last one.</summary>
    private static readonly RuntimeObjectReferenceAction DemidIdle = c => BossCardTick(c);

    /// <summary>
    /// Puts a boss at its post for a card and hands it that card's attack. <see cref="GameBox.SpawnObject"/>
    /// reuses a boss that is already on screen (reloading its template, so its health comes back full), which is
    /// what carries the same Dmitry / Demid through a whole act, card after card.
    /// </summary>
    private static RuntimeObject SpawnCardBoss(GameBox box, int index, RuntimeObjectReferenceAction attack,
        Vector2 post)
    {
        var boss = box.SpawnObject(index);
        boss.Position = post;
        boss.RenderAlpha = 1f;
        boss.Header[0] &= ~RuntimeObject.FlagInvincible;   // the template carries none; a previous card may have
        boss.UpdateAction = attack;
        return boss;
    }

    /// <summary>
    /// Shows or hides the boss health bar for the card about to start.
    /// <see cref="RuntimeObject.LoadFromFile"/> only creates a bar on a boss's FIRST spawn (and skips it
    /// entirely on a BossInvincible chapter), and every later card REUSES that same boss — so a boss keeps
    /// whatever bar it was born with for its whole act. That is wrong in both directions on the Extra stage:
    /// the survival card must not show one, and the finale right after it must.
    /// </summary>
    private static void SetBossHealthBar(GameBox box, RuntimeObject boss, bool visible)
    {
        var bars = new List<GameplayOverlays.BossHealthOverlay>();
        foreach (var overlay in box.GameplayOverlays)
            if (overlay is GameplayOverlays.BossHealthOverlay bar)
                bars.Add(bar);
        if (visible)
        {
            if (bars.Count == 0)
                box.AddOverlay(new GameplayOverlays.BossHealthOverlay(box, boss));
            return;
        }
        foreach (var bar in bars)
            box.RemoveOverlay(bar);
    }

    /// <summary>A BossId no boss carries, so <see cref="RetireOtherBosses"/> keeps none of them.</summary>
    private const int NoBossId = -1;

    /// <summary>
    /// Retires every boss but <paramref name="keepBossId"/> — flag it dead so its health bar self-removes, drop
    /// its lingering screen effects, and take it off the board. Used when Demid takes over from Dmitry, the same
    /// hand-off stage 2 does between its two acts, and with <see cref="NoBossId"/> to clear the board entirely
    /// when stage 1's midboss is done.
    /// </summary>
    private static void RetireOtherBosses(GameBox box, int keepBossId)
    {
        foreach (var old in box.BoxObjects)
        {
            if ((old.Header[0] & RuntimeObject.FlagIsBoss) != RuntimeObject.FlagIsBoss)
                continue;
            if (old.BossId == keepBossId)
                continue;
            old.Header[0] |= RuntimeObject.FlagIsDied;
            if (old.BackgroundDistortionEffect != null)
                box.RemoveScreenEffect(old.BackgroundDistortionEffect);
            if (old.BossCircleEffect != null)
                box.RemoveScreenEffect(old.BossCircleEffect);
            box.RemoveObject(old);
        }
    }

    /// <summary>
    /// Dmitry's first card, "the empty canister": rings of brown gas that puff out, STALL in mid-air — the
    /// magic is gone and nothing comes out with any pressure — and only then finally blow past the player,
    /// with aimed pentabullets in between.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryCard1 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(48, 96 - diff * 12) == 0)
        {
            int count = 10 + diff * 3;
            float baseAngle = TickHash(t) % 628 / 100f;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraOvalIndex, 0x8B5A2B);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = baseAngle + k * (MathF.PI * 2f / count);
                b.CreatedAt = c.Box.CurrentTick;
                b.UpdateAction = DmitryPuffMove;
            }
        }
        if (t % Math.Max(24, 48 - diff * 6) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 1 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraPentaIndex, 0xC8A165);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.16f;
                b.Speed = 2.6f + diff * 0.35f;
            }
        }
    };

    /// <summary>The stalling gas of Dmitry's first card: a weak puff, a long hang, then the real blast.</summary>
    private static readonly RuntimeObjectReferenceAction DmitryPuffMove = obj =>
    {
        int age = obj.Box.CurrentTick - obj.CreatedAt;
        float speed = age < 24 ? 2.3f : age < 60 ? 0.1f : 3.0f;
        obj.Position += Helper.GetDirection(obj.FacingRotation) * speed;
        obj.RenderRotation = obj.FacingRotation + MathF.Sin(age * 0.2f) * 0.3f;
    };

    /// <summary>
    /// Dmitry's second card, "pressure in the pipes": the castle's plumbing still works even if he does not —
    /// walls of gas fire in from both side edges at a height that sweeps up and down, and he drops a slow
    /// curtain of large bullets straight down the middle.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryCard2 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(5, 10 - diff) == 0)
        {
            float y = 72f + (MathF.Sin(t * 0.045f) * 0.5f + 0.5f) * 296f;
            for (int side = 0; side < 2; side++)
            {
                var b = c.Box.SpawnObject(ExtraCircleIndex, side == 0 ? 0x6B8E23 : 0x8B4513);
                b.X = side == 0 ? -8f : 392f;
                b.Y = y + (side == 0 ? 0f : 24f);
                b.FacingRotation = b.RenderRotation = side == 0 ? 0f : MathF.PI;
                b.Speed = 2.1f + diff * 0.28f;
            }
        }
        if (t % 90 == 0)
        {
            int count = 5 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraLargeIndex, 0xADFF2F);
                b.X = 32f + k * (320f / MathF.Max(1, count - 1));
                b.Y = -8f;
                b.FacingRotation = b.RenderRotation = MathF.PI / 2f;
                b.Speed = 1.5f + diff * 0.2f;
            }
        }
    };

    /// <summary>
    /// Dmitry's third card, "the last exhaust": a tight rotating spiral of heavy bullets, each of which bursts
    /// into a fan of micro shards partway across the screen — everything he has left, spent at once.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryCard3 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(4, 8 - diff) == 0)
        {
            int arms = 2 + diff / 2;
            for (int a = 0; a < arms; a++)
            {
                var b = c.Box.SpawnObject(ExtraLargeIndex, 0xA0522D);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = t * 0.21f + a * (MathF.PI * 2f / arms);
                b.Speed = 1.7f + diff * 0.16f;
                b.CreatedAt = c.Box.CurrentTick;
                b.UpdateAction = DmitryExhaustMove;
            }
        }
    };

    /// <summary>A heavy exhaust bullet: flies straight, then bursts into a fan of micro shards and is gone.</summary>
    private static readonly RuntimeObjectReferenceAction DmitryExhaustMove = obj =>
    {
        int age = obj.Box.CurrentTick - obj.CreatedAt;
        if (age >= 48)
        {
            int diff = CardDiff(obj.Box);
            int shards = 3 + diff;
            for (int k = 0; k < shards; k++)
            {
                var m = obj.Box.SpawnObject(ExtraMicroIndex, 0xFFD700);
                m.Position = obj.Position;
                m.FacingRotation = m.RenderRotation = obj.FacingRotation + (k - (shards - 1) / 2f) * 0.34f;
                m.Speed = 1.9f + diff * 0.22f;
            }
            obj.Box.RemoveObject(obj);
            return;
        }
        obj.Position += Helper.GetDirection(obj.FacingRotation) * obj.Speed;
        obj.RenderRotation += 0.08f;
    };

    /// <summary>
    /// Demid's first card, "the stream starts": rows of chat sliding in from the right edge, each with a
    /// two-slot gap in a different place — the only way through is to read where the gap is and be there.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidCard1 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int rate = Math.Max(22, 44 - diff * 5);
        if (t % rate == 0)
        {
            int row = t / rate;
            int gap = TickHash(row * 13) % 8;
            for (int i = 0; i < 9; i++)
            {
                if (i == gap || i == gap + 1)
                    continue;
                var b = c.Box.SpawnObject(ExtraCircleIndex, 0x9146FF);
                b.X = 392f;
                b.Y = 36f + i * 44f;
                b.FacingRotation = b.RenderRotation = MathF.PI;
                b.Speed = 1.7f + diff * 0.22f;
            }
        }
    };

    /// <summary>
    /// Demid's second card, "a hundred tabs": clusters of three bullets — one browser tab each — drop from the
    /// top of the screen in a row with exactly one tab missing.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidCard2 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int rate = Math.Max(26, 50 - diff * 5);
        if (t % rate == 0)
        {
            const int tabs = 6;
            int gap = TickHash(t) % tabs;
            for (int i = 0; i < tabs; i++)
            {
                if (i == gap)
                    continue;
                for (int j = 0; j < 3; j++)
                {
                    var b = c.Box.SpawnObject(ExtraRhombusIndex, 0x4285F4);
                    b.X = 40f + i * 61f + j * 11f;
                    b.Y = -8f;
                    b.FacingRotation = b.RenderRotation = MathF.PI / 2f;
                    b.Speed = 2.0f + diff * 0.3f;
                }
            }
        }
    };

    /// <summary>
    /// Demid's third card, "the memory leak": slow bubbles that split in two twice over on their way down, so
    /// what began as a trickle has filled the screen by the time the card is over.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidCard3 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(24, 48 - diff * 6) == 0)
        {
            int count = 3 + diff / 2;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraBubbleIndex, 0x00C2A8);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation =
                    TickHash(t * 31 + k * 71) % 3600 / 3600f * (MathF.PI * 2f);
                b.Speed = 1.0f + diff * 0.12f;
                b.CreatedAt = c.Box.CurrentTick;
                b.Header[LeakGenerationIndex] = 0;
                b.UpdateAction = DemidLeakMove;
            }
        }
    };

    /// <summary>Scratch slot holding how many times a leaking bubble has already split (capped at two).</summary>
    private const int LeakGenerationIndex = 0x61;

    /// <summary>A leaking bubble: drifts, and at 100 ticks old splits into two slower halves — twice, no more.</summary>
    private static readonly RuntimeObjectReferenceAction DemidLeakMove = obj =>
    {
        int age = obj.Box.CurrentTick - obj.CreatedAt;
        obj.Position += Helper.GetDirection(obj.FacingRotation) * obj.Speed;
        if (age != 100)
            return;
        int generation = obj.Header[LeakGenerationIndex];
        if (generation >= 2)
            return;
        for (int s = -1; s <= 1; s += 2)
        {
            var b = obj.Box.SpawnObject(ExtraBubbleIndex, generation == 0 ? 0x00A2FF : 0x7B68EE);
            b.Position = obj.Position;
            b.FacingRotation = b.RenderRotation = obj.FacingRotation + s * 0.55f;
            b.Speed = obj.Speed * 0.85f;
            b.CreatedAt = obj.Box.CurrentTick;
            b.Header[LeakGenerationIndex] = generation + 1;
            b.UpdateAction = DemidLeakMove;
        }
        obj.Box.RemoveObject(obj);
    };

    /// <summary>
    /// Demid's fourth card, "the blue screen": a wall of blue descends column by column with one column left
    /// open, and the open column walks sideways one step per row.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidCard4 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int rate = Math.Max(20, 40 - diff * 4);
        if (t % rate == 0)
        {
            const int columns = 8;
            int row = t / rate;
            int gap = (row + TickHash(row) % 3) % columns;
            for (int i = 0; i < columns; i++)
            {
                if (i == gap)
                    continue;
                var b = c.Box.SpawnObject(ExtraCircleIndex, 0x0078D7);
                b.X = 24f + i * 48f;
                b.Y = -8f;
                b.FacingRotation = b.RenderRotation = MathF.PI / 2f;
                b.Speed = 1.8f + diff * 0.25f;
            }
        }
    };

    /// <summary>
    /// Demid's fifth card, "click jitter": three-shot bursts fired exactly where the player is standing, each
    /// shot flicking a little off-aim — a mouse hand that cannot hold still.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidCard5 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int period = Math.Max(24, 48 - diff * 6);
        int phase = t % period;
        if (phase < 3)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            float jitter = (TickHash(t) % 200 - 100) / 100f * 0.22f;
            int count = 3 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraLightIndex, 0xFFFFFF);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + jitter + (k - (count - 1) / 2f) * 0.1f;
                b.Speed = 2.8f + diff * 0.3f + phase * 0.25f;
            }
        }
    };

    /// <summary>
    /// Demid's sixth card, "dropped frames": a spiral that plays at full speed, freezes for a third of a
    /// second, then jumps ahead to where it would have been — the stream is lagging, and so is the pattern.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidCard6 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        if ((t / 20) % 3 == 2)   // the dropped frames: nothing is emitted at all
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(3, 6 - diff) == 0)
        {
            int arms = 3 + diff / 2;
            for (int a = 0; a < arms; a++)
            {
                var b = c.Box.SpawnObject(ExtraOvalIndex, 0x00E5FF);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = t * 0.17f + a * (MathF.PI * 2f / arms);
                b.Speed = 2.2f + diff * 0.2f;
            }
        }
    };

    /// <summary>
    /// Demid's seventh card, the survival one: the OBS logo — an outer ring, an inner ring and four spiralling
    /// shutter blades, all built out of bullets (see <see cref="SpawnObsLogo"/>) — hangs in the middle of the
    /// playfield and turns. Demid himself cannot be shot; the card is beaten by outlasting it. Dying or bombing
    /// drops the invincibility and forfeits the survival bonus, exactly like stage 3's pizza card.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidObsBoss = c =>
    {
        if (c.Box.IsFailed)
            c.Header[0] &= ~RuntimeObject.FlagInvincible;
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        // "Recording": every four seconds the logo throws a ring of bullets outward from its rim.
        if (t % 240 == 60)
        {
            int count = 8 + diff * 2;
            for (int k = 0; k < count; k++)
            {
                float ang = k * (MathF.PI * 2f / count) + t * 0.008f;
                var b = c.Box.SpawnObject(ExtraCircleIndex, 0xE02D2D);
                b.Position = ObsLogoCenter + Helper.GetDirection(ang) * ObsOuterRadius;
                b.FacingRotation = b.RenderRotation = ang;
                b.Speed = 1.9f + diff * 0.2f;
            }
        }
        if (t % Math.Max(45, 90 - diff * 10) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 2 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraPentaIndex, 0xFFFFFF);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.2f;
                b.Speed = 2.4f + diff * 0.25f;
            }
        }
    };

    /// <summary>The logo's outer circle, and the radii its four spiralling blades run between.</summary>
    private const float ObsOuterRadius = 112f;
    private const float ObsBladeInnerRadius = 36f;
    private const float ObsBladeOuterRadius = 100f;

    /// <summary>
    /// Lays the OBS logo out of formation bullets: the solid outer circle, and inside it four blades that
    /// spiral out from the middle — the camera-aperture motif of the real mark, with the dark hole left in the
    /// centre. Every bullet keeps its polar coordinates in its own scratch slots and <see cref="ObsPixelMove"/>
    /// re-derives its position each tick, so the whole figure turns and breathes as one piece instead of
    /// drifting apart.
    /// </summary>
    private static void SpawnObsLogo(GameBox box)
    {
        void Place(float radius, float angle, int color)
        {
            var b = box.SpawnObject(ExtraObsPixelIndex, color);
            b.FloatingPoints[0x30] = radius;
            b.FloatingPoints[0x31] = angle;
            b.Position = ObsLogoCenter + Helper.GetDirection(angle) * radius;
            b.CreatedAt = box.CurrentTick;
            b.UpdateAction = ObsPixelMove;
        }

        const int outerCount = 44;
        for (int i = 0; i < outerCount; i++)
            Place(ObsOuterRadius, i * (MathF.PI * 2f / outerCount), 0xFFFFFF);
        // Four blades, each spiralling out from near the centre and sweeping 150° around as it goes.
        const int blades = 4, bladeSteps = 14;
        const float bladeSweep = 150f * MathF.PI / 180f;
        for (int blade = 0; blade < blades; blade++)
        {
            float start = blade * (MathF.PI * 2f / blades);
            for (int s = 0; s < bladeSteps; s++)
            {
                float k = s / (float)(bladeSteps - 1);
                Place(ObsBladeInnerRadius + (ObsBladeOuterRadius - ObsBladeInnerRadius) * k,
                    start + k * bladeSweep, 0xC8C8C8);
            }
        }
    }

    /// <summary>One bullet of the OBS logo: polar position (0x30 radius, 0x31 angle) plus the shared spin/pulse.</summary>
    private static readonly RuntimeObjectReferenceAction ObsPixelMove = obj =>
    {
        int t = Math.Max(0, obj.Box.ChapterTick);
        float radius = obj.FloatingPoints[0x30];
        float angle = obj.FloatingPoints[0x31] + t * 0.008f;
        float pulse = 1f + MathF.Sin(t * 0.03f) * 0.07f;
        obj.Position = ObsLogoCenter + Helper.GetDirection(angle) * radius * pulse;
        obj.RenderRotation = angle;
    };

    // ---- The last card: a window, drawn in bullets, that minimises to the taskbar and maximises back. -----
    /// <summary>One full minimise/hold/maximise/hold cycle, in ticks.</summary>
    private const int WindowPeriod = 300;

    /// <summary>
    /// The window when maximised (origin, size) and when minimised down at the taskbar. The maximised frame
    /// deliberately stops at y=380, above where the player stands at the start of a card (192, 400) — the whole
    /// frame is lethal, and it must not materialise on top of them.
    /// </summary>
    private static readonly Vector2 WindowMaxOrigin = new(24, 36), WindowMaxSize = new(336, 344);
    private static readonly Vector2 WindowMinOrigin = new(26, 400), WindowMinSize = new(64, 34);

    /// <summary>
    /// Where the cycle starts. The card opens on the minimised hold, so the frame is born small down at the
    /// taskbar and grows out of it over the first second, instead of appearing full-size around the player.
    /// </summary>
    private const int WindowStartPhase = 120;

    /// <summary>
    /// Where the window is at a given chapter tick: maximised, then shrinking to the taskbar over a second,
    /// held there, then growing back. The interpolation factor is 0 maximised, 1 minimised.
    /// </summary>
    /// <summary>Where in the minimise/maximise cycle a chapter tick falls: 0..60 shrinking, 60..120 held small,
    /// 120..180 growing, 180..300 sitting maximised.</summary>
    private static int WindowPhase(int tick) =>
        ((tick + WindowStartPhase) % WindowPeriod + WindowPeriod) % WindowPeriod;

    private static (Vector2 Origin, Vector2 Size) WindowRect(int tick)
    {
        int p = WindowPhase(tick);
        float k = p < 60 ? SmoothStep(p / 60f)
            : p < 120 ? 1f
            : p < 180 ? 1f - SmoothStep((p - 120) / 60f)
            : 0f;
        return (Vector2.Lerp(WindowMaxOrigin, WindowMinOrigin, k),
                Vector2.Lerp(WindowMaxSize, WindowMinSize, k));
    }

    private static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// Demid's last card: the window itself. Its frame, title bar and the three title-bar buttons are all
    /// bullets ( <see cref="SpawnWindowFrame"/> ), and they are dragged along as the window minimises and
    /// maximises — the sweep of the growing frame is the attack. While it sits maximised the close button
    /// spits shots at the player.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DemidWindowBoss = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int p = WindowPhase(t);
        if (p >= 180 && p % Math.Max(12, 24 - diff * 3) == 0)   // only while it sits maximised
        {
            var (origin, size) = WindowRect(t);
            Vector2 close = origin + size * new Vector2(0.94f, 0.05f);
            float aim = Helper.FindAngle(close, c.Box.Player.Position);
            int count = 2 + diff / 2;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(ExtraMicroIndex, 0xFF4444);
                b.Position = close;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.15f;
                b.Speed = 2.6f + diff * 0.25f;
            }
        }
    };

    /// <summary>
    /// Builds the window out of bullets: the four edges of the frame, the line under the title bar, and the
    /// minimise / maximise / close buttons in the top-right corner. Each bullet stores its position INSIDE the
    /// window as a 0..1 pair, so <see cref="WindowPixelMove"/> can put it back wherever the window currently is.
    /// </summary>
    private static void SpawnWindowFrame(GameBox box)
    {
        void Place(float u, float v, int index, int color)
        {
            var b = box.SpawnObject(index, color);
            b.FloatingPoints[0x30] = u;
            b.FloatingPoints[0x31] = v;
            b.CreatedAt = box.CurrentTick;
            b.UpdateAction = WindowPixelMove;
            var (origin, size) = WindowRect(Math.Max(0, box.ChapterTick));
            b.Position = origin + new Vector2(u * size.X, v * size.Y);
        }

        const int horizontal = 22, vertical = 15;
        for (int i = 0; i < horizontal; i++)
        {
            float u = i / (float)(horizontal - 1);
            Place(u, 0f, ExtraWindowPixelIndex, 0xE8E8E8);      // top edge
            Place(u, 1f, ExtraWindowPixelIndex, 0xE8E8E8);      // bottom edge
            Place(u, 0.11f, ExtraWindowPixelIndex, 0x7FB3FF);   // the line under the title bar
        }
        for (int i = 1; i < vertical; i++)
        {
            float v = i / (float)vertical;
            Place(0f, v, ExtraWindowPixelIndex, 0xE8E8E8);      // left edge
            Place(1f, v, ExtraWindowPixelIndex, 0xE8E8E8);      // right edge
        }
        Place(0.84f, 0.055f, ExtraLargeIndex, 0xFFD23F);        // minimise
        Place(0.90f, 0.055f, ExtraLargeIndex, 0x3FD36B);        // maximise
        Place(0.96f, 0.055f, ExtraLargeIndex, 0xFF4444);        // close
    }

    /// <summary>One bullet of the window frame: pinned to its (u, v) spot inside the animating window.</summary>
    private static readonly RuntimeObjectReferenceAction WindowPixelMove = obj =>
    {
        var (origin, size) = WindowRect(Math.Max(0, obj.Box.ChapterTick));
        obj.Position = origin + new Vector2(obj.FloatingPoints[0x30] * size.X,
            obj.FloatingPoints[0x31] * size.Y);
    };

    // ---------------------------------------------------------------------------------------------------
    // STAGE 3, second act — Dmitry, the campaign's final boss, and his five spell cards. This is the fight the
    // Extra stage looks back on: here he is still the Бог Пердификации with the gas to prove it, which is why
    // these cards are the loud versions of the wheezing ones he opens Extra with.
    // The indices are into Assets/Data/StagesJson/stage3.json; his entity and the two bullet templates the
    // cards needed (large, light) are appended at the end of that table, the rest were already there.
    // ---------------------------------------------------------------------------------------------------
    private const int Stage3OvalIndex = 0;         // oval — the gas puffs
    private const int Stage3PentaIndex = 8;        // pentabullet — the aimed spray
    private const int Stage3MicroIndex = 11;       // micro shards
    private const int Stage3BubbleIndex = 17;      // bubble — what rises off the floor under the lasers
    private const int Stage3CircleIndex = 20;      // plain round bullet (colourable)
    private const int DmitryStage3BossIndex = 23;  // Dmitry (Visual "dmitry", BossId 3)
    private const int Stage3LargeIndex = 24;       // large round bullet (colourable) — the gas clouds
    private const int Stage3LightIndex = 25;       // light bullet (colourable) — the curving streams
    private const int Stage3GrievanceBoxIndex = 26; // the complaints box (Visual "grievance_box") — card 4
    private const int Stage3MoonIndex = 27;        // the moon (Visual "moon", colourable) — card 4
    private const int DmitryStage3BossId = 3;

    /// <summary>
    /// A per-bullet turn rate in radians per tick, parked in an otherwise-unused float slot (see
    /// RuntimeObject.sp). Only the scripts that curve a bullet read it, so the value can be handed out at spawn
    /// time and the mover stays state-free — no managed side table to keep replay-safe.
    /// </summary>
    private const int ScriptTurnRateIndex = 0x32;

    /// <summary>Dmitry's post for a card, and how far he paces off it on the cards where he moves.</summary>
    private static readonly Vector2 DmitryStage3Post = new(192, 96);

    /// <summary>
    /// Dmitry's first card, "the four winds": four dense arcs fired 90° apart, the whole cross turned a notch
    /// further with every wave, so the gaps between the arms walk around the playfield instead of standing
    /// still. An aimed spray in between keeps sitting in one of those gaps from being free.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Card1 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int period = Math.Max(48, 84 - diff * 10);
        if (t % period == 0)
        {
            float baseAngle = t / period * 0.37f;   // each wave starts a little further round
            int perArm = 5 + diff;
            for (int arm = 0; arm < 4; arm++)
                for (int k = 0; k < perArm; k++)
                {
                    var b = c.Box.SpawnObject(Stage3OvalIndex, 0x8B5A2B);
                    b.Position = c.Position;
                    b.FacingRotation = b.RenderRotation = baseAngle + arm * (MathF.PI / 2f)
                        + (k - (perArm - 1) / 2f) * 0.12f;
                    b.Speed = 2.2f + diff * 0.25f;
                }
        }
        if (t % Math.Max(20, 40 - diff * 5) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 2 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(Stage3PentaIndex, 0xC8A165);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.18f;
                b.Speed = 1.9f + diff * 0.3f;
            }
        }
    };

    /// <summary>
    /// Dmitry's second card, "the stink cloud": heavy clouds sink slowly down the playfield and each one keeps
    /// venting rings of shards as it falls, so the danger is not the clouds but the space between them closing.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Card2 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(40, 70 - diff * 8) == 0)
        {
            int count = 2 + diff / 2;
            for (int k = 0; k < count; k++)
            {
                var cloud = c.Box.SpawnObject(Stage3LargeIndex, 0x6B8E23);
                cloud.X = 40f + TickHash(t * 31 + k * 137) % 304;
                cloud.Y = -16f;
                cloud.FacingRotation = cloud.RenderRotation = MathF.PI / 2f
                    + (TickHash(t * 7 + k * 53) % 100 - 50) / 400f;
                cloud.Speed = 0.7f + diff * 0.08f;
                cloud.CreatedAt = c.Box.CurrentTick;
                cloud.UpdateAction = DmitryStage3CloudMove;
            }
        }
    };

    /// <summary>One sinking cloud of the second card: drifts on its heading and vents a ring of shards on a
    /// clock, each ring turned off the last so the rings interleave rather than stack.</summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3CloudMove = obj =>
    {
        int age = obj.Box.CurrentTick - obj.CreatedAt;
        obj.Position += Helper.GetDirection(obj.FacingRotation) * obj.Speed;
        obj.RenderRotation += 0.03f;
        int diff = CardDiff(obj.Box);
        int vent = Math.Max(30, 54 - diff * 6);
        if (age > 0 && age % vent == 0)
        {
            int shards = 5 + diff;
            float spin = age * 0.05f;
            for (int k = 0; k < shards; k++)
            {
                var m = obj.Box.SpawnObject(Stage3MicroIndex, 0xADFF2F);
                m.Position = obj.Position;
                m.FacingRotation = m.RenderRotation = spin + k * (MathF.PI * 2f / shards);
                m.Speed = 1.4f + diff * 0.2f;
            }
        }
    };

    /// <summary>
    /// Dmitry's third card, "the pressure": a continuous aimed stream out of a muzzle that paces from side to
    /// side, every bullet curving as it flies — and the curve flips direction every couple of seconds, so the
    /// stream folds back over the lane it just left instead of laying down one clean arc.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Card3 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        c.X = DmitryStage3Post.X + MathF.Sin(t * 0.012f) * 96f;
        if (t % Math.Max(3, 6 - diff) == 0)
        {
            float turn = (t / 120 % 2 == 0 ? 1f : -1f) * (0.012f + diff * 0.002f);
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int lanes = 2 + diff / 2;
            for (int k = 0; k < lanes; k++)
            {
                var b = c.Box.SpawnObject(Stage3LightIndex, 0xFFD700);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k - (lanes - 1) / 2f) * 0.22f;
                b.FloatingPoints[ScriptTurnRateIndex] = turn;
                b.Speed = 2.4f + diff * 0.2f;
                b.UpdateAction = DmitryStage3CurveMove;
            }
        }
    };

    /// <summary>A bullet that turns at a fixed rate as it flies — the curving stream of the third card.</summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3CurveMove = obj =>
    {
        obj.FacingRotation += obj.FloatingPoints[ScriptTurnRateIndex];
        obj.RenderRotation = obj.FacingRotation;
        obj.Position += Helper.GetDirection(obj.FacingRotation) * obj.Speed;
    };

    // ---- Card 4: the complaints box and the moon ---------------------------------------------------------
    // A 160x80 "книга жалоб" box sits in the middle of the playfield. Every player shot that lands on it is a
    // grievance (+1, capped at 20), grievances cool off by 0.01 a tick, and while it holds any it vents a bullet
    // in a random direction every 20/level ticks — so firing straight up through it at Dmitry feeds the thing
    // that is shooting at you. It also launches a moon that rebounds off the screen edges a set number of times
    // (3 / 5 / 6 / 7 by difficulty), each rebound shaking the screen with Akob's spell sting, then sails off, and
    // the box launches the next one. The slots below are documented in RuntimeObject.sp under "script scratch".
    private const int GrievanceLevelIndex = 0x35;   // float: the box's grievance level
    private const int GrievanceVentIndex = 0x36;    // float: venting accumulator, whole units are bullets
    private const int MoonBouncesIndex = 0x31;      // header: rebounds the moon has left
    private const int ChapterStampIndex = 0x32;     // header: TickStart of the chapter that spawned it
    private const int MoonCooldownIndex = 0x33;     // header: ticks until the box launches the next moon

    private const float GrievanceMax = 20f;
    private const float GrievancePerShot = 1f;
    private const float GrievanceDecayPerTick = 0.01f;
    /// <summary>"A bullet every 20 / level ticks" is level / 20 of a bullet per tick; the accumulator in
    /// <see cref="GrievanceVentIndex"/> carries the fraction so a level of 0.7 still vents, just slowly.</summary>
    private const float GrievanceVentPerLevelPerTick = 1f / 20f;
    /// <summary>The box cannot be worn down — shots feed it — so its health is pinned here every tick.</summary>
    private const float GrievanceBoxHealth = 1e9f;
    /// <summary>The box hangs along the top of the field and sweeps side to side across it — this far either
    /// way from the centre, at this rate (radians per tick, ~5 s per pass), staying inside the edges.</summary>
    private static readonly Vector2 GrievanceBoxPost = new(192, 40);
    private const float GrievanceBoxSweep = 108f, GrievanceBoxSweepRate = 0.02f;
    private static readonly Vector2 GrievanceBoxSize = new(160, 80);
    /// <summary>The slot on the box's face, where the vented bullets come out (the sprite's slot is 24px above
    /// its centre).</summary>
    private static readonly Vector2 GrievanceSlotOffset = new(0, -24);
    private const int MoonFirstLaunchDelay = 60, MoonRelaunchDelay = 90;
    /// <summary>The moon is object.png (the staff-roll sprite) scaled so it is about this wide on the field,
    /// whatever size the texture was loaded at — the padding around a sprite bullet is included in the draw.</summary>
    private const float MoonSpriteWidth = 108f;
    /// <summary>The moon's visible radius, where it rebounds from an edge: just under half of
    /// <see cref="MoonSpriteWidth"/>, the sprite's art not quite filling its frame.</summary>
    private const float MoonRadius = 47f;

    /// <summary>How many screen edges the moon rebounds from before it sails off: 3 on Easy, 5 on Normal, 6 on
    /// Hard, 7 on Max (and on Extra / any higher practice tier).</summary>
    public static int MoonBounceLimit(int difficulty) => Math.Clamp(difficulty, 0, 4) switch
    {
        0 => 3,
        1 => 5,
        2 => 6,
        _ => 7,
    };

    /// <summary>
    /// Dmitry on his fourth card, "the complaints box": he fires nothing himself. He paces the full width of
    /// the field so the columns either side of the box — the only lines of fire that do not feed it — keep
    /// opening and closing; the pressure comes from the box and the moon.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Card4 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        c.X = DmitryStage3Post.X + MathF.Sin(t * 0.009f) * 120f;
    };

    /// <summary>
    /// The complaints box: counts the player shots the Pizzics sweep landed on it since last tick, grows and
    /// cools its grievance level, vents bullets by that level, and keeps one moon in the air. A plain entity is
    /// not cleared between chapters, so it retires itself the moment the chapter it was spawned for is gone.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction GrievanceBoxTick = box =>
    {
        GameBox gb = box.Box;
        if (gb.ChapterInfo == null || gb.ChapterInfo.TickStart != box.Header[ChapterStampIndex])
        {
            gb.RemoveObject(box);
            return;
        }
        box.Health = GrievanceBoxHealth;
        int hits = box.PlayerShotHits;
        box.PlayerShotHits = 0;
        float level = box.FloatingPoints[GrievanceLevelIndex];
        level = MathF.Min(GrievanceMax, level + hits * GrievancePerShot);
        level = MathF.Max(0f, level - GrievanceDecayPerTick);
        box.FloatingPoints[GrievanceLevelIndex] = level;

        // The level is legible from the sprite: it swells with it, twitches faster the fuller it gets, and
        // jolts on the tick a shot lands.
        float fill = level / GrievanceMax;
        int t = gb.ChapterTick;
        box.X = GrievanceBoxPost.X + MathF.Sin(t * GrievanceBoxSweepRate) * GrievanceBoxSweep;
        box.EntranceScale = 1f + fill * 0.10f + (hits > 0 ? 0.05f : 0f);
        box.RenderRotation = fill > 0f ? MathF.Sin(t * (0.25f + fill * 0.6f)) * fill * 0.07f : 0f;

        // Once the card is over (the boss is down: the chapter clock jumps to its end) it goes quiet.
        if (gb.InChapterDelay || gb.ChapterTick >= gb.ChapterInfo.Length)
            return;

        if (level > 0f)
        {
            float vent = box.FloatingPoints[GrievanceVentIndex] + level * GrievanceVentPerLevelPerTick;
            int diff = CardDiff(gb);
            for (int k = 0; vent >= 1f; k++, vent -= 1f)
            {
                var b = gb.SpawnObject(Stage3CircleIndex, 0xFF6A3D);
                b.Position = box.Position + GrievanceSlotOffset;
                float angle = TickHash(gb.CurrentTick * 7919 + k * 131) % 6283 / 1000f;
                b.FacingRotation = b.RenderRotation = angle;
                b.Speed = 2.0f + diff * 0.2f;
            }
            box.FloatingPoints[GrievanceVentIndex] = vent;
        }

        bool moonOut = false;
        foreach (var other in gb.BoxObjects)
            if (other.UpdateAction == MoonTick)
            {
                moonOut = true;
                break;
            }
        if (moonOut)
            box.Header[MoonCooldownIndex] = MoonRelaunchDelay;
        else if (--box.Header[MoonCooldownIndex] <= 0)
            LaunchMoon(gb, box);
    };

    /// <summary>Sends a moon out of the box on a diagonal-ish heading (never along an axis, which would just
    /// ping-pong between two edges) with the difficulty's stock of rebounds.</summary>
    private static void LaunchMoon(GameBox gb, RuntimeObject box)
    {
        var moon = gb.SpawnObject(Stage3MoonIndex);
        moon.Position = box.Position;
        int quadrant = TickHash(gb.CurrentTick * 31 + 7) % 4;
        float within = TickHash(gb.CurrentTick * 17 + 3) % 1000 / 1000f;
        moon.FacingRotation = quadrant * (MathF.PI / 2f) + MathF.PI / 6f + within * (MathF.PI / 6f);
        // The visual is the whole of object.png; scale it down to size on the field (the texture's pixel size
        // follows the resolution / quality settings, so this is worked out from what was actually loaded),
        // and start it upright — the draw turns every bullet a quarter turn, which a round moon never showed.
        moon.EntranceScale = MoonSpriteWidth / MathF.Max(1f, moon.TotalTextureSize.X);
        moon.RenderRotation = -MathF.PI / 2f;
        moon.Speed = 2.3f + CardDiff(gb) * 0.25f;
        moon.Header[MoonBouncesIndex] = MoonBounceLimit(gb.Difficulty);
        moon.Header[ChapterStampIndex] = box.Header[ChapterStampIndex];
        moon.PersistOffscreen = true;   // it is bigger than the cull margin; it leaves on its own terms below
        moon.UpdateAction = MoonTick;
    }

    /// <summary>
    /// The moon: flies straight, rebounds off whichever playfield edge it reaches while it has rebounds left
    /// (each one shakes the screen and plays Akob's spell sting), and once it is out of them keeps going off
    /// the field and removes itself. Rebounding stops the moment the card is over, so it clears out with it.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction MoonTick = moon =>
    {
        GameBox gb = moon.Box;
        if (gb.ChapterInfo == null || gb.ChapterInfo.TickStart != moon.Header[ChapterStampIndex])
        {
            gb.RemoveObject(moon);
            return;
        }
        Vector2 d = Helper.GetDirection(moon.FacingRotation);
        moon.X += d.X * moon.Speed;
        moon.Y += d.Y * moon.Speed;
        moon.RenderRotation += d.X >= 0 ? 0.012f : -0.012f;   // rolls the way it travels
        if (gb.ChapterTick >= gb.ChapterInfo.Length)
            moon.Header[MoonBouncesIndex] = 0;
        if (moon.Header[MoonBouncesIndex] > 0)
        {
            bool bounced = false;
            if (moon.X < MoonRadius && d.X < 0)
            {
                moon.X = MoonRadius;
                d.X = -d.X;
                bounced = true;
            }
            else if (moon.X > 384 - MoonRadius && d.X > 0)
            {
                moon.X = 384 - MoonRadius;
                d.X = -d.X;
                bounced = true;
            }
            if (moon.Y < MoonRadius && d.Y < 0)
            {
                moon.Y = MoonRadius;
                d.Y = -d.Y;
                bounced = true;
            }
            else if (moon.Y > 448 - MoonRadius && d.Y > 0)
            {
                moon.Y = 448 - MoonRadius;
                d.Y = -d.Y;
                bounced = true;
            }
            if (bounced)
            {
                moon.FacingRotation = MathF.Atan2(d.Y, d.X);
                moon.Header[MoonBouncesIndex]--;
                float time = gb.GetTime();
                gb.AddScreenEffect(new ShakeScreenEffect(gb, 0.1f, 20, 100, time, time + 0.3f));
                PlaySound(Runtime.CurrentRuntime.Sounds["akob-bomb"]);
            }
            return;
        }
        if (moon.X < -MoonRadius - 8 || moon.X > 384 + MoonRadius + 8 ||
            moon.Y < -MoonRadius - 8 || moon.Y > 448 + MoonRadius + 8)
            gb.RemoveObject(moon);
    };

    /// <summary>
    /// Dmitry's last card, "the apotheosis": a turning spiral, full rings aimed one notch off the player so the
    /// gap through them moves, and heavy shots that burst into shards partway down. All three tighten as the
    /// card runs — this is him spending everything, and it is the card the campaign ends on.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Card5 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        float ramp = MathF.Min(1f, t / 900f);   // fully wound up after ~15 s
        c.X = DmitryStage3Post.X + MathF.Sin(t * 0.008f) * 72f;
        if (t % Math.Max(3, 9 - diff - (int)(ramp * 3f)) == 0)
        {
            int arms = 3 + diff / 2;
            for (int a = 0; a < arms; a++)
            {
                var b = c.Box.SpawnObject(Stage3CircleIndex, 0xADFF2F);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = t * 0.13f + a * (MathF.PI * 2f / arms);
                b.Speed = 1.9f + diff * 0.18f;
            }
        }
        if (t % Math.Max(90, 180 - diff * 20 - (int)(ramp * 40f)) == 0)
        {
            int count = 16 + diff * 4;
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(Stage3OvalIndex, 0x8B5A2B);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k + 0.5f) * (MathF.PI * 2f / count);
                b.Speed = 2.5f + diff * 0.22f;
            }
            c.Box.AddScreenEffect(new StrengthScreenEffect(c.Box, c.Position, 50, c.Box.GetTime(), c.Box.GetTime()+1, 0x00FF34, 0x00EE69));
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
        }
        if (t % Math.Max(60, 150 - diff * 15) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 1 + diff / 2;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(Stage3LargeIndex, 0xFFD700);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.3f;
                b.Speed = 2.1f + diff * 0.2f;
                b.CreatedAt = c.Box.CurrentTick;
                b.UpdateAction = DmitryStage3BurstMove;
            }
        }
    };

    /// <summary>A heavy shot of the last card: flies straight, then bursts into a fan of shards and is gone.</summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3BurstMove = obj =>
    {
        int age = obj.Box.CurrentTick - obj.CreatedAt;
        if (age >= 54)
        {
            int diff = CardDiff(obj.Box);
            int shards = 6 + diff * 2;
            for (int k = 0; k < shards; k++)
            {
                var m = obj.Box.SpawnObject(Stage3MicroIndex, 0xFFD700);
                m.Position = obj.Position;
                m.FacingRotation = m.RenderRotation = obj.FacingRotation + k * (MathF.PI * 2f / shards);
                m.Speed = 1.6f + diff * 0.2f;
            }
            obj.Box.RemoveObject(obj);
            return;
        }
        obj.Position += Helper.GetDirection(obj.FacingRotation) * obj.Speed;
        obj.RenderRotation += 0.09f;
    };

    // ---- Dmitry's stage-3 non-spells. Three of them, run before cards 1, 3 and 5, so his act alternates the
    // way the rest of the campaign's do instead of being five spell cards back to back.
    //
    // Each is a plain, readable version of the idea the card after it complicates: a bare turning ring before
    // the four winds, a moving gap before the sinking clouds, a lane sweep before the burst finale. They reuse
    // the cards' bullet templates and colours (Assets/Data/StagesJson/stage3.json) — a non-spell is the same
    // boss with the same gas, just not trying as hard — and none of them needs a per-bullet mover, so there is
    // nothing here but the boss's own update.

    /// <summary>
    /// Before "the four winds": one ring of gas at a time, the whole ring turned a step further with each wave
    /// and the step reversing every few seconds. Nothing aimed — the only thing to read is which way the gaps
    /// are walking, which is the skill the card then asks for under pressure.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Nonspell1 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        int period = Math.Max(24, 44 - diff * 5);
        if (t % period != 0)
            return;
        int wave = t / period;
        int count = 8 + diff * 2;
        float spin = (wave / 6 % 2 == 0 ? 1f : -1f) * wave * 0.29f;
        for (int k = 0; k < count; k++)
        {
            var b = c.Box.SpawnObject(Stage3OvalIndex, 0x8B5A2B);
            b.Position = c.Position;
            b.FacingRotation = b.RenderRotation = spin + k * (MathF.PI * 2f / count);
            b.Speed = 2.0f + diff * 0.22f;
        }
    };

    /// <summary>
    /// Before "the stink cloud": rows of gas walk in from both side edges, each row with one gap in it, and the
    /// gap slides a lane per row. He paces the top slowly and drops the odd aimed shot so the player cannot
    /// simply follow the gap without watching him too.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Nonspell2 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        c.X = DmitryStage3Post.X + MathF.Sin(t * 0.010f) * 84f;

        const int lanes = 7;
        int period = Math.Max(26, 46 - diff * 5);
        if (t % period == 0)
        {
            int row = t / period;
            int gap = row % lanes;                       // the hole moves one lane per row
            for (int lane = 0; lane < lanes; lane++)
            {
                if (lane == gap)
                    continue;
                int side = row % 2;                      // rows come in from alternating edges
                var b = c.Box.SpawnObject(Stage3CircleIndex, side == 0 ? 0x6B8E23 : 0x8B4513);
                b.X = side == 0 ? -8f : 392f;
                b.Y = 96f + lane * 44f;
                b.FacingRotation = b.RenderRotation = side == 0 ? 0f : MathF.PI;
                b.Speed = 1.7f + diff * 0.22f;
            }
        }
        if (t % Math.Max(30, 60 - diff * 7) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 1 + diff / 2;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(Stage3PentaIndex, 0xC8A165);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.2f;
                b.Speed = 2.2f + diff * 0.25f;
            }
        }
    };

    /// <summary>
    /// Before "the apotheosis": a curtain of light bullets falls in sweeps across the playfield, left to right
    /// and back, with a ring off the boss every couple of seconds to break up the rhythm. The last quiet moment
    /// of the campaign before the card that ends it.
    /// </summary>
    private static readonly RuntimeObjectReferenceAction DmitryStage3Nonspell3 = c =>
    {
        int t = BossCardTick(c);
        if (t < 0)
            return;
        int diff = CardDiff(c.Box);
        if (t % Math.Max(4, 8 - diff) == 0)
        {
            // A triangle wave over 240 ticks: the drop point runs the width of the playfield and back.
            int phase = t % 240;
            float sweep = phase < 120 ? phase / 120f : (240 - phase) / 120f;
            for (int k = 0; k < 1 + diff / 2; k++)
            {
                var b = c.Box.SpawnObject(Stage3LightIndex, 0xFFD700);
                b.X = 24f + sweep * 336f + k * 16f;
                b.Y = -8f;
                b.FacingRotation = b.RenderRotation = MathF.PI / 2f;
                b.Speed = 2.3f + diff * 0.25f;
            }
        }
        if (t % Math.Max(70, 130 - diff * 15) == 0)
        {
            int count = 10 + diff * 2;
            float baseAngle = TickHash(t) % 628 / 100f;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(Stage3CircleIndex, 0xADFF2F);
                b.Position = c.Position;
                b.FacingRotation = b.RenderRotation = baseAngle + k * (MathF.PI * 2f / count);
                b.Speed = 1.8f + diff * 0.2f;
            }
        }
    };

    static ActionsScope()
    {
        RebuildObjectActionsList();
        RebuildChapterActionsList();
    }

    public static void RebuildChapterActionsList()
    {
        var dictionary = new Dictionary<string, RuntimeChapterReferenceAction>();
        dictionary["_chapter!!!"] = (chapter) =>
        {

        };
        // Two toilets that spew a bunch of yellow / brown / green-yellow bullets. This spell exists only on Hard
        // and Max — on Easy/Normal it spawns nothing (the card simply times out).
        dictionary["toilets#colorspam#create"] = c =>
        {
            if (c.GameBox.Difficulty < 2)
                return;
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? 112f : 272f;
                var toilet = c.GameBox.SpawnObject(7);
                toilet.X = x;
                toilet.Y = -16;
                toilet.SetMoveToTarget(4, new Vector2(x, 96));
                toilet.UpdateAction = ColorSpamToilet;
            }
        };
        dictionary["nikitos#spell1#easy_create"] = c =>
        {
            var nPerson = c.GameBox.SpawnObject(2);
            nPerson.X = -8;
            nPerson.Y = -8;
            int diff = Math.Clamp(c.GameBox.Difficulty, 0, 3);
            nPerson.Header[0x50] = Math.Max(2, 4 - diff);   // faster bullet cadence on harder difficulties
            nPerson.Header[0x51] = 120;
            nPerson.Header[0x5B] = 1;
            var rnd = new Random((int)(c.GameBox.Player.X + c.GameBox.Player.Y));
            var pos = new Vector2(rnd.Next(32, 352), rnd.Next(48, 96));
            nPerson.SetMoveToTarget(2, pos);
            var direction = Helper.FindAngle(pos, new Vector2(c.GameBox.Player.X, c.GameBox.Player.Y));
            nPerson.FloatingPoints[0x5C] = direction;
        };
        dictionary["nikitos#spell2#easy_create"] = c =>
        {
            var nPerson = c.GameBox.SpawnObject(3);
            nPerson.SetMoveToTarget(2, new Vector2(192, 80));
            int diff = Math.Clamp(c.GameBox.Difficulty, 0, 3);
            nPerson.Header[0x50] = Math.Max(2, 4 - diff);   // faster bullet cadence on harder difficulties
            nPerson.Header[0x51] = 120;
            nPerson.Header[0x5B] = 1;

            // Nokia 8 turrets — Hard & Max only. Nikitos spawns them from itself with a strength-shader pulse,
            // and each slides to its post over ~0.3 s. Once placed they toggle every 2 s (starting off) and fire
            // a delayed laser at the player (nokia8#laser, set as the entity's update script).
            if (c.GameBox.Difficulty >= 2)
            {
                Vector2 from = new Vector2(192, 80);   // out of the boss
                foreach (float nx in new[] { 72f, 192f, 312f })
                {
                    Vector2 target = new Vector2(nx, 56);
                    var nokia = c.GameBox.SpawnObject(Nokia8EntityIndex);
                    nokia.X = from.X;
                    nokia.Y = from.Y;
                    float dist = Vector2.Distance(from, target);
                    nokia.SetMoveToTarget(MathF.Max(0.1f, dist / 18f), target);   // ~0.3 s (18 ticks) to place
                    c.GameBox.AddScreenEffect(new StrengthScreenEffect(c.GameBox, target, 40,
                        c.GameBox.GetTime(), c.GameBox.GetTime() + 0.5f, 0x00FF34, 0x00EE69));
                }
            }
        };
        dictionary["nikitos#spell2#easy"] = c =>
        {

        };
        dictionary["nikitos#spell2#easy"] = c =>
        {
            if (c.GameBox.CurrentTick + c.GameBox.TickOffset - c.TickStart == 30)
            {
                var toilet1 = c.GameBox.SpawnObject(7);
                var toilet2 = c.GameBox.SpawnObject(7);
                toilet1.X = 64;
                toilet2.X = 320;
                toilet1.Y = toilet2.Y = -16;
                toilet1.SetMoveToTarget(4, new Vector2(64, 96));
                toilet2.SetMoveToTarget(4, new Vector2(320, 96));
            }
        };
        // The 0th spell card of stage 2: the original two colour-spam toilets (Hard & Max), plus the Nikitab
        // boss that drifts in and rains tick-driven pseudo-random micro-bullets.
        dictionary["nikitab#stage2#spell0#create"] = c =>
        {
            if (c.GameBox.Difficulty >= 2)
            {
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? 112f : 272f;
                    var toilet = c.GameBox.SpawnObject(7);
                    toilet.X = x;
                    toilet.Y = -16;
                    toilet.SetMoveToTarget(4, new Vector2(x, 96));
                    toilet.UpdateAction = ColorSpamToilet;
                }
            }

            var boss = c.GameBox.SpawnObject(NikitabEntityIndex);
            boss.X = 192;
            // Spawn directly ON-screen. The old code spawned at y=-40 and relied on a move-to-target to slide it
            // down, but the box's offscreen cull removes any non-laser object at y < -32 — so the boss was culled
            // within a tick or two and never appeared. The entrance is instead the elastic scale budge that
            // NikitabMicroSpam plays over its first 30 ticks (EntranceScale), so it pops into place here.
            boss.Y = 120;
        };
        // Stage 2's two non-spells, played (on every difficulty) BEFORE the spell above. Each spawns the nikitab
        // boss on-screen and hands it the matching attack; SpawnObject reuses the same boss across chapters, so it
        // carries through the whole fight into the spell.
        dictionary["nikitab#stage2#nonspell0#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitabEntityIndex);
            boss.X = 192;
            boss.Y = 120;
            boss.UpdateAction = NikitabNonspell1;
        };
        dictionary["nikitab#stage2#nonspell1#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitabEntityIndex);
            boss.X = 192;
            boss.Y = 120;
            boss.UpdateAction = NikitabNonspell2;
        };
        // Stage 3's first spellcard: an INVINCIBLE Nikitab that fires a single laser sweeping clockwise around
        // itself. A survival card — the boss can't be shot down and shows no health bar (the chapter's
        // BossInvincible flag drives the suppression and grants full score on survival). Dying or bombing drops
        // the invincibility and forfeits the bonus.
        dictionary["nikitab#stage3#pizza#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitabEntityIndex);
            boss.X = 192;
            boss.Y = 224;
            boss.Header[0] |= RuntimeObject.FlagInvincible;
            boss.UpdateAction = NikitaStage3LaserBoss;
            PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
            // One laser fired from Nikitab that sweeps clockwise for the whole card (its own update rotates it).
            var laser = c.GameBox.SpawnLaser(boss.Position, 0f, 520f, 16f, 45, 1800, 20);
            laser.UpdateAction = NikitaStage3SweepLaser;
        };
        // ---- Stage 3's second act: Dmitry, the campaign's final boss, and his five cards. His first card also
        // retires Nikitab (whose survival card is done with him) and brings in a brand-new boss, BossId 3 — the
        // same hand-off stage 2 does between its two acts. SpawnObject then REUSES that Dmitry for every later
        // card, so one boss with one health bar carries the whole act.
        // His arrival is now the FIRST non-spell, not card 1 — the non-spell is what he turns up on, so it is
        // what retires Nikitab, plays the sting and shows the splash. Card 1 is a plain spawn behind it. (Spell
        // practice starts at a card, so a practiced card 1 gets no splash — the same as every other practiced
        // card, none of which shows one.)
        dictionary["dmitry#stage3#nonspell1#create"] = c =>
        {
            RetireOtherBosses(c.GameBox, DmitryStage3BossId);
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Nonspell1, DmitryStage3Post);
            PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
            ShowBossSplash(c.GameBox, "dmitry");
        };
        dictionary["dmitry#stage3#nonspell2#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Nonspell2, DmitryStage3Post);
        dictionary["dmitry#stage3#nonspell3#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Nonspell3, DmitryStage3Post);
        dictionary["dmitry#stage3#card1#create"] = c =>
        {
            RetireOtherBosses(c.GameBox, DmitryStage3BossId);
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Card1, DmitryStage3Post);
        };
        dictionary["dmitry#stage3#card2#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Card2, DmitryStage3Post);
        dictionary["dmitry#stage3#card3#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Card3, DmitryStage3Post);
        // The complaints box is put down once here and runs the card from its own update (grievances, venting,
        // the moon); it is stamped with the chapter so it can take itself off when the card is over.
        dictionary["dmitry#stage3#card4#create"] = c =>
        {
            // The moon is drawn from object.png, which belongs to the staff-roll texture group and is not
            // loaded for a run; bring the group in now (a no-op once it is there — it stays until the title
            // screen unloads the extras) so the first launch does not look up a texture that is not in.
            Runtime.CurrentRuntime.LoadTextureGroup("staff");
            SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Card4, DmitryStage3Post);
            var box = c.GameBox.SpawnObject(Stage3GrievanceBoxIndex);
            box.Position = GrievanceBoxPost;
            box.HitBoxSize = GrievanceBoxSize;
            box.Header[ChapterStampIndex] = c.TickStart;
            box.Header[MoonCooldownIndex] = MoonFirstLaunchDelay;
            box.UpdateAction = GrievanceBoxTick;
        };
        // The last card of the campaign: he has to die for real at the end of it, which is what the final-boss
        // flag says (it is a Header[0] bit, and SpawnObject's reuse path does not carry it over from a template).
        dictionary["dmitry#stage3#card5#create"] = c =>
        {
            var boss = SpawnCardBoss(c.GameBox, DmitryStage3BossIndex, DmitryStage3Card5, DmitryStage3Post);
            boss.Header[0] |= RuntimeObject.FlagIsFinalBossChapter;
            ShowBossSplash(c.GameBox, "dmitry");
        };
        // ---- Nikita Bukin's act (after the toilet spell): a brand-new boss (BossId 2) arrives on a pizza, drops
        // it, talks, then runs a non-spell (spiral) and two spells (watermelon, then the yellow/penta reaction).
        dictionary["nikitab#stage2#appear#create"] = c =>
        {
            // Retire the previous act's boss (BossId 1): after its spell it lingers with its attack disabled.
            // Flagging it "died" lets its health bar self-remove; drop its lingering screen effects explicitly.
            foreach (var old in c.GameBox.BoxObjects)
            {
                if ((old.Header[0] & RuntimeObject.FlagIsBoss) != RuntimeObject.FlagIsBoss) continue;
                if (old.BossId == NikitaBossId) continue;
                old.Header[0] |= RuntimeObject.FlagIsDied;
                if (old.BackgroundDistortionEffect != null) c.GameBox.RemoveScreenEffect(old.BackgroundDistortionEffect);
                if (old.BossCircleEffect != null) c.GameBox.RemoveScreenEffect(old.BossCircleEffect);
                c.GameBox.RemoveObject(old);
            }

            var boss = c.GameBox.SpawnObject(NikitaBossIndex);   // first spawn -> creates BossId 2 (+ its health bar)
            boss.X = 192;
            boss.Y = 408;                                        // just inside the bottom edge
            boss.RenderAlpha = 0.5f;                             // rides in see-through
            boss.Header[0] |= RuntimeObject.FlagInvincible;      // cannot be shot during the entrance
            boss.UpdateAction = NikitaAppear;
            boss.SetMoveToTarget(3.6f, new Vector2(192, 100));   // ~85 ticks up to its post

            var pizza = c.GameBox.SpawnObject(NikitaPizzaMountIndex);
            pizza.X = 192;
            pizza.Y = 454;
            pizza.RenderAlpha = 0.5f;
            pizza.Header[0] |= RuntimeObject.FlagInvincible;
        };
        dictionary["nikitab#stage2#spiral#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitaBossIndex);   // reuses BossId 2, reset to full health for the card
            boss.X = 192; boss.Y = 100;
            boss.RenderAlpha = 1f;
            boss.UpdateAction = NikitaSpiral;
        };
        dictionary["nikitab#stage2#watermelon#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitaBossIndex);
            boss.X = 192; boss.Y = 100;
            boss.RenderAlpha = 1f;
            boss.UpdateAction = NikitaWatermelon;
        };
        dictionary["nikitab#stage2#yellow#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitaBossIndex);
            boss.X = 192; boss.Y = 100;
            boss.RenderAlpha = 1f;
            boss.UpdateAction = NikitaYellow;
        };
        dictionary["nikitab#stage2#final#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(NikitaBossIndex);
            boss.X = 192; boss.Y = 100;
            boss.RenderAlpha = 1f;
            boss.Header[0] |= RuntimeObject.FlagIsFinalBossChapter;   // now the true last card — dies for real at the end
            boss.UpdateAction = NikitaFinal;
            ShowBossSplash(c.GameBox);
        };
        // Stage 1 non-spells: spawn the nikitos boss and give it the non-spell attack (overriding its default
        // spell behaviour). Spawning the boss entity reuses the one already on screen, so it persists across the
        // boss's attacks.
        // SpawnObject(2) reuses the shared nikitos boss (all nikitos entities are BossId 0, same as the stage's
        // spell cards), so the same boss carries through the fight. Place it dead centre-top so it — and the
        // bullets it fires from its position — are on screen.
        // The midboss: the same nikitos entity, met halfway through the level between the two halves of the
        // stage section. Lower down the screen than his boss posts (there is no health bar crowding the top
        // yet) and on half health, because this is a drive-by, not the fight.
        dictionary["nikitos#midboss#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(2);
            boss.X = 192;
            boss.Y = 72;
            boss.Health = boss.MaxHealth = boss.Health / 2f;
            boss.UpdateAction = NikitosMidbossRain;
        };
        // ...and leaves when the level resumes. A chapter boundary does NOT clear the board — the boss fight
        // depends on that, since every one of nikitos's chapters reuses the same object — so the half of the
        // stage section after the midboss has to take him off it, or he hangs there raining for the rest of the
        // level and then again through the fight. Finds nothing to do if the player shot him down.
        dictionary["stage#midboss#retire"] = c => RetireOtherBosses(c.GameBox, NoBossId);
        dictionary["nikitos#nonspell1#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(2);
            boss.X = 192;
            boss.Y = 88;
            boss.UpdateAction = NikitosNonspell1;
        };
        dictionary["nikitos#nonspell2#create"] = c =>
        {
            var boss = c.GameBox.SpawnObject(2);
            boss.X = 192;
            boss.Y = 88;
            boss.UpdateAction = NikitosNonspell2;
        };
        dictionary["nikitab#spell5#create"] = c =>
        {
            var box = c.GameBox;
            int diff = Math.Clamp(box.Difficulty, 0, 3);
            long seed = new Random((int)(box.Player.Position.X + box.Player.Position.Y)).NextInt64();
            box.SpawnObject(0);
            ShowBossSplash(box);
        };
        dictionary["nikitab#spell5#update"] = c =>
        {
            var box = c.GameBox;
            int t = box.ChapterTick;
            if (t % NikitabLastSpellCircleSpawnRate == 0)
            {
                float angle = t;
                angle = angle / NikitabLastSpellFullCircleTime * MathF.PI * 2;
                double r = (t + 2);
                double v = r < 3 ? 2 : 1;
                double b1 = r > 4 ? 1 : 3.5;
                double g = r > 3 ? r + 1 : r + 2;
                double z = Math.Abs(v - g % (v * 2));
                float s = (float)(Math.Pow(z, .5) * b1);
                var obj = box.SpawnObject(22);
                obj.Position = new Vector2(192, 224) + Helper.GetDirection2(angle) * (s * .4f + .6f) * 200;
            }
        };
        // ---- EXTRA STAGE ----------------------------------------------------------------------------------
        // Dmitry's arrival: he is invincible here because the chapter is nothing but his (unskippable) speech —
        // there is no attack to survive and nothing to shoot down yet.
        dictionary["dmitry#extra#arrival#create"] = c =>
        {
            var boss = SpawnCardBoss(c.GameBox, DmitryBossIndex, DmitryIdle, new Vector2(192, 96));
            boss.Header[0] |= RuntimeObject.FlagInvincible;
            ShowBossSplash(c.GameBox, "dmitry");
        };
        dictionary["dmitry#extra#card1#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryBossIndex, DmitryCard1, new Vector2(192, 96));
        dictionary["dmitry#extra#card2#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryBossIndex, DmitryCard2, new Vector2(192, 88));
        dictionary["dmitry#extra#card3#create"] = c =>
            SpawnCardBoss(c.GameBox, DmitryBossIndex, DmitryCard3, new Vector2(192, 112));
        // Demid takes over: retire Dmitry (his act is done) and bring in a brand-new boss, BossId 1.
        dictionary["demid#extra#card1#create"] = c =>
        {
            RetireOtherBosses(c.GameBox, DemidBossId);
            SpawnCardBoss(c.GameBox, DemidBossIndex, DemidCard1, new Vector2(192, 92));
            ShowBossSplash(c.GameBox, "demid.png");
        };
        dictionary["demid#extra#card2#create"] = c =>
            SpawnCardBoss(c.GameBox, DemidBossIndex, DemidCard2, new Vector2(192, 92));
        dictionary["demid#extra#card3#create"] = c =>
            SpawnCardBoss(c.GameBox, DemidBossIndex, DemidCard3, new Vector2(192, 100));
        dictionary["demid#extra#card4#create"] = c =>
            SpawnCardBoss(c.GameBox, DemidBossIndex, DemidCard4, new Vector2(192, 84));
        dictionary["demid#extra#card5#create"] = c =>
            SpawnCardBoss(c.GameBox, DemidBossIndex, DemidCard5, new Vector2(192, 96));
        dictionary["demid#extra#card6#create"] = c =>
            SpawnCardBoss(c.GameBox, DemidBossIndex, DemidCard6, new Vector2(192, 104));
        // The survival card: an invincible Demid up at the top and the OBS logo turning in the middle. There is
        // nothing to whittle down, so the bar he has carried since his first card comes off for this one.
        dictionary["demid#extra#obs#create"] = c =>
        {
            var boss = SpawnCardBoss(c.GameBox, DemidBossIndex, DemidObsBoss, new Vector2(192, 60));
            boss.Header[0] |= RuntimeObject.FlagInvincible;
            SetBossHealthBar(c.GameBox, boss, false);
            SpawnObsLogo(c.GameBox);
        };
        // The "you can't get back at my Google Chromines" speech, between the OBS card and the last one.
        dictionary["demid#extra#chromines#create"] = c =>
        {
            var boss = SpawnCardBoss(c.GameBox, DemidBossIndex, DemidIdle, new Vector2(192, 92));
            boss.Header[0] |= RuntimeObject.FlagInvincible;
            SetBossHealthBar(c.GameBox, boss, true);
        };
        dictionary["demid#extra#window#create"] = c =>
        {
            var boss = SpawnCardBoss(c.GameBox, DemidBossIndex, DemidWindowBoss, new Vector2(192, 72));
            SetBossHealthBar(c.GameBox, boss, true);
            boss.Header[0] |= RuntimeObject.FlagIsFinalBossChapter;   // the true last card: he dies for real here
            SpawnWindowFrame(c.GameBox);
            ShowBossSplash(c.GameBox, "demid.png");
        };
        ChapterActions = dictionary.ToFrozenDictionary();
    }

    public static void RebuildObjectActionsList()
    {
        var dictionary = new Dictionary<string, RuntimeObjectReferenceAction>();
        dictionary["__object!!!"] = (robj) =>
        {

        };
        dictionary["MysticalToilet"] = obj =>
        {
            int age = obj.Box.CurrentTick - obj.CreatedAt;
            if (age >= ToiletEscapeTick)
            {
                // Out of time: it stops wandering and climbs off the top of the screen with whatever it has
                // swallowed. Header[0x56] marks the escape so the die script knows not to hand the hoard back —
                // it is running off with it. No explicit removal is needed: rising past the top edge puts it
                // through GameBox's normal out-of-bounds cull, which runs the die script like any other removal.
                obj.Header[0x56] = 1;
                obj.Header[0] &= ~RuntimeObject.FlagIsMovingToTarget;   // drop any wander target still in flight
                obj.FloatingPoints[0x18] = MathUtil.MoveTowards(obj.FloatingPoints[0x18], -ToiletEscapeSpeed,
                    ToiletEscapeAcceleration);                    // 0x18 = Velocity Y; -Y is up
                obj.Y += obj.FloatingPoints[0x18];
                obj.RenderRotation = MathF.Sin(obj.Box.CurrentTick * .35f) * 1.1f;   // wobbles harder as it bolts
                if (age >= ToiletLifetimeTick)
                    obj.Box.RemoveObject(obj);   // backstop, in case something kept it inside the box
                return;
            }
            if (obj.Box.ChapterTick % obj.Header[0x55] == 0)
            {
                var rnd = new Random(obj.Box.CurrentTick);
                obj.SetMoveToTarget(4, new Vector2(rnd.Next(64, 320), rnd.Next(64, 128)));
            }
            obj.RenderRotation = MathF.Sin(obj.Box.ChapterTick * .125f) * .5f;
        };
        dictionary["MysticalToiletDie"] = obj =>
        {
            var box = obj.Box;
            // DieAction fires twice on death (once in the death branch, once inside RemoveObject); the singleton
            // field is our once-guard, so the reward drops exactly one piece.
            if (box.MysticalToilet != obj)
                return;
            box.MysticalToilet = null;
            // Retires the health bar: BossHealthOverlay drops itself once its target is flagged dead, and the
            // enemy death path never sets that (only the boss one does), so it would otherwise hang around
            // after the toilet is gone — including when it leaves by climbing off the top of the screen.
            obj.Header[0] |= RuntimeObject.FlagIsDied;
            List<RuntimeObject>? hoard = obj.SwallowedItems;
            obj.SwallowedItems = null;
            if (obj.Header[0x56] != 0)
            {
                // It ran out its clock and escaped over the top edge. It keeps what it swallowed and there is no
                // kill to reward — beating the timer is the whole point of shooting it.
                box.UpdateUI();
                return;
            }
            // Reward killing the network-spawned toilet with a bomb piece (collectable Type 7 -> BombsSpices).
            var piece = RuntimeObject.LoadFromFile(RuntimeObject.CollectableFEIs[7], box);
            piece.Position = obj.Position;
            piece.CollectableVelocity = new Vector2(0f, -2.5f);   // pops up, then gravity lets it fall to be caught
            box.AddObject(piece);
            // Everything it swallowed comes back: laid out evenly around a ring centred on the toilet, thrown
            // outward, and flagged to home in on the player from wherever they are — so the hoard is returned in
            // full without the player having to go and catch it. The list was taken and cleared above, so a
            // second call (DieAction fires twice per kill) cannot spit the same items out again.
            if (hoard != null)
            {
                for (int i = 0; i < hoard.Count; i++)
                {
                    float angle = MathF.Tau * i / hoard.Count;
                    var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    RuntimeObject item = hoard[i];
                    item.Position = obj.Position + outward * ToiletHoardRingRadius;
                    item.CollectableVelocity = outward * 1.5f;
                    item.Header[0] |= RuntimeObject.FlagHomingCollectable;
                    box.AddObject(item);
                }
            }
            box.UpdateUI();   // the hoard row in the UI strip goes away with the toilet
            // TODO: Play toilet die sound
        };
        dictionary["AkobShoot"] = (obj) =>
        {
            obj.Y -= obj.Speed;
        };
        dictionary["RainShoot"] = obj =>
        {
            var dir = Helper.GetDirection2(obj.FloatingPoints[6] * 180 / 3.14f);
            obj.FloatingPoints[0x7] *= 61f / 60f;
            obj.FloatingPoints[0x6] *= 45f / 60f;
            obj.FloatingPoints[0x5] = obj.FloatingPoints[0x6];
            obj.X += dir.X * obj.Speed;
            obj.Y += dir.Y * obj.Speed;
        };
        dictionary["MoveLinearByDirection"] = obj =>
        {
            var d = Helper.GetDirection(obj.FloatingPoints[0x6]);
            obj.X += obj.Speed * d.X;
            obj.Y += obj.Speed * d.Y;
        };
        // The pizza slice and its orange glow share this mover, so they stay glued together: enlarge in with an
        // elastic budge, recoil backward for a moment ("bounce back"), then launch along FacingRotation.
        dictionary["nikitab#pizza#move"] = obj =>
        {
            int age = Math.Clamp(obj.Box.CurrentTick - obj.CreatedAt, 0, 5);
            obj.EntranceScale = age >= 26 ? 1f : EaseOutElastic(age / 26f);
            obj.RenderRotation += 0.09f;   // the slice tumbles as it flies
            var d = Helper.GetDirection(obj.FacingRotation);
            if (age < 12)
                obj.Position -= d * (1f - age / 12f) * 1.1f;   // recoil away from the aim, decelerating
            else
                obj.Position += d * obj.Speed;                 // then fly toward where the player was
        };
        // Red zig-zag: weave the heading side to side around the base aim (kept in FacingRotation) as it travels.
        dictionary["nikitab#zigzag#move"] = obj =>
        {
            int age = obj.Box.CurrentTick - obj.CreatedAt;
            float ang = obj.FacingRotation + MathF.Sin(age * 0.34f) * 0.62f;
            var d = Helper.GetDirection(ang);
            obj.X += obj.Speed * d.X;
            obj.Y += obj.Speed * d.Y;
            obj.RenderRotation = ang;
        };
        dictionary["toilet#spell2#easy"] = obj =>
        {
            if (obj.Box.ChapterTick % 120 == 7)
            {
                var c = obj.Box.SpawnObject(6);
                c.X = obj.X;
                c.Y = obj.Y;
                c.Speed = 2 + Math.Clamp(obj.Box.Difficulty, 0, 3) * 0.5f;   // faster on harder difficulties
                c.FacingRotation = c.RenderRotation = MathF.PI / 2;
                c.Velocity = new Vector2(0, -1);
                obj.SetMoveToTarget(4, new Vector2(obj.X, 128));
            }
            else if (obj.Box.ChapterTick % 120 == 15)
            {
                obj.SetMoveToTarget(4, new Vector2(obj.X, 96));
            }
        };
        dictionary["toilet_bullet#spell2#easy"] = obj =>
        {
            obj.Velocity = MathUtil. Vector2MoveTowards(obj.Velocity, Helper.GetDirection(obj.Position, obj.Box.Player.Position), 0.01f);
            obj.Position += obj.Velocity * obj.Speed;
            obj.RenderRotation = Helper.FindAngle(Vector2.Zero, obj.Velocity);
        };
        dictionary["DirectionShoot"] = obj =>
        {
            
        };
        dictionary["nikitos#spell1"] = c =>
        {
            var time = c.Box.CurrentTick - c.CreatedAt + c.Header[0x5A];
            if (time % c.Header[0x50] == 0 && time > 0)
            {
                int diff = Math.Clamp(c.Box.Difficulty, 0, 3);
                float baseAngle = (float)(c.FloatingPoints[0x5c] + (Math.PI / 2) * (Math.Abs(c.FloatingPoints[0x5D] % 10) - 5) / 5);
                int count = 1 + diff;                       // 1..4 bullets, fanned out with difficulty
                for (int k = 0; k < count; k++)
                {
                    var d = c.Box.SpawnObject(0);
                    d.FacingRotation = d.RenderRotation = baseAngle + (k - (count - 1) / 2f) * 0.18f;
                    d.X = c.X;
                    d.Y = c.Y;
                    d.Speed = 6f + diff * 0.5f;             // and a touch faster
                }
                c.FloatingPoints[0x5D]++;
            }

            if (time % c.Header[0x51] == 0 && time > 0)
            {
                
            }

            // Smooth (eased) repositioning. SetMoveToTarget glides at a constant speed and then snaps to a stop,
            // which read as a jerky "transition"; instead ease the boss from where it is to the new spot over
            // ~60 ticks with a smoothstep, so it accelerates and decelerates. Scratch: 0x62/0x63 = start pos,
            // Header[0x5E] = transition start tick, Header[0x5F] = transition active.
            const int transitionTicks = 60;
            if (time % 300 == 250)
            {
                var rnd = new Random((int)(c.Box.Player.X + c.Box.Player.Y + c.Box.CurrentTick));
                var pos = new Vector2(rnd.Next(32, 352), rnd.Next(48, 96));
                c.FloatingPoints[0x62] = c.X;
                c.FloatingPoints[0x63] = c.Y;
                c.MoveTarget = pos;
                c.Header[0x5E] = c.Box.CurrentTick;
                c.Header[0x5F] = 1;
            }
            if (c.Header[0x5F] == 1)
            {
                int elapsed = c.Box.CurrentTick - c.Header[0x5E];
                float k = Math.Clamp(elapsed / (float)transitionTicks, 0f, 1f);
                float sk = k * k * (3f - 2f * k);   // smoothstep ease-in-out
                c.Position = Vector2.Lerp(new Vector2(c.FloatingPoints[0x62], c.FloatingPoints[0x63]), c.MoveTarget, sk);
                if (k >= 1f)
                    c.Header[0x5F] = 0;
            }
            if (time % 300 == 0)
            {
                c.Header[0x5B]++;
                c.Header[0x5A] += 360;
                c.Box.AddScreenEffect(new StrengthScreenEffect(c.Box, c.Position, 50, c.Box.GetTime(), c.Box.GetTime()+1, 0x00FF34, 0x00EE69));
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
                var direction = Helper.FindAngle(c.Position, new Vector2(c.Box.Player.X, c.Box.Player.Y));
                c.FloatingPoints[0x5C] = direction;
            }
        };
        dictionary["nikitos#spell2"] = c =>
        {

        };
        dictionary["nikitab#pizza#outline"] = c =>
        {
            var box = c.Box;
            int t = box.CurrentTick - c.CreatedAt;
            float angle = t;
            angle = angle / NikitabLastSpellFullCircleTime * MathF.PI * 2;
            double t2 = box.ChapterTick;
            t2 /= NikitabLastSpellFullCircleTime;
            if (t == NikitabLastSpellFullCircleTime)
            {
                c.Velocity = Helper.GetDirection(c.Position, box.Player.Position);
                c.Speed = 1 + MathF.Pow(box.Difficulty, 2.3f);
            }
            else if (t < NikitabLastSpellFullCircleTime)
            {
                double r = (t2 + 2);
                double v = r < 3 ? 2 : 1;
                double b1 = r > 4 ? 1 : 3.5;
                double g = r > 3 ? r + 1 : r + 2;
                double z = Math.Abs(v - g % (v * 2));
                float s = (float)(Math.Pow(z, .5) * b1);
                c.Position = new Vector2(192, 224) + Helper.GetDirection2(angle) * (s * .4f + .6f) * 200;
            }
            else
            {
                c.PersistOffscreen = false;
                c.Position += c.Velocity * (1 + MathF.Pow(box.Difficulty, 1.8f)) / 60 * 5;
            }
        };
        dictionary["nikitab#microspam"] = NikitabMicroSpam;
        // ---- Nikita Bukin's act ----
        dictionary["nikitab2#appear"] = NikitaAppear;
        // The big pizza Nikita rides in on: glued just under him while he flies up (both half-transparent), then
        // when he settles (move-to-target done) it detaches, falls away and fades out. Non-lethal, invincible.
        dictionary["nikitab#pizzamount#move"] = obj =>
        {
            RuntimeObject? boss = null;
            foreach (var o in obj.Box.BoxObjects)
                if ((o.Header[0] & RuntimeObject.FlagIsBoss) == RuntimeObject.FlagIsBoss && o.BossId == NikitaBossId)
                { boss = o; break; }
            bool riding = boss != null &&
                          (boss.Header[0] & RuntimeObject.FlagIsMovingToTarget) == RuntimeObject.FlagIsMovingToTarget;
            if (riding)
            {
                obj.X = boss.X;
                obj.Y = boss.Y + 46f;
                obj.RenderAlpha = 0.5f;
            }
            else
            {
                obj.Y += 3.4f;
                obj.RenderRotation += 0.03f;
                obj.RenderAlpha = MathF.Max(0f, obj.RenderAlpha - 0.015f);
            }
        };
        // A descending bullet that wobbles side to side around its spawn column — the "shaked lines".
        dictionary["nikitab#shakeline#move"] = obj =>
        {
            int age = obj.Box.CurrentTick - obj.CreatedAt;
            float phase = obj.FloatingPoints[0x30];
            obj.Y += obj.Speed;
            obj.X += MathF.Sin(age * 0.34f + phase) * 1.7f;
            obj.RenderRotation = MathF.Sin(age * 0.34f + phase) * 0.5f;
        };
        // Gray pentabullet rain that REACTS to Nikita's yellow "large" anchors (tagged RoleYellowLarge):
        //  • Normal+  — near an anchor it re-aims at the player and turns orange (once; marked so it won't loop).
        //  • Max      — touching an anchor shatters it into a burst of little violet microbullets.
        dictionary["nikitab#graypenta#move"] = obj =>
        {
            var box = obj.Box;
            var d = Helper.GetDirection(obj.FacingRotation);
            obj.X += obj.Speed * d.X;
            obj.Y += obj.Speed * d.Y;

            int diff = Math.Clamp(box.Difficulty, 0, 3);
            if (diff < 1) return;   // Easy: no reaction, just gray rain

            bool reacted = obj.Header[RoleHeaderIndex] == RoleReactedPenta;
            foreach (var o in box.BoxObjects)
            {
                if (o.Header[RoleHeaderIndex] != RoleYellowLarge) continue;
                float dist = MathUtil.Vector2Distance(obj.Position, o.Position);
                if (diff >= 3 && dist < (o.Collision + obj.Collision) * 0.5f + 3f)   // Max: touch -> crumble
                {
                    for (int s = 0; s < 6; s++)
                    {
                        var m = box.SpawnObject(NikitaVioletMicroIndex, 0x8A2BE2);
                        m.X = obj.X; m.Y = obj.Y;
                        m.FacingRotation = m.RenderRotation = s * (MathF.PI * 2f / 6f);
                        m.Speed = 1.6f;
                    }
                    box.RemoveObject(obj);
                    return;
                }
                if (!reacted && dist < 44f)   // Normal+: near -> retarget at player + turn orange (once)
                {
                    float aim = Helper.FindAngle(obj.Position, box.Player.Position);
                    var orange = box.SpawnObject(NikitaGrayPentaIndex, 0xFFA500);
                    orange.X = obj.X; orange.Y = obj.Y;
                    orange.FacingRotation = aim;                       // flies at the player
                    orange.RenderRotation = aim + MathF.PI;            // sprite flipped 180° (matches the gray rain)
                    orange.Speed = MathF.Max(obj.Speed, 2.6f) + diff * 0.15f;
                    orange.CreatedAt = box.CurrentTick;
                    orange.Header[RoleHeaderIndex] = RoleReactedPenta;
                    box.RemoveObject(obj);
                    return;
                }
            }
        };
        // Nokia 8 turret: toggles on/off every 2 s (120 ticks), starting OFF; the instant it switches ON it
        // fires a laser aimed at the player, whose ~1 s telegraph (60 ticks) is the delay before it bites.
        dictionary["nokia8#laser"] = obj =>
        {
            int tick = obj.Box.ChapterTick;
            if (tick < 0)
                return;
            int window = tick / 120;               // 2-second windows
            bool on = (window & 1) == 1;           // window 0 = OFF, 1 = ON, 2 = OFF, ...
            if (on && tick % 120 == 0)             // fire once, right when it turns ON
            {
                float ang = Helper.FindAngle(obj.Position, obj.Box.Player.Position);
                var laser = RuntimeObject.MakeLaser(obj.Box, obj.Position, ang, 520f, 8f, 60, 60, 15);
                obj.Box.AddObject(laser);
            }
        };
        // Ray turret: like nokia8#laser but fires a "ray" — a widening cone that fades along a finite length —
        // aimed at the player. Fires every 4 s; the ~1 s telegraph (60 ticks) is the tell before it bites.
        dictionary["ray"] = obj =>
        {
            int tick = obj.Box.ChapterTick;
            if (tick < 0)
                return;
            if (tick % 240 == 0)
            {
                float ang = Helper.FindAngle(obj.Position, obj.Box.Player.Position);
                var ray = RuntimeObject.MakeRay(obj.Box, obj.Position, ang, 300f, 14f, 36f, 60, 90, 20);
                obj.Box.AddObject(ray);
            }
        };
        // ---- EXTRA STAGE ----------------------------------------------------------------------------------
        // The bosses' default (template) behaviour is to just stand there; every card overrides UpdateAction
        // with its own attack in the chapter's create script. The movers below are likewise assigned at spawn,
        // and are named here so the entity templates in extra1.json have a valid script to load with.
        dictionary["dmitry#idle"] = DmitryIdle;
        dictionary["demid#idle"] = DemidIdle;
        // Stage 3, card 4: the complaints box and its moon (templates 26 / 27 in stage3.json).
        dictionary["dmitry#stage3#grievance"] = GrievanceBoxTick;
        dictionary["dmitry#stage3#moon"] = MoonTick;
        dictionary["dmitry#puff#move"] = DmitryPuffMove;
        dictionary["dmitry#exhaust#move"] = DmitryExhaustMove;
        dictionary["demid#leak#move"] = DemidLeakMove;
        dictionary["demid#obs#pixel"] = ObsPixelMove;
        dictionary["demid#window#pixel"] = WindowPixelMove;
        ObjectActions = dictionary.ToFrozenDictionary();
    }

    private const int NikitabLastSpellFullCircleTime = 180;
    private const int NikitabLastSpellCircleSpawnRate = 4;
}

public delegate void RuntimeChapterReferenceAction(RuntimeChapter chapter);
public delegate void RuntimeObjectReferenceAction(RuntimeObject obj);