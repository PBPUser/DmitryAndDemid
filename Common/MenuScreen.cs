using DmitryAndDemid.Rendering;
using System.Globalization;
using System.IO.Pipes;
using System.Numerics;
using DmitryAndDemid;
using static DmitryAndDemid.Rendering.Gfx;
using static DmitryAndDemid.Runtime;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Common;

public abstract class MenuScreen : ScreenWithTitle
{
    public const double MenuSwitchCooldown = 0.125;
    public const double MenuActivateCooldown = 0.5;
    
    protected bool AllowExitWithEscape = true;
    protected bool LoopList = true;
    protected bool HorizontalDirectionNavigation = false;
    protected bool VerticalDirectionNavigation = true;
    protected int CurrentY = 0;
    protected int CurrentX = 0;
    protected int SelectedIndex = 0;
    /// <summary>When set, a list longer than the visible area is drawn as a window of rows that follows the
    /// selection instead of overflowing off the bottom of the screen. Opt-in per screen so menus that already
    /// fit (main menu, difficulty, pause…) are unaffected. Used by Settings, Music Room and the list-select.</summary>
    protected bool EnableScrolling = false;
    /// <summary>When &gt; 0 (and <see cref="EnableScrolling"/> is on) the visible window is capped to exactly
    /// this many rows regardless of how much vertical room there is; 0 means "as many as fit". Keeps the window
    /// a fixed height even while the appear animation slides <see cref="CurrentY"/> around. The music room uses
    /// 7.</summary>
    protected int MaxVisibleItems = 0;
    /// <summary>Index of the first item in the visible window when <see cref="EnableScrolling"/> windows the
    /// list. Only moves when the selection leaves the window, so the cursor travels within the window and the
    /// list scrolls at its edges.</summary>
    private int ScrollFirstIndex = 0;
    /// <summary>Eased, fractional form of <see cref="ScrollFirstIndex"/> — the list slides toward the target
    /// each frame instead of jumping, giving smooth scrolling.</summary>
    private float ScrollFirstFloat = 0;

    // --- Touch drag-to-scroll (windowed menus only) ---------------------------------------------------------
    // A vertical finger drag scrolls the list directly, decoupled from the selection; a touch that never moves
    // far enough to become a drag falls through to tap-to-select on release. These are fed from the last
    // DrawMenu, which is the only place that knows the row height and window size.
    private float LastItemHeight;   // row height (game px) recorded by the last DrawMenu
    private int LastMaxVisible;     // visible-window row count recorded by the last DrawMenu
    private bool LastWindowed;      // whether the last DrawMenu actually windowed (i.e. list overflows)
    private Vector2 DragStartPoint; // finger position (game coords) when the gesture began
    private Vector2 LastTouchPoint; // most recent finger position (game coords), for tap-on-release hit-testing
    private float DragStartScroll;  // ScrollFirstFloat at the moment the finger went down
    private bool DragActive;        // this gesture has crossed the threshold and is now a scroll drag
    /// <summary>True while a finger is actively drag-scrolling a windowed list. Screens with their own touch
    /// handling (e.g. the settings sliders) check this to stand down so a scroll does not also nudge a value.</summary>
    protected bool IsManualScrolling { get; private set; }
    protected float SelectedItemScale = 1f;
    protected double AnimationStartedAt = 0;
    protected double AnimationStartedIndex = 0;
    /// <summary>Max on-screen width (game px, 1x) a menu item may occupy before it is horizontally scrolled.
    /// 0 = auto: everything from <see cref="CurrentX"/> to the right edge. Screens with content beside the list
    /// (e.g. the music room's description panel) set this so long items scroll instead of running under it.</summary>
    protected float MenuItemMaxWidth = 0;
    protected Vector2 SelectedItemOffset = Vector2.Zero;
    protected Vector2 SelectedNoise = new Vector2(8, 8) * CurrentRuntime.ScaleF;
    protected List<MenuItem> MenuItems = new();
    
