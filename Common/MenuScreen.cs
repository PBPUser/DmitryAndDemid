using System.Globalization;
using System.IO.Pipes;
using System.Numerics;
using Atk;
using DmitryAndDemid;
using static Raylib_cs.Raylib;
using static DmitryAndDemid.Runtime;
using DmitryAndDemid.Utils;
using Raylib_cs;
using Rectangle = Raylib_cs.Rectangle;

namespace DmitryAndDemid.Common;

public abstract class MenuScreen : ScreenWithTitle
{
    public const double MenuSwitchCooldown = 0.125;
    public const double MenuActivateCooldown = 0.5;
    
    protected int CurrentY = 0;
    protected List<MenuItem> MenuItems = new();
    protected int CurrentX = 0;
    protected int SelectedIndex = 0;
    protected bool AllowExitWithEscape = true;
    protected bool LoopList = true;
    protected Vector2 SelectedItemOffset = Vector2.Zero;
    protected Vector2 SelectedNoise = new Vector2(8, 8) * CurrentRuntime.ScaleF;
    protected float SelectedItemScale = 1f;

    protected double AnimationStartedAt = 0;
    protected double AnimationStartedIndex = 0;
    
    public MenuScreen()
    {
        TimeAppearTitle = (float)GetTime();
    }

    protected override void Created()
    {
        AnimationStartedIndex = SelectedIndex;
        CreateMenu();
    }

    static RenderTexture2D DrawMenuItem(string text)
    {
        return Helper.DrawTextScaled(Helper.Translate(text), 16, 8, 4, 2, Runtime.CurrentRuntime.Fonts["newsreader"], "gradient");
    }

    public virtual void CreateMenu()
    {

    }

    protected bool HorizontalDirectionNavigation = false;
    protected bool VerticalDirectionNavigation = true;
    
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
        if (GetTime() - PreviousKeyTimestamp < MenuSwitchCooldown)
            return;
        if ((IsKeyDown(KeyboardKey.Up) || Controller.IsButtonDown(GamepadButton.LeftFaceUp))&& VerticalDirectionNavigation ||
            (IsKeyDown(KeyboardKey.Left) || Controller.IsButtonDown(GamepadButton.LeftFaceLeft)) && HorizontalDirectionNavigation)
        {
            Helper.PlaySound(CurrentRuntime.Sounds["item-switch"]);
            PreviousKeyTimestamp = GetTime();
            PreviousSelectedIndex = SelectedIndex;
            double j = ComputeAnimationIndex();
            AnimationStartedIndex = j;
            AnimationStartedAt = GetTime();
            if (MenuItems.Count == 0)
                return;
            if(LoopList)
                SelectedIndex = (SelectedIndex - 1 + MenuItems.Count()) % MenuItems.Count();
            else
                SelectedIndex = Math.Clamp(SelectedIndex - 1, 0, MenuItems.Count() - 1);
        }
        else if (
            ((IsKeyDown(KeyboardKey.Down)) || Controller.IsButtonDown(GamepadButton.LeftFaceDown)) && VerticalDirectionNavigation ||
            (IsKeyDown(KeyboardKey.Right) || Controller.IsButtonDown(GamepadButton.LeftFaceRight)) && HorizontalDirectionNavigation)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            PreviousKeyTimestamp = GetTime();
            PreviousSelectedIndex = SelectedIndex;
            double j = ComputeAnimationIndex();
            AnimationStartedIndex = j;
            AnimationStartedAt = GetTime();
            if (MenuItems.Count == 0)
                return;
            if(LoopList)
                SelectedIndex = (SelectedIndex + 1) % MenuItems.Count();
            else
                SelectedIndex = Math.Clamp(SelectedIndex + 1, 0, MenuItems.Count() - 1);
        }
        else if (IsKeyDown(KeyboardKey.Enter) || IsKeyDown(KeyboardKey.Z) || Controller.IsButtonDown(GamepadButton.RightFaceDown))
        {
            PreviousKeyTimestamp = GetTime();
            Helper.PlaySound(CurrentRuntime.Sounds["button"]);
            if(SelectedIndex > MenuItems.Count() - 1)
                return;
            Event = MenuItems[SelectedIndex].Action;
            ItemActivated = true;
        }
        else if (AllowExitWithEscape && (IsKeyDown(KeyboardKey.Escape) || IsKeyDown(KeyboardKey.X) || Controller.IsButtonDown(GamepadButton.RightFaceRight)))
        {
            Exiting();
            PreviousKeyTimestamp = GetTime();
            Event = a => CurrentRuntime.RemoveScreen(this);
            ItemActivated = true;
        }
    }

    protected double ComputeAnimationIndex()
    {
        if(SelectedIndex > AnimationStartedIndex)
            return Math.Min(AnimationStartedIndex + (GetTime() - AnimationStartedAt) / MenuSwitchCooldown, (float)SelectedIndex);
        else
            return Math.Max(AnimationStartedIndex - (GetTime() - AnimationStartedAt) / MenuSwitchCooldown, (float)SelectedIndex);
    }
    
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
    
    protected void DrawMenu()
    {
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
            DrawTextureEx(x.Texture, new Vector2(CurrentX + offset.X, y + offset.Y), 0, scale, 
                index == SelectedIndex ? Helper.Mix(Color.Yellow, Color.White, MathF.Abs((t * 
                        (ItemActivated ? 30 : 2)
                        ) % 2 - 1)) : Color.White);
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
        public Texture2D Texture =>  texture.Texture;
        private RenderTexture2D texture = new RenderTexture2D();

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
            texture=Helper.DrawTextScaled(Helper.Translate(text).Replace("%s", Helper.Translate(replace)), 16, 8, 4, 2, Runtime.CurrentRuntime.Fonts["newsreader"], "gradient");
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
