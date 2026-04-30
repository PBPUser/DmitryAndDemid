using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using Gtk;
using Raylib_cs;

namespace DmitryAndDemid.Screens;

public class IngameSaveReplayScreen : Screen
{
    private PlayerController Controller;
    GameplayScreen GameplayScreen;
    
    public IngameSaveReplayScreen(PlayerController playerController, GameplayScreen screen)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        MenuItems = new RenderTexture2D[20];
        FontSize = (int)(16 * Runtime.CurrentRuntime.ScaleF);
        Spacing = (int)(2 * Runtime.CurrentRuntime.ScaleF);
        X = (int)(32 * Runtime.CurrentRuntime.ScaleF);
        Y = (int)(32 * Runtime.CurrentRuntime.ScaleF);
        var font = Runtime.CurrentRuntime.Fonts["googlesans"];
        LineHeight = (int)(Spacing + Raylib.MeasureTextEx(font, "a", FontSize, Spacing).Y);
        Controller = playerController;
        GameplayScreen = screen;
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
    public bool ExitAfterSave = false;

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
        float state = (float)Helper.ComputeObjectTime((float)Raylib.GetTime(),
                KeyboardModeSwitchInTime, 0.5,
                KeyboardModeSwitchOutTime, 0.5);
        if (InKeyboardMode)
        {
            Raylib.DrawTexture(MenuItems[Index].Texture, X, Y, Color.Yellow);
            
            return;
        }
        for (int i = 0; i < 20; i++)
        {
            Raylib.DrawTexture(MenuItems[i].Texture, X,
                Y + (i*LineHeight), i == Index ? Color.Yellow : Color.White);
        }
        Keyboard.DrawKeyboard((Runtime.CurrentRuntime.Width - Keyboard.LineWidth)/2, (int)(Runtime.CurrentRuntime.Height - (Keyboard.KeyboardHeight*state)));
    }

    
    protected override void Created()
    {
        DrawPage(0);
        base.Created();
    }

    public override void Activated()
    {
        KeyboardModeSwitchInTime = float.MaxValue;
    }

    public override void Unload()
    {
        foreach (var item in MenuItems)
        {
            Raylib.UnloadRenderTexture(item);
        }
        base.Unload();
    }

    private float KeyboardModeSwitchInTime = 0;
    private float KeyboardModeSwitchOutTime = float.MaxValue;
    
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
            KeyboardModeSwitchInTime = (float)Raylib.GetTime();
            KeyboardModeSwitchOutTime = float.MaxValue;
            InKeyboardMode = true;
            LetterIndex = 0;
            Current = "";
            var time = DateTime.Now;
            CurrentFormat = $"No. {Index:00} %s {(time.Year%100):00}/{time.Month:00}/{time.Day:00} {time.Hour:00}:{time.Minute:00} {GameplayScreen.Game.ProtogonistId.PadLeft(7,' ')} {Helper.DifficultyIds[GameplayScreen.Game.Difficulty]} All 0.0%";
            var rJson = new Replay.ReplayJson();
            rJson.Timestamp = time;
            rJson.Person = GameplayScreen.Game.ProtogonistId;
            rJson.Difficulty = GameplayScreen.Game.Difficulty;
            rJson.Stage = "All";
            rJson.Slowdown = "0.0";
            LastInputTime = Raylib.GetTime();
            Keyboard.Reset();
            Keyboard.SetKeyboardCallback(a =>
            {
                if (a == '\n')
                {
                    InKeyboardMode = false;
                    LastInputTime = Raylib.GetTime();
                    KeyboardModeSwitchOutTime = (float)Raylib.GetTime()+0.5f;
                    if (ExitAfterSave)
                    {
                        Runtime.CurrentRuntime.RemoveScreen(this); 
                        Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
                        Runtime.CurrentRuntime.RemoveScreen(GameplayScreen.PauseMenu);
                    }
                    else
                    {
                        Runtime.CurrentRuntime.RemoveScreen(this); 
                    }
                    rJson.Nickname = Current;
                    var r = new Replay(Controller.Movements, rJson);
                    r.Save($"Replays/aab020-{Index:0000}.rpy");
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
                    KeyboardModeSwitchOutTime = (float)Raylib.GetTime()+0.5f;
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