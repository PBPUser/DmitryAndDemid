using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// The manual. Two states:
///   * MENU — a list of the pages (manual.page1..9). Up/Down move the cursor; Enter opens the highlighted page.
///   * VIEW — the page image (manual-1.png..manual-9.png) shown large. Up/Down step to the previous/next page,
///     looping; Escape/back returns to the menu.
/// Opening a page slides the menu off to the right and pops the image up from the centre with an overshoot
/// (ease-out-back) bounce; leaving reverses it.
/// </summary>
public class ManualScreen : MenuScreen
{
    private const int PageCount = 9;
    private const float TransitionDuration = 0.45f;
    private const float PagePopDuration = 0.25f;

    private bool InView;
    private double TransitionTime = double.MinValue;   // when the menu<->view transition last started
    private double PagePopTime = double.MinValue;      // when the current page was last (re)shown, for its pop
    private int CurrentPage;
    private int BaseX;

    public override void CreateMenu()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        for (int i = 0; i < PageCount; i++)
            MenuItems.Add(new MenuItem($"manual.page{i + 1}", "", a => { }));
        // The selected page title enlarges and jitters (the base menu machinery, just turned up here).
        SelectedItemScale = 1.18f;
        SelectedNoise = new Vector2(6, 6) * Runtime.CurrentRuntime.ScaleF;
        BaseX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentX = BaseX;
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 96);
    }

    public override void TopUpdate()
    {
        if (GetTime() - PreviousKeyTimestamp < MenuSwitchCooldown)
            return;

        if (InView)
            ViewInput();
        else
            MenuInput();
    }

    private void MenuInput()
    {
        if (IsKeyDown(KeyCode.Up) || Controller.IsButtonDown(PadButton.LeftFaceUp))
            Move(-1);
        else if (IsKeyDown(KeyCode.Down) || Controller.IsButtonDown(PadButton.LeftFaceDown))
            Move(1);
        else if (IsKeyDown(KeyCode.Enter) || IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(PadButton.RightFaceDown))
            OpenPage(SelectedIndex);
        else if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) || Controller.IsButtonDown(PadButton.RightFaceRight))
            ExitManual();
        else
            HandleMenuTouch();
    }

    private void ViewInput()
    {
        if (IsKeyDown(KeyCode.Up) || Controller.IsButtonDown(PadButton.LeftFaceUp))
            StepPage(-1);
        else if (IsKeyDown(KeyCode.Down) || Controller.IsButtonDown(PadButton.LeftFaceDown))
            StepPage(1);
        else if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) || Controller.IsButtonDown(PadButton.RightFaceRight))
            ClosePage();
        else
            HandleViewTouch();
    }

    private void Move(int direction)
    {
        PreviousKeyTimestamp = GetTime();
        // Restart the selection-change shake. The base TopUpdate does this on every move, but this screen
        // overrides TopUpdate, so without stamping the animation fields swapNoise stays 0 and the selected
        // page title never jitters the way the main menu's does.
        PreviousSelectedIndex = SelectedIndex;
        AnimationStartedIndex = SelectedIndex;
        AnimationStartedAt = GetTime();
        SelectedIndex = (SelectedIndex + direction + MenuItems.Count) % MenuItems.Count;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
    }

    private void OpenPage(int page)
    {
        PreviousKeyTimestamp = GetTime();
        CurrentPage = page;
        InView = true;
        TransitionTime = GetTime();
        PagePopTime = GetTime();
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
    }

    private void ClosePage()
    {
        PreviousKeyTimestamp = GetTime();
        InView = false;
        TransitionTime = GetTime();
        SelectedIndex = CurrentPage;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
    }

    /// <summary>
    /// Leaves the manual. This screen overrides TopUpdate and never calls the base, so the base's deferred
    /// exit (its ItemActivated/Event machinery) never runs — removing the screen directly is what actually
    /// makes Escape work here.
    /// </summary>
    private void ExitManual()
    {
        PreviousKeyTimestamp = GetTime();
        Exiting();
        Runtime.CurrentRuntime.RemoveScreen(this);
    }

    private void StepPage(int direction)
    {
        PreviousKeyTimestamp = GetTime();
        CurrentPage = (CurrentPage + direction + PageCount) % PageCount;
        PagePopTime = GetTime();
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
    }

    // Touch: tap a listed page to open it; in view, tap left/right third to step, and a downward swipe... kept
    // simple — the two side zones step pages and the top-left corner returns to the menu.
    private int PreviousTouches;

    private void HandleMenuTouch()
    {
        if (!TouchTapped(out Vector2 p))
            return;
        for (int i = 0; i < MenuItems.Count; i++)
        {
            Rect b = ItemBounds(i);
            if (b.Width > 0 && p.X >= b.X && p.X <= b.X + b.Width && p.Y >= b.Y && p.Y <= b.Y + b.Height)
            {
                SelectedIndex = i;
                OpenPage(i);
                return;
            }
        }
    }

    private void HandleViewTouch()
    {
        if (!TouchTapped(out Vector2 p))
            return;
        float w = Runtime.CurrentRuntime.Width, h = Runtime.CurrentRuntime.Height;
        if (p.Y < h * 0.18f && p.X < w * 0.3f)
            ClosePage();
        else if (p.X < w / 2f)
            StepPage(-1);
        else
            StepPage(1);
    }

    private bool TouchTapped(out Vector2 point)
    {
        point = default;
        int count = Engine.Input.TouchCount;
        bool tapped = count > 0 && PreviousTouches == 0;
        PreviousTouches = count;
        if (!tapped || !TryGetTouchPoint(out point))
            return false;
        return true;
    }

    /// <summary>0 in the menu, 1 fully in the page view; animates across a transition.</summary>
    private float ViewProgress()
    {
        float raw = (float)Math.Clamp((GetTime() - TransitionTime) / TransitionDuration, 0, 1);
        return InView ? raw : 1 - raw;
    }

    // Ease-out-back: overshoots ~10% before settling, which reads as a bounce.
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    public override void Render()
    {
        DrawBackground();

        // The menu slides fully off to the LEFT as the view opens, so the page has the screen to itself.
        float progress = ViewProgress();
        CurrentX = BaseX - (int)(progress * Runtime.CurrentRuntime.Width);
        DrawMenu();

        if (progress <= 0.001f)
            return;

        // The open/close bounce, times a short per-page pop when stepping pages within the view.
        float scale = EaseOutBack(progress);
        float pop = (float)Math.Clamp((GetTime() - PagePopTime) / PagePopDuration, 0, 1);
        scale *= 0.9f + 0.1f * EaseOutBack(pop);

        TextureHandle page = Runtime.CurrentRuntime.Textures[$"manual-{CurrentPage + 1}.png"];
        float screenW = Runtime.CurrentRuntime.Width, screenH = Runtime.CurrentRuntime.Height;

        // Fit the page (4:3) into most of the screen, preserving aspect, then apply the animation scale.
        float baseH = screenH * 0.82f;
        float baseW = baseH * page.Width / page.Height;
        if (baseW > screenW * 0.92f)
        {
            baseW = screenW * 0.92f;
            baseH = baseW * page.Height / page.Width;
        }
        float w = baseW * scale, h = baseH * scale;
        // A gentle up/down float once the page has settled into view.
        float bob = MathF.Sin((float)GetTime() * 1.6f) * 9f * Runtime.CurrentRuntime.ScaleF * progress;
        // A slight up-and-down nudge each time the user switches pages, decaying as the pop settles.
        float switchNudge = -MathF.Sin(pop * MathF.PI) * 12f * Runtime.CurrentRuntime.ScaleF;
        float cx = screenW / 2f, cy = screenH / 2f + bob + switchNudge;
        DrawTexturePro(page, new Rect(0, 0, page.Width, page.Height),
            new Rect(cx - w / 2f, cy - h / 2f, w, h), Vector2.Zero, 0, Rgba.White);

        // Page counter, only once the page is essentially settled.
        if (progress > 0.85f)
        {
            FontHandle font = Runtime.CurrentRuntime.Fonts["kodemono"];
            string label = $"{CurrentPage + 1} / {PageCount}";
            float size = 20 * Runtime.CurrentRuntime.ScaleF;
            Vector2 m = MeasureTextEx(font, label, size, 1);
            DrawTextEx(font, label, new Vector2(cx - m.X / 2f, cy + h / 2f + 6 * Runtime.CurrentRuntime.ScaleF),
                size, 1, Rgba.White);
        }
    }
}
