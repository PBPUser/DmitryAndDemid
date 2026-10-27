using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils.DualSense;

namespace DmitryAndDemid.Gameplay;

/// <summary>
/// Turns what is happening in the game into what the DualSense does: a colour on the lightbar, a life count on
/// the player LEDs, resistance on whichever triggers are bound to something, and a kick from the motors when the
/// player dies or bombs.
///
/// The game calls in from two directions. Events (<see cref="OnPlayerDeath"/> and friends) are fired from the
/// gameplay code where they happen, and <see cref="UpdateGameplay"/> runs once a frame while a run is on screen.
/// <see cref="Update"/> runs every frame regardless and is what decays a flash and falls back to the menu look —
/// so a screen that never heard of this class (the title, the music room, an ending) still gets a sane lightbar
/// instead of whatever colour the last run left behind.
///
/// Nothing here checks for a pad: every call into <see cref="DualSensePad"/> is a no-op without one.
/// </summary>
public static class DualSenseFeedback
{
    // The resting colours. Gameplay sits on a cool blue and warms towards cyan on focus; the menus breathe in
    // the violet the title screen is built around; the last life turns the whole thing to a warning red.
    private static readonly Rgba GameplayColor = new(20, 70, 220);
    private static readonly Rgba FocusColor = new(0, 190, 200);
    private static readonly Rgba LastLifeColor = new(200, 20, 40);
    private static readonly Rgba MenuColor = new(90, 20, 150);

    private static readonly Rgba DeathFlash = new(255, 30, 30);
    private static readonly Rgba BombFlash = new(255, 255, 255);
    private static readonly Rgba ExtendFlash = new(60, 255, 120);
    private static readonly Rgba SpellFlash = new(220, 60, 255);
    private static readonly Rgba CaptureFlash = new(255, 220, 80);
    private static readonly Rgba BossDeathFlash = new(255, 140, 0);
    private static readonly Rgba UnlockFlash = new(120, 220, 255);
    private static readonly Rgba MenuMoveFlash = new(180, 120, 255);
    private static readonly Rgba GrazeGlowColor = new(120, 255, 255);

    /// <summary>
    /// How loud a piece of feedback is allowed to be relative to everything else. The mixer only lets a request
    /// interrupt one that is still playing if it is at least as important — otherwise a graze tick arriving two
    /// frames into a death rumble would cut the death short, which is exactly backwards.
    /// </summary>
    private enum Weight
    {
        /// <summary>Constant background chatter: grazing, items streaming in, the menu cursor.</summary>
        Chatter = 0,
        /// <summary>Something the player did on purpose, or a small reward.</summary>
        Notice = 1,
        /// <summary>A beat of the run: a bomb, a captured card, a boss going down.</summary>
        Event = 2,
        /// <summary>Death and game over. Nothing talks over these.</summary>
        Critical = 3,
    }

    private static Rgba FlashColor;
    private static long FlashStartMs, FlashEndMs;
    private static Weight FlashWeight;

    // The motors are a single shared channel: the kernel plays one force-feedback effect at a time, so a request
    // that lands mid-effect replaces it outright. Tracking what is playing (and how important it was) is what
    // keeps a stream of graze taps from stomping the kick the player actually needs to feel.
    private static long RumbleEndMs;
    private static Weight RumbleWeight;

    /// <summary>When gameplay last reported in. Past this the lightbar goes back to its menu look.</summary>
    private static long LastGameplayMs = long.MinValue;

    private const long GameplayTimeoutMs = 250;

    private static Rgba BaseColor = MenuColor;
    private static int Lives = -1;

    public static void OnPlayerDeath()
    {
        Flash(DeathFlash, 700, Weight.Critical);
        Rumble(1.0f, 0.65f, 450, Weight.Critical);
    }

    public static void OnBomb()
    {
        Flash(BombFlash, 800, Weight.Event);
        Rumble(0.75f, 0.45f, 600, Weight.Event);
    }

    /// <summary>A whole extra life — the one collectable worth feeling.</summary>
    public static void OnExtend()
    {
        Flash(ExtendFlash, 600, Weight.Notice);
        Rumble(0.0f, 0.55f, 220, Weight.Notice);
    }

