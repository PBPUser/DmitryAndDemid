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

    private static Rgba FlashColor;
    private static long FlashStartMs, FlashEndMs;

    /// <summary>When gameplay last reported in. Past this the lightbar goes back to its menu look.</summary>
    private static long LastGameplayMs = long.MinValue;

    private const long GameplayTimeoutMs = 250;

    private static Rgba BaseColor = MenuColor;
    private static int Lives = -1;

    public static void OnPlayerDeath()
    {
        Flash(DeathFlash, 700);
        DualSensePad.Rumble(1.0f, 0.65f, 450);
    }

    public static void OnBomb()
    {
        Flash(BombFlash, 800);
        DualSensePad.Rumble(0.75f, 0.45f, 600);
    }

    /// <summary>A whole extra life — the one collectable worth feeling.</summary>
    public static void OnExtend()
    {
        Flash(ExtendFlash, 600);
        DualSensePad.Rumble(0.0f, 0.55f, 220);
    }

    public static void OnSpellCardStart()
    {
        Flash(SpellFlash, 900);
        DualSensePad.Rumble(0.35f, 0.2f, 300);
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
        // A paused game dims rather than switching colour, so the lightbar reads as "the same run, on hold".
        BaseColor = box.IsPaused ? Scale(color, 0.25f) : color;
        DualSensePad.SetTriggers(LeftTriggerEffect(), RightTriggerEffect());
    }

    /// <summary>Called every frame from the main loop, whatever is on screen.</summary>
    public static void Update()
    {
        long now = Environment.TickCount64;
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
            DualSensePad.SetTriggers(TriggerEffect.Off, TriggerEffect.Off);
        }

        DualSensePad.SetLightbar(CurrentColor(now));
        DualSensePad.Poll();
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

    private static void Flash(Rgba color, int milliseconds)
    {
        FlashColor = color;
        FlashStartMs = Environment.TickCount64;
        FlashEndMs = FlashStartMs + milliseconds;
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
