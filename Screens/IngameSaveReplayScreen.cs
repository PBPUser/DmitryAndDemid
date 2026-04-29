using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Gtk;
using Raylib_cs;

namespace DmitryAndDemid.Screens;

public class IngameSaveReplayScreen : Screen
{
    public IngameSaveReplayScreen()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        MenuItems = new RenderTexture2D[20];
        FontSize = (int)(16 * Runtime.CurrentRuntime.ScaleF);
        Spacing = (int)(2 * Runtime.CurrentRuntime.ScaleF);
        X = (int)(32 * Runtime.CurrentRuntime.ScaleF);
        Y = (int)(32 * Runtime.CurrentRuntime.ScaleF);
        var font = Runtime.CurrentRuntime.Fonts["googlesans"];
        LineHeight = (int)(Spacing + Raylib.MeasureTextEx(font, "a", FontSize, Spacing).Y);
    }

    private int FontSize;
    private int Spacing;
    private int Index;
    private int LetterIndex = 0;
    private int LineHeight;
    private int X, Y;
    public RenderTexture2D[] MenuItems;
    private bool InKeyboardMode = false;
    private double LastInputTime = 0;
    private const double InputDelay = 0.25f;
    private string Current = "";
    private string CurrentFormat = "";

    public void DrawPage(int page)
    {
        var font = Runtime.CurrentRuntime.Fonts["googlesans"];
        for (int i = 0; i < 20; i++)
        {
            MenuItems[i] =
                Helper.DrawText(ReadFile(i), FontSize, 0, 0, 
                    Spacing, font, "gradient", 
                    Runtime.CurrentRuntime.ScaleF);
        }
    }

    string ReadFile(int i)
    {
        string final = $"No. {i:00} -------- --/--/-- --:-- %P----- ------- --- ---%";
        string path = $"Replays/aab020_{i:0000}.rpy";
        if (!File.Exists(path))
            return final.Replace("%P", "--");
        var header = Replay.ReadHeader(path);
        return final;
    }

    public override void Render()
    {
        base.Render();
        DrawBackground();
        if (InKeyboardMode)
        {
            Raylib.DrawTexture(MenuItems[Index].Texture, X, Y, Color.Yellow);
            Keyboard.DrawKeyboard((Runtime.CurrentRuntime.Width - Keyboard.LineWidth)/2, Runtime.CurrentRuntime.Height - Keyboard.KeyboardHeight);
            return;
        }
        for (int i = 0; i < 20; i++)
        {
            Raylib.DrawTexture(MenuItems[i].Texture, X,
                Y + (i*LineHeight), i == Index ? Color.Yellow : Color.White);
        }
    }

    
    protected override void Created()
    {
        DrawPage(0);
        base.Created();
    }

    public override void Activated()
    {
        
    }

    public override void Unload()
    {
        foreach (var item in MenuItems)
        {
            Raylib.UnloadRenderTexture(item);
        }
        base.Unload();
    }
    
    public override void TopUpdate()
    {
        if (InKeyboardMode)
        {
            Keyboard.HandleInput();
            return;
        }
        if (Raylib.GetTime() - LastInputTime < InputDelay)
            return;
        if (Raylib.IsKeyDown(KeyboardKey.Z) || Raylib.IsKeyDown(KeyboardKey.Enter))
        {
            var font = Runtime.CurrentRuntime.Fonts["googlesans"];
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
            InKeyboardMode = true;
            LetterIndex = 0;
            Current = "";
            CurrentFormat = $"No. {Index:00} %s --/--/-- --:-- %P----- ------- --- ---%";
            LastInputTime = Raylib.GetTime();
            Keyboard.Reset();
            Keyboard.SetKeyboardCallback(a =>
            {
                if (a == '\n')
                {
                    InKeyboardMode = false;
                    LastInputTime = Raylib.GetTime();
                    return;
                }
                if (a == null)
                {
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
                    InKeyboardMode = false;
                    LastInputTime = Raylib.GetTime();
                    MenuItems[Index] = Helper.DrawText(ReadFile(Index), FontSize, 0, 0, 
                        Spacing, font, "gradient", 
                        Runtime.CurrentRuntime.ScaleF);
                    return;
                }

                if (LetterIndex < 8)
                {
                    Current = $"{Current.Substring(0, LetterIndex)}{a}".PadRight(8, '-');
                    LetterIndex++;
                }
                Raylib.UnloadRenderTexture(MenuItems[Index]);
                MenuItems[Index] = Helper.DrawText(CurrentFormat.Replace("%s", Current), FontSize, 0, 0, 
                    Spacing, font, "gradient", 
                    Runtime.CurrentRuntime.ScaleF);
            });
        }
        else if (Raylib.IsKeyDown(KeyboardKey.Up))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            Index = (Index + 19) % 20;
            LastInputTime = Raylib.GetTime();
        }
        else if (Raylib.IsKeyDown(KeyboardKey.Down))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            Index = (Index + 1) % 20;
            LastInputTime = Raylib.GetTime();
        }
        else if (Raylib.IsKeyDown(KeyboardKey.Escape))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
        base.TopUpdate();
    }
}