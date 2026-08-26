using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using System.Numerics;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// The gamma chart, opened from the ease-of-access settings. Dmitry and Demid pose on a plain black
/// background while left/right moves <see cref="Configuration.Gamma"/>; because the ease-of-access
/// grading runs in the present pass, the picture shifts live as the value does. The attention line at
/// the bottom tells the player what to look for. Escape/X leaves.
/// </summary>
internal class GammaScreen : Screen
{
    private BasicTexture DmitryTexture, DemidTexture;
    private RenderedTexture AttentionTexture;
    private RenderedTexture ValueTexture;
    private float RenderedGamma = float.MinValue;

    protected override void Created()
    {
        // Shared assets from the startup scan — do NOT unload them in Unload().
        DmitryTexture = Runtime.CurrentRuntime.Textures["dmitry_top.png"];
        DemidTexture = Runtime.CurrentRuntime.Textures["demid_2.png"];
        AttentionTexture = Helper.DrawTextScaled(Helper.Translate("invalid.gamma.attention"), 16, 8, 4, 2,
            Runtime.CurrentRuntime.Fonts["newsreader"], "outline");
    }

    public override void TopUpdate()
    {
        double time = GetTime();
        if (time > MenuScreen.PreviousKeyTimestamp + MenuScreen.MenuSwitchCooldown)
        {
            float delta = 0;
            if (Controller.IsButtonDown(PadButton.LeftFaceLeft) || IsKeyDown(KeyCode.Left))
                delta -= .05f;
            if (Controller.IsButtonDown(PadButton.LeftFaceRight) || IsKeyDown(KeyCode.Right))
                delta += .05f;
            if (delta != 0)
            {
                MenuScreen.PreviousKeyTimestamp = time;
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
                Configuration.Config.Gamma = Math.Clamp(Configuration.Config.Gamma + delta,
                    InvalidSettingsScreen.MinGamma, InvalidSettingsScreen.MaxGamma);
                Configuration.Config.Save();
            }
        }

        if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) ||
            Controller.IsButtonDown(PadButton.RightFaceRight))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
            TimeDisappear = (float)GetTime();
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
    }

    public override void Render()
    {
        float appear = (float)Helper.ComputeObjectTime(GetTime(), TimeAppear, .5f, TimeDisappear, .5f);
        ClearBackground(Rgba.Black);

        // The two portraits side by side, centred, sized to the strip between the value label and the
        // attention line.
        float top = Runtime.CurrentRuntime.Height * 0.22f;
        float bottom = Runtime.CurrentRuntime.Height - 64 * Runtime.CurrentRuntime.ScaleF;
        float stripHeight = bottom - top;
        float gap = 32 * Runtime.CurrentRuntime.ScaleF;
        float dmitryScale = stripHeight / DmitryTexture.Height;
        float demidScale = stripHeight / DemidTexture.Height;
        float totalWidth = DmitryTexture.Width * dmitryScale + gap + DemidTexture.Width * demidScale;
        float x = (Runtime.CurrentRuntime.Width - totalWidth) / 2f;
        Rgba tint = Rgba.White with { A = (byte)(255 * appear) };

        DrawTextureEx(DmitryTexture, new Vector2(x, top), 0, dmitryScale, tint);
        DrawTextureEx(DemidTexture, new Vector2(x + DmitryTexture.Width * dmitryScale + gap, top), 0, demidScale, tint);

        // The current value with its bar, re-baked only when it moves.
        if (RenderedGamma != Configuration.Config.Gamma)
        {
            RenderedGamma = Configuration.Config.Gamma;
            if (ValueTexture.Id != 0)
                UnloadRenderTexture(ValueTexture);
            ValueTexture = Helper.DrawTextScaled(
                $"{Helper.Translate("invalid.gamma")} {RenderedGamma:0.00} {InvalidSettingsScreen.GammaBar()}",
                16, 8, 4, 2, Runtime.CurrentRuntime.Fonts["newsreader"], "outline");
        }
        int cx = Runtime.CurrentRuntime.Width / 2;
        if (ValueTexture.Id != 0)
            DrawTexture(ValueTexture.Texture, cx - ValueTexture.Texture.Width / 2,
                (int)(top - ValueTexture.Texture.Height - 8 * Runtime.CurrentRuntime.ScaleF), tint);

        // The attention line, centred at the bottom.
        if (AttentionTexture.Id != 0)
            DrawTexture(AttentionTexture.Texture, cx - AttentionTexture.Texture.Width / 2,
                (int)(Runtime.CurrentRuntime.Height - AttentionTexture.Texture.Height - 16 * Runtime.CurrentRuntime.ScaleF), tint);
    }

    public override void Unload()
    {
        if (AttentionTexture.Id != 0)
            UnloadRenderTexture(AttentionTexture);
        if (ValueTexture.Id != 0)
            UnloadRenderTexture(ValueTexture);
        base.Unload();
    }
}