    public static void OnSpellCardStart()
    {
        Flash(SpellFlash, 900, Weight.Event);
        Rumble(0.35f, 0.2f, 300, Weight.Event);
    }

    /// <summary>A card survived — the good ending of a spell, and the one worth a celebratory shudder.</summary>
    public static void OnSpellCaptured()
    {
        Flash(CaptureFlash, 1100, Weight.Event);
        Rumble(0.3f, 0.8f, 500, Weight.Event);
    }

    /// <summary>A card ended badly (died on it, or ran the clock out): one short, flat, unhappy thud.</summary>
    public static void OnSpellFailed()
    {
        Flash(Scale(SpellFlash, 0.5f), 500, Weight.Notice);
        Rumble(0.5f, 0.0f, 180, Weight.Notice);
    }

    /// <summary>The boss of a chapter going down — the longest, heaviest roll in the game bar death itself.</summary>
    public static void OnBossDefeated()
    {
        Flash(BossDeathFlash, 1400, Weight.Event);
        Rumble(0.9f, 0.55f, 900, Weight.Event);
    }

    /// <summary>
    /// A boss's send-off, matched to which of the two exits it is playing. A destruct is a single heavy blast
    /// that decays with the slow motion; a retreat is lighter and longer — the boss pulling away rather than
    /// coming apart. Both run the length of the finale so the pad is part of the beat rather than a hit at the
    /// front of it.
    /// </summary>
    public static void OnBossFinale(bool destruct)
    {
        if (destruct)
        {
            Flash(BossDeathFlash, 1500, Weight.Event);
            Rumble(1.0f, 0.6f, 1500, Weight.Event);
        }
        else
        {
            Flash(Scale(BossDeathFlash, 0.7f), 1500, Weight.Event);
            Rumble(0.3f, 0.45f, 1500, Weight.Event);
        }
    }

    public static void OnGameOver()
    {
        Flash(DeathFlash, 1500, Weight.Critical);
        Rumble(0.85f, 0.15f, 1200, Weight.Critical);
    }

    /// <summary>Something got unlocked (a trophy, a track, a stage) — a bright, quick double-tick.</summary>
    public static void OnUnlock()
    {
        Flash(UnlockFlash, 900, Weight.Notice);
        Rumble(0.0f, 0.7f, 260, Weight.Notice);
    }

    /// <summary>Pausing dips the pad; unpausing snaps it back. Both are a single soft bump.</summary>
    public static void OnPauseToggled(bool paused)
    {
        Flash(paused ? Scale(MenuColor, 1.6f) : GameplayColor, 350, Weight.Notice);
        Rumble(paused ? 0.25f : 0.0f, paused ? 0.0f : 0.35f, 120, Weight.Notice);
    }

    // --- Menu haptics -------------------------------------------------------------------------------------
    // The cursor is the thing a player touches most, and it had nothing. These are deliberately tiny: a tick you
    // notice in your fingers rather than a buzz you notice in the room.

    /// <summary>One row of cursor travel.</summary>
    public static void OnMenuMove()
    {
        Flash(MenuMoveFlash, 140, Weight.Chatter);
        Rumble(0f, 0.22f, 22, Weight.Chatter);
    }

    /// <summary>An entry was chosen — firmer than a move, so confirm and travel never feel the same.</summary>
    public static void OnMenuConfirm()
    {
        Flash(Rgba.White, 260, Weight.Notice);
        Rumble(0.3f, 0.4f, 70, Weight.Notice);
    }

    /// <summary>Backing out: the same size as a confirm but on the heavy motor only, so it reads as lower.</summary>
    public static void OnMenuBack()
    {
        Flash(Scale(MenuColor, 1.8f), 200, Weight.Chatter);
        Rumble(0.28f, 0f, 55, Weight.Chatter);
    }

