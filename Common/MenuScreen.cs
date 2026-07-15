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
    protected float SelectedItemScale = 1f;
    protected double AnimationStartedAt = 0;
    protected double AnimationStartedIndex = 0;
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
        bool tapped = count > 0 && PreviousTouchCount == 0;
        PreviousTouchCount = count;
        if (!tapped)
            return false;

        Rect present = CurrentRuntime.PresentRect;
        if (present.Width <= 0 || present.Height <= 0)
            return false;

        Vector2 window = Engine.Input.GetTouchPosition(0);
        float bx = (window.X - present.X) / present.Width * CurrentRuntime.Width;
        float by = (window.Y - present.Y) / present.Height * CurrentRuntime.Height;

        // 1. A direct hit on a listed item — the vertical menus, which lay their items out through DrawMenu.
        foreach ((Rect bounds, int idx, bool enabled) in ItemHitboxes)
        {
            if (!enabled)
                continue;
            if (bx < bounds.X || bx > bounds.X + bounds.Width || by < bounds.Y || by > bounds.Y + bounds.Height)
                continue;
            ActivateItem(idx);
            return true;
        }

        // 2. The horizontal carousels (difficulty, character select) draw their own layout with no per-item
        //    box, so a tap is read as a zone: left third steps back, right third steps forward, the middle
        //    confirms the current choice.
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
                ItemHitboxes.Add((new Rect(CurrentX, y0, x.Texture.Width * scale, x.Texture.Height * scale),
                    index, x.Enabled));
                DrawTextureEx(x.Texture, new Vector2(CurrentX + offset.X, y0 + offset.Y), 0, scale,
                    (index == SelectedIndex ? Helper.Mix(Rgba.Yellow, Rgba.White, MathF.Abs((t *
                            (ItemActivated ? 30 : 2)
                            ) % 2 - 1)) : Rgba.White) with { A = (byte)(x.Enabled ? 255 : 128) });
                y0 += (int)(x.Texture.Height * scale);
            }
            return;
        }

        // Windowed smooth scroll: keep the selected row inside the window, then ease a fractional scroll
        // position toward that integer target so the list slides rather than jumping. Rows fade out toward the
        // top and bottom of the window (a transparency gradient) so content dissolves in/out instead of clipping.
        if (SelectedIndex < ScrollFirstIndex)
            ScrollFirstIndex = SelectedIndex;
        else if (SelectedIndex >= ScrollFirstIndex + maxVisible)
            ScrollFirstIndex = SelectedIndex - maxVisible + 1;
        ScrollFirstIndex = Math.Clamp(ScrollFirstIndex, 0, count - maxVisible);
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
            // fadeZone pixels at either edge.
            float centerY = y + itemHeight / 2f;
            float edgeFade = Math.Clamp((centerY - viewTop) / fadeZone, 0f, 1f)
                           * Math.Clamp((viewBottom - centerY) / fadeZone, 0f, 1f);
            if (edgeFade <= 0.02f)
                continue;

            float offsetState = (float)Math.Abs(1-Math.Clamp(Math.Abs(cIndex - index), 0, 1));
            Vector2 offset = offsetState * SelectedItemOffset;
            if (index == SelectedIndex)
                offset += swapNoise*SelectedNoise*new Vector2(MathF.Sin(t*100+24), MathF.Cos(t*100));
            float scale = SelectedItemScale * offsetState + 1f * (1 - offsetState);
            ItemHitboxes.Add((new Rect(CurrentX, (int)y, x.Texture.Width * scale, x.Texture.Height * scale),
                index, x.Enabled));
            Rgba color = index == SelectedIndex
                ? Helper.Mix(Rgba.Yellow, Rgba.White, MathF.Abs((t * (ItemActivated ? 30 : 2)) % 2 - 1))
                : Rgba.White;
            byte alpha = (byte)((x.Enabled ? 255 : 128) * edgeFade);
            DrawTextureEx(x.Texture, new Vector2(CurrentX + offset.X, y + offset.Y), 0, scale, color with { A = alpha });
        }
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
