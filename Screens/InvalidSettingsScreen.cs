using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Ease-of-access settings: a difficulty shortcut, the colour-grading sliders (contrast / brightness /
/// saturation / hue), the gamma chart, and the colour-blindness simulation picker. Every value lives in
/// <see cref="Configuration"/> and is applied as a full-screen pass when the backbuffer is presented
/// (see Runtime.Present), so changes here are visible immediately, menus included.
/// </summary>
internal class InvalidSettingsScreen : MenuScreen
{
    // The adjustable rows, referenced directly so the left/right handler does not depend on positions.
    private MenuItem? ContrastItem, BrightnessItem, SaturationItem, HueItem;

    /// <summary>The gamma slider's range. 2.2 (a standard monitor's gamma) is the neutral default; the
    /// shader divides it out, so only the offset from 2.2 changes the picture.</summary>
    internal const float MinGamma = 1.0f, MaxGamma = 4.4f;

    // The slider glyphs: a fixed-width bar like <====----->, so the row width never jumps as the value moves.
    private const int BarSegments = 16;

    internal static string Bar(float fraction)
    {
        int filled = (int)MathF.Round(Math.Clamp(fraction, 0f, 1f) * BarSegments);
        return "<" + new string('=', filled) + new string('-', BarSegments - filled) + ">";
    }

    // Value <-> bar-fraction mappings. Contrast/brightness/saturation run 0..2 around a neutral 1;
    // hue runs -180..180 degrees around 0.
    private static float GainToFraction(float gain) => gain / 2f;
    private static float FractionToGain(float fraction) => fraction * 2f;
    private static float HueToFraction(float degrees) => (degrees + 180f) / 360f;
    private static float FractionToHue(float fraction) => fraction * 360f - 180f;
    private static float GammaToFraction(float gamma) => (gamma - MinGamma) / (MaxGamma - MinGamma);
    internal static string GammaBar() => Bar(GammaToFraction(Configuration.Config.Gamma));

    /// <summary>The colour-blindness modes on offer, in pick order.</summary>
    private static readonly ColorBlindMode[] ColorBlindModes =
    [
        ColorBlindMode.Normal, ColorBlindMode.Protanopia, ColorBlindMode.Deuteranopia,
        ColorBlindMode.Tritanopia, ColorBlindMode.Tritanomaly, ColorBlindMode.Deuteranomaly,
        ColorBlindMode.Achromatopsia,
    ];

    /// <summary>A mode's display label: the invalid.colorblind.* entry in translation.json whose key
    /// suffix is the enum member's name, lowercased.</summary>
    private static string ColorBlindLabel(ColorBlindMode mode) =>
        Helper.Translate($"invalid.colorblind.{mode.ToString().ToLowerInvariant()}");

    public InvalidSettingsScreen()
    {
    }

    public override void Exiting()
    {
        Configuration.Config.Save();
        base.Exiting();
    }

    /// <summary>Restores this screen's values (not the whole config) and refreshes every row's bar.</summary>
    private void ResetToDefaults()
    {
        Configuration.Config.Contrast = 1f;
        Configuration.Config.Brightness = 1f;
        Configuration.Config.Saturation = 1f;
        Configuration.Config.Hue = 0f;
        Configuration.Config.Gamma = 2.2f;
        Configuration.Config.ColorBlind = ColorBlindMode.Normal;
        Configuration.Config.Save();
        foreach ((MenuItem? item, Func<float> get, Action<float> set) in Sliders())
            if (item != null)
                set(get());
    }

    public override void CreateMenu()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);

        // Difficulty shortcut, same as the main menu's start entry.
        MenuItems.Add(new MenuItem("menu.start", "", a => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(GameType.Default))));

        ContrastItem = new MenuItem("invalid.contrast", Bar(GainToFraction(Configuration.Config.Contrast)), a => { });
        MenuItems.Add(ContrastItem);
        BrightnessItem = new MenuItem("invalid.brightness", Bar(GainToFraction(Configuration.Config.Brightness)), a => { });
        MenuItems.Add(BrightnessItem);
        SaturationItem = new MenuItem("invalid.saturation", Bar(GainToFraction(Configuration.Config.Saturation)), a => { });
        MenuItems.Add(SaturationItem);
        HueItem = new MenuItem("invalid.hue", Bar(HueToFraction(Configuration.Config.Hue)), a => { });
        MenuItems.Add(HueItem);

        // Gamma gets its own screen: the reference picture has to be seen while the value moves.
        MenuItems.Add(new MenuItem("invalid.gamma", "", a => Runtime.CurrentRuntime.AddScreen(new GammaScreen())));

        MenuItems.Add(new MenuItem("invalid.colorblind", "", a => OpenColorBlindList()));

        // Resets only THIS screen's values, not the whole config.
        MenuItems.Add(new MenuItem("settings.default", "", a => ResetToDefaults()));

        MenuItems.Add(new MenuItem("controller.back", "", a => Exit()));

        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 192);
    }

    /// <summary>Opens the mode picker; the selection is both persisted and shown by the whole game at once.</summary>
    private void OpenColorBlindList()
    {
        Runtime.CurrentRuntime.AddScreen(new ListSelectScreen(
            Runtime.CurrentRuntime.Textures["settings.png"],
            ColorBlindModes.Select(m => (ColorBlindLabel(m), (System.Action)(() =>
            {
                Configuration.Config.ColorBlind = m;
                Configuration.Config.Save();
            }))), windowed: true, headerKey: "invalid.colorblind"));
    }

    public override void Render()
    {
        float time = (float)GetTime();
        CurrentY = (int)(Runtime.CurrentRuntime.Height * (1 - Helper.EaseInOutElasticF((float)(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f) * 0.5))));
        DrawBackground();
        DrawMenu();
        DrawTitle();
    }

    /// <summary>
    /// The value-nudge rows and how to read/write them as a 0..1 fraction, so the bars and touch-drag
    /// drive them uniformly. Gamma is NOT here — it is adjusted on its own chart screen.
    /// </summary>
    private IEnumerable<(MenuItem? Item, Func<float> Get, Action<float> Set)> Sliders()
    {
        yield return (ContrastItem, () => GainToFraction(Configuration.Config.Contrast), f =>
        {
            Configuration.Config.Contrast = FractionToGain(f);
            ContrastItem!.Replace = Bar(f);
            Configuration.Config.Save();
        });
        yield return (BrightnessItem, () => GainToFraction(Configuration.Config.Brightness), f =>
        {
            Configuration.Config.Brightness = FractionToGain(f);
            BrightnessItem!.Replace = Bar(f);
            Configuration.Config.Save();
        });
        yield return (SaturationItem, () => GainToFraction(Configuration.Config.Saturation), f =>
        {
            Configuration.Config.Saturation = FractionToGain(f);
            SaturationItem!.Replace = Bar(f);
            Configuration.Config.Save();
        });
        yield return (HueItem, () => HueToFraction(Configuration.Config.Hue), f =>
        {
            Configuration.Config.Hue = FractionToHue(f);
            HueItem!.Replace = Bar(f);
            Configuration.Config.Save();
        });
    }

    // A tall touch strip over each slider row, so dragging anywhere along it sets the value. In unscaled units.
    private const float SliderWidth = 340f;

    /// <summary>Drives the slider under the finger (only when touch is the input method).</summary>
    private void UpdateTouchSliders()
    {
        if (IsManualScrolling)
            return;
        if (!TouchActive || !TryGetTouchPoint(out Vector2 p))
            return;
        float barW = SliderWidth * Runtime.CurrentRuntime.ScaleF;
        foreach ((MenuItem? item, Func<float> _, Action<float> set) in Sliders())
        {
            if (item == null)
                continue;
            Rect b = ItemBounds(MenuItems.IndexOf(item));
            if (b.Width <= 0)
                continue;
            if (p.Y >= b.Y && p.Y <= b.Y + b.Height && p.X >= CurrentX && p.X <= CurrentX + barW)
            {
                set(Math.Clamp((p.X - CurrentX) / barW, 0f, 1f));
                PreviousKeyTimestamp = GetTime();
                return;
            }
        }
    }

    public override void TopUpdate()
    {
        base.TopUpdate();
        UpdateTouchSliders();
        double time = GetTime();
        MenuItem selected = SelectedIndex >= 0 && SelectedIndex < MenuItems.Count ? MenuItems[SelectedIndex] : null!;

        if (time > PreviousKeyTimestamp + MenuSwitchCooldown)
        {
            float delta = 0;
            if (Controller.IsButtonDown(PadButton.LeftFaceLeft) || IsKeyDown(KeyCode.Left))
                delta -= .05f;
            if (Controller.IsButtonDown(PadButton.LeftFaceRight) || IsKeyDown(KeyCode.Right))
                delta += .05f;
            if (delta == 0)
                return;
            AnimationStartedAt = PreviousKeyTimestamp = time;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);

            foreach ((MenuItem? item, Func<float> get, Action<float> set) in Sliders())
            {
                if (selected != item)
                    continue;
                set(Math.Clamp(get() + delta, 0f, 1f));
                return;
            }
        }
    }
}
