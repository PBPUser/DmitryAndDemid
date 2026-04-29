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

public class MenuScreen : Screen
{
    public const double MenuSwitchCooldown = 0.125;
    public const double MenuActivateCooldown = 0.5;

    protected Dictionary<string, EventHandler<int>> Menu = new();
    protected RenderTexture2D[] MenuItems;
    protected int CurrentY = 0;
    protected int CurrentX = 0;
    protected int SelectedIndex = 0;
    protected bool AllowExitWithEscape = true;
    protected bool LoopList = true;

    protected double AnimationStartedAt = 0;
    protected double AnimationStartedIndex = 0;


    static MenuScreen()
    {
        MenuTextureTarget = Helper.Scale(
            new Rectangle(0, 0, 640, 135), 
            Runtime.CurrentRuntime.Scale);
    }
    
    public MenuScreen()
    {
        TimeAppearTitle = (float)GetTime();
    }

    protected override void Created()
    {
        AnimationStartedIndex = SelectedIndex;
        
        CreateMenu();
        MenuItems = new RenderTexture2D[Menu.Count()];

        for (int i = 0; i < Menu.Count(); i++)
        {
            MenuItems[i] = DrawMenuItem(Menu.Keys.ElementAt(i));
        }
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
    EventHandler<int>? Event;
    private Texture2D MenuTitleTexture;
    private static Rectangle MenuTextureSource = new Rectangle(0, 0, 1920, 270);
    private static Rectangle MenuTextureTarget;

    bool ItemActivated = false;

    public override void TopUpdate()
    {
        if (ItemActivated)
        {
            if (GetTime() - PreviousKeyTimestamp < MenuActivateCooldown)
                return;
            ItemActivated = false;
            if (Event != null)
                Event.Invoke(null, 0);
        }
        if (GetTime() - PreviousKeyTimestamp < MenuSwitchCooldown)
            return;
        if (Raylib.IsKeyDown(KeyboardKey.Up)&& VerticalDirectionNavigation ||
            IsKeyDown(KeyboardKey.Left) && HorizontalDirectionNavigation)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            PreviousKeyTimestamp = GetTime();
            PreviousSelectedIndex = SelectedIndex;
            double j = ComputeAnimationIndex();
            AnimationStartedIndex = j;
            AnimationStartedAt = GetTime();
            if (MenuItems.Length == 0)
                return;
            if(LoopList)
                SelectedIndex = (SelectedIndex - 1 + MenuItems.Count()) % MenuItems.Count();
            else
                SelectedIndex = Math.Clamp(SelectedIndex - 1, 0, MenuItems.Count() - 1);
        }
        else if (
            IsKeyDown(KeyboardKey.Down) && VerticalDirectionNavigation ||
            IsKeyDown(KeyboardKey.Right) && HorizontalDirectionNavigation)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            PreviousKeyTimestamp = GetTime();
            PreviousSelectedIndex = SelectedIndex;
            double j = ComputeAnimationIndex();
            AnimationStartedIndex = j;
            AnimationStartedAt = GetTime();
            if (MenuItems.Length == 0)
                return;
            if(LoopList)
                SelectedIndex = (SelectedIndex + 1) % MenuItems.Count();
            else
                SelectedIndex = Math.Clamp(SelectedIndex + 1, 0, MenuItems.Count() - 1);
        }
        else if (Raylib.IsKeyDown(KeyboardKey.Enter))
        {
            PreviousKeyTimestamp = GetTime();
            ItemActivated = true;
            Event = Menu.ElementAt(SelectedIndex).Value;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
        }
        else if (AllowExitWithEscape && Raylib.IsKeyDown(KeyboardKey.Escape))
        {
            Exiting();
            PreviousKeyTimestamp = GetTime();
            Event = (a, b) => Runtime.CurrentRuntime.RemoveScreen(this);
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
                (float)SelectedIndex + (isReverted ? Menu.Count : 0)) % Menu.Count;
        }
        else
        {
            return Math.Max(AnimationStartedIndex - (GetTime() - AnimationStartedAt) / MenuSwitchCooldown + (isReverted ? Menu.Count : 0),
                (float)SelectedIndex) % Menu.Count;
        }
    }
    
    public virtual void Exiting()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
        TimeDisappear = (float)Raylib.GetTime() + 0.5f;
        TimeDisappearTitle = (float)GetTime() + 0.5f;
    }

    private float TimeDisappearTitle = float.MaxValue;
    private float TimeAppearTitle = float.MinValue;
    
    public override void Deactivated()
    {
        TimeDisappearTitle = (float)GetTime() + 0.5f;
        base.Deactivated();
    }

    public override void Activated()
    {
        TimeAppearTitle = (float)GetTime();
        TimeDisappearTitle = float.MaxValue;
        base.Activated();
    }

    protected void SetTitle(Texture2D title)
    {
        MenuTitleTexture = title;
    }
    
    protected void DrawMenu()
    {
        int y = CurrentY;
        int index = 0;
        foreach (var x in MenuItems)
        {
            DrawTexture(x.Texture, CurrentX, y, index == SelectedIndex ? Color.Yellow : Color.White);
            y += x.Texture.Height;
            index++;
        }
    }

    protected void DrawTitle()
    {
        float appear = (float)Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppearTitle, .5f, TimeDisappearTitle, .5f);
        DrawTexturePro(MenuTitleTexture, MenuTextureSource, MenuTextureTarget with { Y = (1-Helper.Pow2F(appear)) * MenuTextureTarget.Height * -1 }, Vector2.Zero, 0, Color.White);
    }
}
