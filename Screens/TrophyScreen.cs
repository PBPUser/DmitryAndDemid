using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class TrophyScreen : ScreenWithTitle
{
    public TrophyScreen()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        SetTitle(Runtime.CurrentRuntime.Textures["nickname.png"]);
        YFrom = Runtime.CurrentRuntime.Height/4;
        Load();
    }

    private TargetHandle[] Menu;
    private TargetHandle[] Description;
    private float ItemSwitchTime = 0;
    private float ItemTriggerTime = 0;
    public bool IsItemTriggered = false;
    public Action? Action = null;
    private int Index = 0;
    private int Columns = 0;
    private int YFrom = 0;

    void Load()
    {
        string[] files = Assets.Files("Assets/Data/Trophy", "*.json");
        Menu = new TargetHandle[files.Length];
        Description = new TargetHandle[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            Menu[i] = Helper.DrawTextScaled($"{i:00}",
                32, 8, 4, 2, 
                Runtime.CurrentRuntime.Fonts["newsreader"],
                "gradient");
        }
        Columns = (int)Math.Sqrt(Menu.Length)+1;
        DisappearingTime = 1f;
    }
    
    public override void Render()
    {
        DrawBackground();
        DrawTitle();
        int row = 0, start = 0;
        Rect rc;
        float time = (float)GetTime();
        for (int i = 0; i < Menu.Length; i++)
        {
            rc = Helper.GetFullSource(Menu[0].Texture);
            if (i % Columns == 0)
            {
                start = (Runtime.CurrentRuntime.Width - Menu[0].Texture.Width * Math.Min(Menu.Length - i, Columns)) / 2;
                row = i / Columns;
            }
            DrawTexturePro(Menu[i].Texture,
                rc,
                rc with {X = start + (i % Columns) * Menu[0].Texture.Width, Y = (YFrom) + (Runtime.CurrentRuntime.Height * (1-Helper.EaseInOutElasticF((float)Helper.ComputeObjectTime(time, TimeAppear + (.02f * i), 1, TimeDisappear + (.02f * i), 1)))) + (row) * Menu[0].Texture.Height },
                Vector2.Zero, 0, Index == i ? Rgba.Yellow : Rgba.White
                );
        }
        base.Render();
    }
    
    public override void TopUpdate()
    {
        
        float time = (float)GetTime();
        if (IsItemTriggered && time - ItemTriggerTime > MenuScreen.MenuActivateCooldown)
        {
            IsItemTriggered = false;
            Action?.Invoke();
            return;
        }
        if (time - ItemSwitchTime < MenuScreen.MenuSwitchCooldown)
            return;
        int indexDif = 0;
        if (IsKeyDown(KeyCode.Down))
            indexDif += Columns;
        if (IsKeyDown(KeyCode.Up))
            indexDif -= Columns;
        if (IsKeyDown(KeyCode.Left))
            indexDif -= 1;
        if (IsKeyDown(KeyCode.Right))
            indexDif += 1;
        if (Math.Abs(indexDif) > 0)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            Index = (Index + indexDif + Menu.Length) % Menu.Length;
            ItemSwitchTime = time;
        }
        if (time - ItemSwitchTime < MenuScreen.MenuActivateCooldown)
            return;
        if (IsKeyDown(KeyCode.Enter) || IsKeyDown(KeyCode.Z))
        {
            IsItemTriggered = true;
            Exiting();
            ItemSwitchTime = time;
            TimeDisappear = (float)GetTime() + DisappearingTime;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
            ItemTriggerTime = time;
            Action = () => Runtime.CurrentRuntime.RemoveScreen(this);
        }
        if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X))
        {
            IsItemTriggered = true;
            Exiting();
            TimeDisappear = (float)GetTime() + DisappearingTime;
            ItemSwitchTime = time;
            Action = () => Runtime.CurrentRuntime.RemoveScreen(this);
            ItemTriggerTime = time + DisappearingTime;
        }
    }
}