    // --- Coalesced chatter --------------------------------------------------------------------------------
    // Grazing and collecting fire many times a second — dozens, during a dense pattern or a bomb's worth of loot.
    // Playing one effect per event would mean an ioctl per bullet and a motor restarted so often it never spins
    // up. Instead the events are COUNTED here and drained once per window in Update: the count sets the strength,
    // so a light brush is a single tap and a wall of bullets becomes a sustained hum, at a fixed cost either way.

    private static int PendingGrazes, PendingPickups;
    private static long LastChatterMs;
    private const long ChatterIntervalMs = 60;

    /// <summary>A bullet was grazed. Counted, not played — see the note above.</summary>
    public static void OnGraze() => PendingGrazes++;

    /// <summary>A collectable was picked up. Counted, not played.</summary>
    public static void OnItemCollect() => PendingPickups++;

    /// <summary>
    /// How hot the grazing is right now, 0..1, rising with each bullet brushed and bleeding away over about a
    /// second. Drives a cyan wash over the gameplay lightbar colour, so the pad literally glows brighter the
    /// closer the player is flying — a reading of the run that needs no HUD.
    /// </summary>
    private static float GrazeGlow;
    private static long LastGlowMs;

    private static void DrainChatter(long now)
    {
        if (now - LastChatterMs < ChatterIntervalMs)
            return;
        int grazes = PendingGrazes, pickups = PendingPickups;
        PendingGrazes = PendingPickups = 0;
        LastChatterMs = now;
        if (grazes == 0 && pickups == 0)
            return;

        // Saturating on purpose: five bullets in one window already feels like "a lot", and everything past that
        // would only push a motor that is nearly flat out anyway.
        float grazeAmount = MathF.Min(grazes / 5f, 1f);
        float pickupAmount = MathF.Min(pickups / 6f, 1f);
        GrazeGlow = MathF.Min(GrazeGlow + grazeAmount * 0.7f, 1f);
        // Graze is the high-frequency motor (a sharp scrape past the hitbox), pickup the low one (a soft patter
        // of things landing), so a stream of loot and a stream of near-misses do not feel like the same event.
        float weak = 0.10f + grazeAmount * 0.30f;
        float strong = pickups > 0 ? 0.06f + pickupAmount * 0.14f : 0f;
        if (grazes == 0)
            weak = 0f;
        Rumble(strong, weak, (int)ChatterIntervalMs + 20, Weight.Chatter);
    }

    /// <summary>Called once a frame while a run is on screen, from the gameplay screen's per-frame update.</summary>
    public static void UpdateGameplay(GameBox box)
    {
        LastGameplayMs = Environment.TickCount64;

        Player player = box.Player;
        // Lives are counted from zero: 0 on the gauge still means one more attempt, so the LEDs show one more
        // than the number to match what the player reads off the life row.
        int lives = Math.Max(player.LivesValue + 1, 0);
        if (lives != Lives)
        {
            Lives = lives;
            DualSensePad.SetPlayerLives(lives);
        }

        Rgba color = lives <= 1 ? LastLifeColor : player.IsFocused ? FocusColor : GameplayColor;
        // Grazing washes the resting colour towards cyan, in proportion to how hard the player is pushing it.
        if (GrazeGlow > 0.01f)
            color = Blend(color, GrazeGlowColor, GrazeGlow * 0.8f);
        // A paused game dims rather than switching colour, so the lightbar reads as "the same run, on hold".
        BaseColor = box.IsPaused ? Scale(color, 0.25f) : color;
        DualSensePad.SetTriggers(LeftTriggerEffect(), RightTriggerEffect());
    }