    public MenuScreen()
    {
        TimeAppearTitle = (float)GetTime();
    }

    protected override void Created()
    {
        AnimationStartedIndex = SelectedIndex;
        CreateMenu();
    }

    static TargetHandle DrawMenuItem(string text)
    {
        return Helper.DrawTextScaled(Helper.Translate(text), 16, 8, 4, 2, Runtime.CurrentRuntime.Fonts["newsreader"], "outline");
    }

    public virtual void CreateMenu()
    {

    }

    public static double PreviousKeyTimestamp = 0;
    protected int PreviousSelectedIndex = 0;
    Action<int>? Event;
    bool ItemActivated = false;

    public override void TopUpdate()
    {
        if (ItemActivated)
        {
            if (GetTime() - PreviousKeyTimestamp < MenuActivateCooldown)
                return;
            ItemActivated = false;
            if (Event != null)
                Event.Invoke(0);
        }

        // A tap on an item selects and activates it — the only way to drive menus on a touch device.
        if (HandleTouch())
            return;

        if (GetTime() - PreviousKeyTimestamp < MenuSwitchCooldown)
            return;
        if ((IsKeyDown(KeyCode.Up) || Controller.IsButtonDown(PadButton.LeftFaceUp))&& VerticalDirectionNavigation ||
            (IsKeyDown(KeyCode.Left) || Controller.IsButtonDown(PadButton.LeftFaceLeft)) && HorizontalDirectionNavigation)
        {
            Helper.PlaySound(CurrentRuntime.Sounds["item-switch"]);
            PreviousKeyTimestamp = GetTime();
            PreviousSelectedIndex = SelectedIndex;
            double j = ComputeAnimationIndex();
            AnimationStartedIndex = j;
            AnimationStartedAt = GetTime();
            if (MenuItems.Count == 0)
                return;
            int z = 0;
            do
            {
                if(LoopList)
                    SelectedIndex = (SelectedIndex - 1 + MenuItems.Count()) % MenuItems.Count();
                else
                    SelectedIndex = Math.Clamp(SelectedIndex - 1, 0, MenuItems.Count() - 1);
            } while (z < MenuItems.Count && !MenuItems[SelectedIndex].Enabled);
        }
        else if (
            ((IsKeyDown(KeyCode.Down)) || Controller.IsButtonDown(PadButton.LeftFaceDown)) && VerticalDirectionNavigation ||
            (IsKeyDown(KeyCode.Right) || Controller.IsButtonDown(PadButton.LeftFaceRight)) && HorizontalDirectionNavigation)
        {
            PreviousSelectedIndex = SelectedIndex;
            PreviousKeyTimestamp = GetTime();
            double j = ComputeAnimationIndex();
            Helper.PlaySound(CurrentRuntime.Sounds["item-switch"]);
            AnimationStartedIndex = j;
            AnimationStartedAt = GetTime();
            if (MenuItems.Count == 0)
                return;
            int z = 0;
            do
            {
                if (LoopList)
                    SelectedIndex = (SelectedIndex + 1) % MenuItems.Count;
                else
                    SelectedIndex = Math.Clamp(SelectedIndex + 1, 0, MenuItems.Count() - 1);
                z++;
            } while (z < MenuItems.Count && !MenuItems[SelectedIndex].Enabled);
        }
        else if (IsKeyDown(KeyCode.Enter) || IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(PadButton.RightFaceDown))
        {
            PreviousKeyTimestamp = GetTime();
            Helper.PlaySound(CurrentRuntime.Sounds["button"]);
            if(SelectedIndex > MenuItems.Count() - 1)
                return;
            Event = MenuItems[SelectedIndex].Action;
            ItemActivated = true;
        }
        else if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) ||
                 Controller.IsButtonDown(PadButton.RightFaceRight))
        {
            // Escape moves the cursor onto the screen's own leave-entry, and only leaves once it is already
            // there — so the first press shows you where you are going and the second one commits. Screens
            // with no such entry (or the pause menu, which opts out) still leave immediately.
            int exitIndex = EscapeFocusesExitItem ? FindExitItemIndex() : -1;
            if (exitIndex < 0)
            {
                if (AllowExitWithEscape)
                    Exit();
                return;
            }

            PreviousKeyTimestamp = GetTime();
            if (SelectedIndex == exitIndex)
            {
                Helper.PlaySound(CurrentRuntime.Sounds["button"]);
                Event = MenuItems[exitIndex].Action;
                ItemActivated = true;
                return;
            }

            Helper.PlaySound(CurrentRuntime.Sounds["item-switch"]);
            PreviousSelectedIndex = SelectedIndex;
            AnimationStartedIndex = ComputeAnimationIndex();
            AnimationStartedAt = GetTime();
            SelectedIndex = exitIndex;
        }
    }

    /// <summary>
    /// The menu entries that mean "leave this screen". They are matched by their translation key, which is
    /// what every menu already passes as the item's text.
    /// </summary>
    static readonly string[] ExitItemTexts = ["ingame.exit", "menu.exit", "controller.back"];

    /// <summary>
    /// The pause menu opts out: there Escape has always meant "unpause, now", and that reflex should survive.
    /// </summary>
    protected bool EscapeFocusesExitItem = true;

    int FindExitItemIndex() =>
        MenuItems.FindIndex(item => item.Enabled && ExitItemTexts.Contains(item.Text));

    protected void Exit()
    {
        Exiting();
        ItemActivated = true;
        PreviousKeyTimestamp = GetTime();
        Event = a => CurrentRuntime.RemoveScreen(this);
    }
    
    protected double ComputeAnimationIndex()
        => SelectedIndex > AnimationStartedIndex 
                ? Math.Min(AnimationStartedIndex + (GetTime() - AnimationStartedAt) / MenuSwitchCooldown, (float)SelectedIndex)
         : Math.Max(AnimationStartedIndex - (GetTime() - AnimationStartedAt) / MenuSwitchCooldown, (float)SelectedIndex);
    
    protected double ComputeAnimationIndexLoop()
    {
        bool isPositive = PreviousSelectedIndex < SelectedIndex;
        bool isReverted = Math.Abs(PreviousSelectedIndex - SelectedIndex) > 1;
        
        isPositive = isReverted ? !isPositive : isPositive;
        if (isPositive)
        {
            return Math.Min(AnimationStartedIndex + (GetTime() - AnimationStartedAt) / MenuSwitchCooldown,
                (float)SelectedIndex + (isReverted ? MenuItems.Count : 0)) % MenuItems.Count;
        }
        else
        {
            return Math.Max(AnimationStartedIndex - (GetTime() - AnimationStartedAt) / MenuSwitchCooldown + (isReverted ? MenuItems.Count : 0),
                (float)SelectedIndex) % MenuItems.Count;
        }
    }
    
    // Where each menu item landed last frame, in backbuffer (game) coordinates, for touch hit-testing.
    private readonly List<(Rect Bounds, int Index, bool Enabled)> ItemHitboxes = new();
    private int PreviousTouchCount;

    /// <summary>Whether touch drives the menus: always on Android, on desktop only with touch controls on.</summary>
    protected static bool TouchActive =>
