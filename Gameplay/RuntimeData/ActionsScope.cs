using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Collections.Frozen;
using System.Numerics;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public static class ActionsScope
{
    public static FrozenDictionary<string, RuntimeChapterReferenceAction> ChapterActions;
    public static FrozenDictionary<string, RuntimeObjectReferenceAction> ObjectActions;

    // Yellow, brown, green-yellow — the palette for the two-toilet colour-spam spell (Hard & Max only).
    private static readonly int[] SpamColors = { 0xFFFF00, 0x8B4513, 0xADFF2F };

    // The Nikitab boss and its bullets live in stage 2's entity table at these indices (added to
    // Assets/Data/StagesJson/stage2.json). Kept as named constants so the boss action reads clearly.
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
            g.FacingRotation = g.RenderRotation = MathF.PI / 2f;   // point the sprite down, the way it travels
            g.Speed = 2.2f + diff * 0.2f;
            g.CreatedAt = c.Box.CurrentTick;
        }
        c.RenderRotation = MathF.Sin(tick * 0.08f) * 0.1f;
    };

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

        // Aimed pentabullets at the player, faster/wider with difficulty.
        if (tick % Math.Max(8, 26 - diff * 5) == 0)
        {
            float aim = Helper.FindAngle(c.Position, c.Box.Player.Position);
            int count = 1 + diff;
            for (int k = 0; k < count; k++)
            {
                var b = c.Box.SpawnObject(NikitosNonspellPentaIndex);
                b.X = c.X;
                b.Y = c.Y;
                b.FacingRotation = b.RenderRotation = aim + (k - (count - 1) / 2f) * 0.16f;
                b.Speed = 3f + diff * 0.5f;
            }
        }

        // Random light circle bullets, direction and timing straight from the tick hash.
        if ((TickHash(tick) & 15) == 0)
        {
            var b = c.Box.SpawnObject(NikitosNonspellLightIndex);
            b.X = c.X;
            b.Y = c.Y;
            float ang = TickHash(tick * 3) % 3600 / 3600f * (MathF.PI * 2f);
            b.FacingRotation = b.RenderRotation = ang;
            b.Speed = 1.4f + diff * 0.35f;
        }
        c.RenderRotation = MathF.Sin(tick * 0.1f) * 0.3f;
    };

    /// <summary>Nikitos second non-spell: the same attack, plus a steady rain of light bullets from the top.</summary>
    private static readonly RuntimeObjectReferenceAction NikitosNonspell2 = c =>
    {
        NikitosNonspell1(c);
        int tick = c.Box.ChapterTick;
        if (tick > 0 && tick % 5 == 0)
        {
            int diff = Math.Clamp(c.Box.Difficulty, 0, 3);
            var b = c.Box.SpawnObject(NikitosNonspellLightIndex);
            b.X = TickHash(tick * 7) % 384;
            b.Y = 0;
            b.FacingRotation = b.RenderRotation = MathF.PI / 2f;   // straight down
            b.Speed = 2f + diff * 0.4f;
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
            boss.Header[0] |= RuntimeObject.FlagIsFinalBossChapter;   // last card of the stage — dies for real at the end
            boss.UpdateAction = NikitaYellow;
        };
        // Stage 1 non-spells: spawn the nikitos boss and give it the non-spell attack (overriding its default
        // spell behaviour). Spawning the boss entity reuses the one already on screen, so it persists across the
        // boss's attacks.
        // SpawnObject(2) reuses the shared nikitos boss (all nikitos entities are BossId 0, same as the stage's
        // spell cards), so the same boss carries through the fight. Place it dead centre-top so it — and the
        // bullets it fires from its position — are on screen.
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
            // Reward killing the network-spawned toilet with a bomb piece (collectable Type 7 -> BombsSpices).
            var piece = RuntimeObject.LoadFromFile(RuntimeObject.CollectableFEIs[7], box);
            piece.Position = obj.Position;
            piece.CollectableVelocity = new Vector2(0f, -2.5f);   // pops up, then gravity lets it fall to be caught
            box.AddObject(piece);
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
            int age = obj.Box.CurrentTick - obj.CreatedAt;
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

            if (time % 300 == 250)
            {
                var rnd = new Random((int)(c.Box.Player.X + c.Box.Player.Y + c.Box.CurrentTick));
                var pos = new Vector2(rnd.Next(32, 352), rnd.Next(48, 96));
                c.SetMoveToTarget(3, pos);
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
        dictionary["nikitab#microspam"] = NikitabMicroSpam;
        // ---- Nikita Bukin's act ----
        dictionary["nikitab2#appear"] = NikitaAppear;
        // The big pizza Nikita rides in on: glued just under him while he flies up (both half-transparent), then
        // when he settles (move-to-target done) it detaches, falls away and fades out. Non-lethal, invincible.
        dictionary["nikitab#pizzamount#move"] = obj =>
        {
            RuntimeObject boss = null;
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
                    orange.FacingRotation = orange.RenderRotation = aim;
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
        ObjectActions = dictionary.ToFrozenDictionary();
    }
}

public delegate void RuntimeChapterReferenceAction(RuntimeChapter chapter);
public delegate void RuntimeObjectReferenceAction(RuntimeObject obj);