    /// <summary>Called every frame from the main loop, whatever is on screen.</summary>
    public static void Update()
    {
        long now = Environment.TickCount64;
        DrainChatter(now);
        DecayGrazeGlow(now);
        if (now - LastGameplayMs > GameplayTimeoutMs)
        {
            // Out of gameplay: breathe the menu colour and let the triggers go, so the pad is not stiff while
            // the player is picking a character.
            // The sine is taken in double: TickCount64 is milliseconds since boot, which on a machine that has
            // been up for a few weeks is far past the point where a float can still tell one millisecond from
            // the next — the breath would visibly stutter, then stop.
            float breath = 0.55f + 0.45f * (float)Math.Sin(now / 900.0);
            BaseColor = Scale(MenuColor, breath);
            if (Lives != -1)
            {
                Lives = -1;
                DualSensePad.SetPlayerLives(0);
            }
            // Grazing belongs to a run; leaving the glow up would tint the menus cyan after the run ended.
            GrazeGlow = 0f;
            PendingGrazes = PendingPickups = 0;
            DualSensePad.SetTriggers(TriggerEffect.Off, TriggerEffect.Off);
        }

        DualSensePad.SetLightbar(CurrentColor(now));
        DualSensePad.Poll();
    }

    /// <summary>Bleeds the graze wash away in real time (~1 s from full), independent of framerate.</summary>
    private static void DecayGrazeGlow(long now)
    {
        long elapsed = LastGlowMs == 0 ? 0 : now - LastGlowMs;
        LastGlowMs = now;
        if (GrazeGlow <= 0f)
            return;
        GrazeGlow = MathF.Max(0f, GrazeGlow - Math.Clamp(elapsed, 0, 200) / 1000f);
    }

    private static Rgba CurrentColor(long now)
    {
        if (now >= FlashEndMs)
            return BaseColor;
        // Flashes fade out linearly over their lifetime rather than cutting off, which reads as a pulse of light
        // rather than a colour change.
        float remaining = (FlashEndMs - now) / (float)Math.Max(FlashEndMs - FlashStartMs, 1);
        return Blend(BaseColor, FlashColor, remaining);
    }

    private static void Flash(Rgba color, int milliseconds, Weight weight)
    {
        long now = Environment.TickCount64;
        // A menu tick must not wipe the red still fading off a death. Same weight replaces, lower is dropped.
        if (now < FlashEndMs && weight < FlashWeight)
            return;
        FlashColor = color;
        FlashStartMs = now;
        FlashEndMs = FlashStartMs + milliseconds;
        FlashWeight = weight;
    }

    /// <summary>
    /// The one way anything here reaches the motors. Only one force-feedback effect plays at a time, so a request
    /// arriving while a more important one is still running is dropped rather than cutting it off.
    /// </summary>
    private static void Rumble(float strong, float weak, int milliseconds, Weight weight)
    {
        long now = Environment.TickCount64;
        if (now < RumbleEndMs && weight < RumbleWeight)
            return;
        RumbleEndMs = now + milliseconds;
        RumbleWeight = weight;
        DualSensePad.Rumble(strong, weak, milliseconds);
    }

    /// <summary>
    /// Trigger resistance follows the BINDINGS, not the hardware: a trigger the player has nothing bound to stays
    /// free, and one that fires, bombs or focuses gets a feel that matches the action. Otherwise the pad would sit
    /// there stiffening triggers the game never reads.
    /// </summary>
    private static TriggerEffect LeftTriggerEffect() => EffectFor(PadButton.LeftTrigger2);

    private static TriggerEffect RightTriggerEffect() => EffectFor(PadButton.RightTrigger2);

    private static TriggerEffect EffectFor(PadButton trigger)
    {
        Configuration config = Configuration.Config;
        if (config.FocusButton == trigger)
            return TriggerEffect.Rigid(0x60, 0x70);          // a firm ledge you hold against while focused
        if (config.ShootButton == trigger)
            return TriggerEffect.Pulse(0x30, 0x80, 0x50);    // light, gives way — it is held for whole stages
        if (config.BombButton == trigger)
            return TriggerEffect.Rigid(0x90, 0xC0);          // heavy: a bomb should take a deliberate pull
        return TriggerEffect.Off;
    }

    private static Rgba Scale(Rgba color, float factor) => new(
        (byte)Math.Clamp(color.R * factor, 0, 255),
        (byte)Math.Clamp(color.G * factor, 0, 255),
        (byte)Math.Clamp(color.B * factor, 0, 255));

    private static Rgba Blend(Rgba from, Rgba to, float amount)
    {
        float t = Math.Clamp(amount, 0f, 1f);
        return new Rgba(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }
}
