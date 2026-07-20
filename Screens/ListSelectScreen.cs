using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// A plain "pick one from a list" menu. Used for the settings that are a discrete choice rather than a value
/// to nudge — the resolution and the renderer — where a full list reads better than cycling a single row.
/// Picking an option runs its callback and closes the list.
///
/// When <paramref name="windowed"/> (see the constructor) is set, the list skips the usual full-screen
/// background/title and instead floats as a small liquid-glass panel centred over whatever screen opened it
/// (e.g. Settings stays visible, dimmed, behind the resolution picker).
/// </summary>
public class ListSelectScreen : MenuScreen
{
    private readonly TextureHandle TitleTexture;
    private readonly (string Label, System.Action OnSelect)[] Options;
    private readonly bool Windowed;
    private readonly string? HeaderKey;
    private TargetHandle? BackgroundCapture;

    public ListSelectScreen(TextureHandle title, IEnumerable<(string Label, System.Action OnSelect)> options, bool windowed = false, string? headerKey = null)
    {
        TitleTexture = title;
        Options = options.ToArray();
        Windowed = windowed;
        HeaderKey = headerKey;
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        SetTitle(TitleTexture);
        if (Windowed && HeaderKey != null)
            MenuItems.Add(new MenuItem(HeaderKey, "", null) { Enabled = false });
        foreach ((string label, System.Action onSelect) in Options)
        {
            System.Action select = onSelect;
            MenuItems.Add(new MenuItem(label, "", _ =>
            {
                select();
                Exit();
            }));
        }
        MenuItems.Add(new MenuItem("ingame.exit", "", _ => Exit()));
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 40);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 176);
    }

    // Runtime.RunFrame does PreRender() THEN Render() — PreRender is outside any active render target, which
    // is the only place this engine supports opening a new one (Render() is already nested inside the shared
    // Backbuffer target; GameBox.cs documents the same constraint for its laser-glow bake). So the real-time
    // background capture has to happen here, one step ahead of the glass panel that samples it in Render().
    public override void PreRender(double delta)
    {
        base.PreRender(delta);
        if (!Windowed)
            return;
        var below = Runtime.CurrentRuntime.GetScreenBelow(this);
        if (below == null)
            return;
        if (BackgroundCapture == null || BackgroundCapture.Value.Texture.Width != Runtime.CurrentRuntime.Width || BackgroundCapture.Value.Texture.Height != Runtime.CurrentRuntime.Height)
            BackgroundCapture = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        BeginTextureMode(BackgroundCapture.Value);
        ClearBackground(Rgba.Black);
        below.Render();
        EndTextureMode();
    }

    public override void Render()
    {
        if (!Windowed)
        {
            DrawBackground();
            DrawMenu();
            DrawTitle();
            return;
        }

        // Dim whatever screen opened this list instead of covering it, then float the options in a small
        // centred glass panel over it — sized to the content, since item textures (translated, variable width)
        // are only known once MenuItem.RenderItems() has run for this frame (Runtime.cs, before Render()).
        float appear = (float)Helper.ComputeObjectTime(GetTime(), TimeAppear, .35f, TimeDisappear, .35f);
        DrawRectangle(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, new Rgba(0, 0, 0, (byte)(140 * appear)));

        float maxItemWidth = 0f, totalHeight = 0f;
        foreach (var item in MenuItems)
        {
            maxItemWidth = MathF.Max(maxItemWidth, item.Texture.Width);
            totalHeight += item.Texture.Height;
        }
        float bounceAppear = Helper.EaseInOutElasticF(appear);

        float pad = 24 * Runtime.CurrentRuntime.ScaleF;
        float panelWidth = maxItemWidth + pad * bounceAppear * 2;
        float panelHeight = totalHeight + pad * bounceAppear  * 2;
        Vector2 center = new(Runtime.CurrentRuntime.Width / 2f, Runtime.CurrentRuntime.Height / 2f);

        Rect panel = new(center.X - panelWidth / 2, center.Y - panelHeight / 2, panelWidth, panelHeight);
        float radius = ((1 - bounceAppear) * 80 + 20) * Runtime.CurrentRuntime.ScaleF;

        // BackgroundCapture was filled a moment ago in PreRender(), outside the render target this Render()
        // call is already nested in.
        if (BackgroundCapture != null)
            Helper.DrawLiquidGlassPanel(BackgroundCapture.Value, panel, radius, new Rgba(255, 255, 255, (byte)(235 * appear)));

        CurrentX = (int)(panel.X + pad);
        CurrentY = (int)(panel.Y + pad);
        DrawMenu();
    }

    public override void Unload()
    {
        if (BackgroundCapture != null)
            UnloadRenderTexture(BackgroundCapture.Value);
        base.Unload();
    }
}
