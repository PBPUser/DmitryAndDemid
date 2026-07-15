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
        Vector2 offset;
        int y = CurrentY;
        int index = 0;
        double cIndex = ComputeAnimationIndexLoop();
        float offsetState = 0;
        float scale = 0;
        float t = (float)GetTime();
        float swapNoise = 1-(float)Helper.ComputeObjectTimeStart(t, AnimationStartedAt, 0.25);
        foreach (var x in MenuItems)
        {
            offsetState = (float)Math.Abs(1-Math.Clamp(Math.Abs(cIndex - index), 0, 1));
            offset = offsetState * SelectedItemOffset;
            if (index == SelectedIndex)
            {
                offset += swapNoise*SelectedNoise*new Vector2(MathF.Sin(t*100+24), MathF.Cos(t*100));
            }
            scale = SelectedItemScale * offsetState + 1f * (1 - offsetState);
            ItemHitboxes.Add((new Rect(CurrentX, y, x.Texture.Width * scale, x.Texture.Height * scale),
                index, x.Enabled));
            DrawTextureEx(x.Texture, new Vector2(CurrentX + offset.X, y + offset.Y), 0, scale,
                (index == SelectedIndex ? Helper.Mix(Rgba.Yellow, Rgba.White, MathF.Abs((t * 
                        (ItemActivated ? 30 : 2)
                        ) % 2 - 1)) : Rgba.White) with { A = (byte)(x.Enabled ? 255 : 128) });
            y += (int)(x.Texture.Height * scale);
            index++;
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
                CurrentRuntime.Fonts["newsreader"],
                16 * CurrentRuntime.ScaleF,
                Helper.Translate(text).Replace("%s", Helper.Translate(replace)),
                Rgba.White,
                4 * CurrentRuntime.ScaleF
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