#if ANDROID
        true;
#else
        Configuration.Config.TouchControls;
#endif

    /// <summary>Bounds of the item drawn last frame (backbuffer coords), or default if it was not drawn.</summary>
    protected Rect ItemBounds(int index)
    {
        foreach ((Rect bounds, int idx, bool _) in ItemHitboxes)
            if (idx == index)
                return bounds;
        return default;
    }

    /// <summary>The primary touch point mapped into backbuffer coords; false when no finger is down.</summary>
    protected bool TryGetTouchPoint(out Vector2 point)
    {
        point = default;
        if (Engine.Input.TouchCount == 0)
            return false;
        Rect present = CurrentRuntime.PresentRect;
        if (present.Width <= 0 || present.Height <= 0)
            return false;
        Vector2 window = Engine.Input.GetTouchPosition(0);
        point = new Vector2((window.X - present.X) / present.Width * CurrentRuntime.Width,
            (window.Y - present.Y) / present.Height * CurrentRuntime.Height);
        return true;
    }

    /// <summary>
    /// A tap selects the item under the finger and activates it. Touch positions arrive in real window
    /// pixels; the game draws into a letterboxed backbuffer, so they are mapped back through PresentRect into
    /// the same coordinate space the hitboxes were recorded in. Returns true if a tap was consumed.
    /// </summary>
    private bool HandleTouch()
    {
#if !ANDROID
        // On desktop the "touch" points are synthesised from the mouse. Menus are keyboard/pad-driven there,
        // so tap-to-select is only enabled when the user has actually turned touch controls on — a bare mouse
        // must not drive the menus.
        if (!Configuration.Config.TouchControls)
            return false;
#endif
        int count = Engine.Input.TouchCount;
        bool down = count > 0 && PreviousTouchCount == 0;
        bool up = count == 0 && PreviousTouchCount > 0;
        PreviousTouchCount = count;

        // Windowed lists (Settings, Music Room, list-select) get drag-to-scroll: a vertical drag moves the list
        // and a touch that never turns into a drag still selects on release. Everything else keeps the original
        // tap-on-touch-down behaviour untouched.
        if (EnableScrolling && LastWindowed)
            return HandleScrollableTouch(count > 0, down, up);

        if (!down)
            return false;

        Rect present = CurrentRuntime.PresentRect;
        if (present.Width <= 0 || present.Height <= 0)
            return false;

        Vector2 window = Engine.Input.GetTouchPosition(0);
        float bx = (window.X - present.X) / present.Width * CurrentRuntime.Width;
        float by = (window.Y - present.Y) / present.Height * CurrentRuntime.Height;
        return TrySelectAt(bx, by);
    }

    /// <summary>
    /// Resolves a touch point (game coords) to an action: a direct hit on a listed item selects+activates it;
    /// on a horizontal carousel the point is read as a left/right/confirm zone. Returns true if it consumed the
    /// touch. Shared by tap-on-down (non-scrolling menus) and tap-on-release (windowed, drag-scrolling menus).
    /// </summary>
    private bool TrySelectAt(float bx, float by)
    {
        foreach ((Rect bounds, int idx, bool enabled) in ItemHitboxes)
        {
            if (!enabled)
                continue;
            if (bx < bounds.X || bx > bounds.X + bounds.Width || by < bounds.Y || by > bounds.Y + bounds.Height)
                continue;
            ActivateItem(idx);
            return true;
        }

        if (HorizontalDirectionNavigation && MenuItems.Count > 0)
        {
            float third = CurrentRuntime.Width / 3f;
            if (bx < third)
                StepSelection(-1);
            else if (bx > third * 2)
                StepSelection(1);
            else
                ActivateItem(SelectedIndex);
            return true;
        }
        return false;
    }

    /// <summary>Distance (in fractions of a row) the finger must travel before a touch is treated as a scroll
    /// drag rather than a tap. Small enough to feel responsive, large enough that a tap's jitter is not a drag.</summary>
    private const float DragThreshold = 0.35f;

    /// <summary>
    /// Drag-to-scroll for a windowed list. A vertical drag slides <see cref="ScrollFirstFloat"/> under the
    /// finger (direct manipulation: dragging down reveals earlier rows), keeping the selection inside the
    /// visible window so the selection-follow logic in <see cref="DrawMenu"/> does not snap the view back. A
    /// touch that stays under the threshold is a tap and selects the row under the finger on release.
    /// </summary>
    private bool HandleScrollableTouch(bool held, bool down, bool up)
    {
        int count = MenuItems.Count;
        int maxFirst = Math.Max(0, count - LastMaxVisible);

        // No finger down and not the release frame: make sure a drag can never linger. Without this a manual
        // scroll interrupted without a clean release (touch lost, screen change) could leave IsManualScrolling
        // stuck true, which suppresses the easing in DrawMenu — the list then stops following keyboard/pad
        // selection and the scroll animation appears frozen.
        if (!held && !up)
        {
            DragActive = false;
            IsManualScrolling = false;
            return false;
        }

        if (down)
        {
            if (TryGetTouchPoint(out Vector2 p))
            {
                DragStartPoint = p;
                LastTouchPoint = p;
                DragStartScroll = ScrollFirstFloat;
            }
            DragActive = false;
            IsManualScrolling = false;
            return true;   // consume; wait to see whether this becomes a tap or a drag
        }

        if (held)
        {
            if (!TryGetTouchPoint(out Vector2 p) || LastItemHeight <= 0)
                return true;
            LastTouchPoint = p;
            float dy = p.Y - DragStartPoint.Y;
            if (!DragActive && MathF.Abs(dy) > LastItemHeight * DragThreshold)
                DragActive = true;
            if (DragActive)
            {
                float target = Math.Clamp(DragStartScroll - dy / LastItemHeight, 0, maxFirst);
                ScrollFirstFloat = target;
                ScrollFirstIndex = Math.Clamp((int)MathF.Round(target), 0, maxFirst);
                KeepSelectionInWindow();
                IsManualScrolling = true;
            }
            return true;
        }

        if (up)
        {
            bool wasDrag = DragActive;
            DragActive = false;
            IsManualScrolling = false;
            if (wasDrag)
            {
                // Settle onto a whole row; the easing in DrawMenu slides ScrollFirstFloat onto it.
                ScrollFirstIndex = Math.Clamp((int)MathF.Round(ScrollFirstFloat), 0, maxFirst);
                return true;
            }
            // A tap: select the row under where the finger last was (the finger is already up, so TouchCount is 0).
            return TrySelectAt(LastTouchPoint.X, LastTouchPoint.Y);
        }

        return false;
    }

    /// <summary>Clamps the selection into the currently visible window (landing on an enabled row), so a manual
    /// scroll never leaves the selected item off-screen — which would make DrawMenu's follow logic snap back.</summary>
    private void KeepSelectionInWindow()
    {
        if (LastMaxVisible <= 0 || MenuItems.Count == 0)
            return;
        int first = Math.Clamp(ScrollFirstIndex, 0, MenuItems.Count - 1);
        int last = Math.Min(MenuItems.Count - 1, first + LastMaxVisible - 1);
        int target = Math.Clamp(SelectedIndex, first, last);
        if (!MenuItems[target].Enabled)
        {
            int found = -1;
            for (int i = target; i <= last && found < 0; i++)
                if (MenuItems[i].Enabled) found = i;
            for (int i = target; i >= first && found < 0; i--)
                if (MenuItems[i].Enabled) found = i;
            if (found >= 0)
                target = found;
        }
        if (target == SelectedIndex)
            return;
        PreviousSelectedIndex = SelectedIndex;
        SelectedIndex = target;
        // Snap the selection animation to the new row so the highlight does not slide across the list as it scrolls.
        AnimationStartedIndex = target;
        AnimationStartedAt = GetTime();
    }

    private void ActivateItem(int idx)
    {
        if (idx < 0 || idx >= MenuItems.Count)
            return;
        SelectedIndex = idx;
        Helper.PlaySound(CurrentRuntime.Sounds["button"]);
        Event = MenuItems[idx].Action;
        ItemActivated = true;
        PreviousKeyTimestamp = GetTime();
    }

    /// <summary>Moves the selection by one, skipping disabled items — the touch equivalent of a left/right press.</summary>
    private void StepSelection(int direction)
    {
        if (MenuItems.Count == 0)
            return;
        Helper.PlaySound(CurrentRuntime.Sounds["item-switch"]);
        PreviousSelectedIndex = SelectedIndex;
        AnimationStartedIndex = ComputeAnimationIndex();
        AnimationStartedAt = GetTime();
        PreviousKeyTimestamp = GetTime();
        int z = 0;
        do
        {
            if (LoopList)
                SelectedIndex = (SelectedIndex + direction + MenuItems.Count) % MenuItems.Count;
            else
                SelectedIndex = Math.Clamp(SelectedIndex + direction, 0, MenuItems.Count - 1);
            z++;
        } while (z < MenuItems.Count && !MenuItems[SelectedIndex].Enabled);
    }

    protected void DrawMenu()
    {
        ItemHitboxes.Clear();
        double cIndex = ComputeAnimationIndexLoop();
        float t = (float)GetTime();
        float swapNoise = 1-(float)Helper.ComputeObjectTimeStart(t, AnimationStartedAt, 0.25);
        int count = MenuItems.Count;

        // Decide whether to window+scroll: opt-in, uniform row height, and only when the list is taller than
        // the room below CurrentY. Rows in these menus are uniform, so one item's texture height sizes them all.
        float itemHeight = count > 0 ? MenuItems[0].Texture.Height : 0;
        bool windowed = EnableScrolling && itemHeight >= 1;
        int maxVisible = count;
        if (windowed)
        {
            if (MaxVisibleItems > 0)
            {
                // Hard per-screen cap (e.g. the music room's 7): a fixed-size window independent of the
                // available height, so it stays put while the appear animation slides CurrentY around.
                maxVisible = MaxVisibleItems;
            }
            else
            {
                int bottomMargin = (int)(6 * CurrentRuntime.ScaleF);
                int available = CurrentRuntime.Height - CurrentY - bottomMargin;
                maxVisible = Math.Max(1, (int)(available / itemHeight));
            }
            windowed = count > maxVisible;
        }

        // Hand the touch drag-scroll logic (which runs in TopUpdate, before this) the row height and window
        // size it needs. One frame stale, exactly like the item hitboxes below.
        LastItemHeight = itemHeight;
        LastMaxVisible = maxVisible;
        LastWindowed = windowed;

        if (!windowed)
        {
            // Original behaviour, unchanged: draw every item top-to-bottom, each advancing by its own height.
            ScrollFirstIndex = 0;
            ScrollFirstFloat = 0;
            int y0 = CurrentY;
            for (int index = 0; index < count; index++)
            {
                var x = MenuItems[index];
                float offsetState = (float)Math.Abs(1-Math.Clamp(Math.Abs(cIndex - index), 0, 1));
                Vector2 offset = offsetState * SelectedItemOffset;
                if (index == SelectedIndex)
                    offset += swapNoise*SelectedNoise*new Vector2(MathF.Sin(t*100+24), MathF.Cos(t*100));
                float scale = SelectedItemScale * offsetState + 1f * (1 - offsetState);
                ItemHitboxes.Add((new Rect(CurrentX, y0, MathF.Min(x.Texture.Width * scale, ItemMaxWidth()), x.Texture.Height * scale),
                    index, x.Enabled));
                Rgba col = (index == SelectedIndex ? Helper.Mix(Rgba.Yellow, Rgba.White, MathF.Abs((t *
                        (ItemActivated ? 30 : 2)
                        ) % 2 - 1)) : Rgba.White) with { A = (byte)(x.Enabled ? 255 : 128) };
                DrawMenuItemTexture(x, index, CurrentX + offset.X, y0 + offset.Y, scale, col);
                y0 += (int)(x.Texture.Height * scale);
            }
            return;
        }

        // Windowed smooth scroll: keep the selected row inside the window, then ease a fractional scroll
        // position toward that integer target so the list slides rather than jumping. Rows fade out toward the
        // top and bottom of the window (a transparency gradient) so content dissolves in/out instead of clipping.
        // Keep a one-row lookahead: start scrolling the instant the cursor reaches the first/last VISIBLE row
        // (not only once it leaves the window), so there's always an item shown above/below the selection —
        // except at the true ends of the list, where the clamp lets the cursor sit on the real first/last row.
        int edgeMargin = maxVisible >= 3 ? 1 : 0;
        if (SelectedIndex - edgeMargin < ScrollFirstIndex)
            ScrollFirstIndex = SelectedIndex - edgeMargin;
        else if (SelectedIndex + edgeMargin >= ScrollFirstIndex + maxVisible)
            ScrollFirstIndex = SelectedIndex + edgeMargin - maxVisible + 1;
        ScrollFirstIndex = Math.Clamp(ScrollFirstIndex, 0, count - maxVisible);
        // While a finger is dragging, ScrollFirstFloat tracks the finger exactly (set in HandleScrollableTouch);
        // easing it toward the rounded ScrollFirstIndex here would drag it back off the finger. Ease only when
        // the list is moving on its own (selection change or a settle after release).
        if (!IsManualScrolling)
            ScrollFirstFloat = MathF.Abs(ScrollFirstFloat - ScrollFirstIndex) < 0.003f
                ? ScrollFirstIndex
                : Helper.Mix(ScrollFirstFloat, ScrollFirstIndex, 0.2f);

        float viewTop = CurrentY;
        float viewBottom = CurrentY + maxVisible * itemHeight;
        float fadeZone = itemHeight * 1.25f;

        for (int index = 0; index < count; index++)
        {
            var x = MenuItems[index];
            float y = CurrentY + (index - ScrollFirstFloat) * itemHeight;
            if (y + itemHeight < viewTop - fadeZone || y > viewBottom + fadeZone)
                continue;   // fully outside the window (plus fade margin) — no draw, no hitbox

            // Transparency gradient: full opacity in the middle of the window, fading to 0 across the last
            // fadeZone pixels at either edge — but ONLY where there is more list beyond that edge. At the very
            // top of the list the first item must stay crisp (nothing above it to dissolve into), and likewise
            // the last item at the very bottom; fading them there reads as a broken scroll.
            float centerY = y + itemHeight / 2f;
            float topFade = Math.Clamp((centerY - viewTop) / fadeZone, 0f, 1f);
            float bottomFade = Math.Clamp((viewBottom - centerY) / fadeZone, 0f, 1f);
            if (ScrollFirstFloat <= 0.01f)
                topFade = 1f;                                   // pinned to the first item — no top fade
            if (ScrollFirstFloat + maxVisible >= count - 0.01f)
                bottomFade = 1f;                                // pinned to the last item — no bottom fade
            float edgeFade = topFade * bottomFade;
            if (edgeFade <= 0.02f)
                continue;

            float offsetState = (float)Math.Abs(1-Math.Clamp(Math.Abs(cIndex - index), 0, 1));
            Vector2 offset = offsetState * SelectedItemOffset;
            if (index == SelectedIndex)
                offset += swapNoise*SelectedNoise*new Vector2(MathF.Sin(t*100+24), MathF.Cos(t*100));
            float scale = SelectedItemScale * offsetState + 1f * (1 - offsetState);
            ItemHitboxes.Add((new Rect(CurrentX, (int)y, MathF.Min(x.Texture.Width * scale, ItemMaxWidth()), x.Texture.Height * scale),
                index, x.Enabled));
            Rgba color = index == SelectedIndex
                ? Helper.Mix(Rgba.Yellow, Rgba.White, MathF.Abs((t * (ItemActivated ? 30 : 2)) % 2 - 1))
                : Rgba.White;
            byte alpha = (byte)((x.Enabled ? 255 : 128) * edgeFade);
            DrawMenuItemTexture(x, index, CurrentX + offset.X, y + offset.Y, scale, color with { A = alpha });
        }
    }

    /// <summary>The widest a menu item may draw before it is horizontally scrolled, in screen px.</summary>
    private float ItemMaxWidth() =>
        MenuItemMaxWidth > 0
            ? MenuItemMaxWidth * CurrentRuntime.ScaleF
            : MathF.Max(1, CurrentRuntime.Width - CurrentX - 8 * CurrentRuntime.ScaleF);

    /// <summary>
    /// Draws one item's pre-rendered text texture, horizontally scrolling it when it is too wide to fit
    /// <see cref="ItemMaxWidth"/>. The selected over-wide item marquees — hold, slide to reveal the end, hold,
    /// slide back (restarting each time the selection changes); other over-wide items just show their start.
    /// Items that fit are drawn as before.
    /// </summary>
    private void DrawMenuItemTexture(MenuItem item, int index, float drawX, float drawY, float scale, Rgba color)
    {
        var tex = item.Texture;
        float maxW = ItemMaxWidth();
        if (tex.Width * scale <= maxW)
        {
            DrawTextureEx(tex, new Vector2(drawX, drawY), 0, scale, color);
            return;
        }

        float srcW = maxW / scale;                       // texels that fit in the allowed width at this scale
        float overflow = tex.Width - srcW;               // > 0
        float srcX = 0f;
        if (index == SelectedIndex)
        {
            const float hold = 0.9f, speed = 55f;        // seconds paused at each end; texels/second while sliding
            float slide = overflow / speed;
            float total = 2f * (hold + slide);
            float p = (float)((GetTime() - AnimationStartedAt) % total);   // restarts on each selection change
            if (p < hold) srcX = 0f;
            else if (p < hold + slide) srcX = (p - hold) / slide * overflow;
            else if (p < 2f * hold + slide) srcX = overflow;
            else srcX = overflow * (1f - (p - 2f * hold - slide) / slide);
        }
        DrawTexturePro(tex,
            new Rect(srcX, 0, srcW, tex.Height),
            new Rect(drawX, drawY, srcW * scale, tex.Height * scale),
            Vector2.Zero, 0, color);
    }


    public class MenuItem : IDisposable
    {
        public MenuItem(string text, string replace, Action<int>? action)
        {
            Action = action;
            Text = text;
            Replace = replace;
        }

        public static bool RequiresRender = false;
        public static List<MenuItem> RenderItemQueue = new();
        
        private string text = "";
        private string replace = "";
        public Action<int>? Action;
        public TextureHandle Texture =>  texture.Texture;
        private TargetHandle texture = new TargetHandle();
        public bool Enabled = true;

        // Rendering style, overridable per item (the replay list uses a compact monospace font). Changing any of
        // these re-renders the item's texture, so object-initializer overrides take effect on the first draw.
        private string fontKey = "newsreader";
        private float fontSize = 16f;
        private float padding = 4f;
        public string FontKey { get => fontKey; set { if (fontKey == value) return; fontKey = value; AddToRender(this); } }
        public float FontSize { get => fontSize; set { if (fontSize == value) return; fontSize = value; AddToRender(this); } }
        public float Padding { get => padding; set { if (padding == value) return; padding = value; AddToRender(this); } }
        
        public static void AddToRender(MenuItem item)
        {
            if (RenderItemQueue.Contains(item))
                return;
            RequiresRender = true;
            RenderItemQueue.Add(item);
        }

        public static void RenderItems()
        {
            RequiresRender = false;
            foreach (var item in RenderItemQueue)
                item.Render();
            RenderItemQueue.Clear();
        }

        void Render()
        {
            if(texture.Id != 0)
                UnloadRenderTexture(texture);
            Helper.DrawTextGradient(
                out texture,
                CurrentRuntime.Fonts[fontKey],
                fontSize * CurrentRuntime.ScaleF,
                Helper.Translate(text).Replace("%s", Helper.Translate(replace)),
                Rgba.White,
                padding * CurrentRuntime.ScaleF
                );
            //texture=Helper.DrawTextScaled(, 16, 8, 4, 2, Runtime.CurrentRuntime.Fonts["newsreader"], "gradient");
        }
        
        public string Text
        {
            get => text;
            set
            {
                text = value;
                AddToRender(this);
            }
        }

        public string Replace
        {
            get => replace;
            set
            {
                if (replace == value)
                    return;
                replace = value;
                AddToRender(this);
            }
        }

        public void Dispose()
        {
            if(texture.Id != 0)
                UnloadRenderTexture(texture);
        }
    }
}
