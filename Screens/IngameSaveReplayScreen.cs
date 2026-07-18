using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class IngameSaveReplayScreen : Screen
{
    private PlayerController Controller;
    GameplayScreen GameplayScreen;
    
    public IngameSaveReplayScreen(PlayerController playerController, GameplayScreen screen)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        MenuItems = new TargetHandle[20];
        FontSize = (int)(16 * Runtime.CurrentRuntime.ScaleF);
        Spacing = (int)(2 * Runtime.CurrentRuntime.ScaleF);
        X = (int)(32 * Runtime.CurrentRuntime.ScaleF);
        Y = (int)(32 * Runtime.CurrentRuntime.ScaleF);
        var font = Runtime.CurrentRuntime.Fonts["googlesans"];
        LineHeight = (int)(Spacing + MeasureTextEx(font, "a", FontSize, Spacing).Y);
        Controller = playerController;
        GameplayScreen = screen;
    }

    private int FontSize;
    private int Spacing;
    private int Index;
    private int LetterIndex = 0;
    private int LineHeight;
    private int X, Y;
    public TargetHandle[] MenuItems;
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

    /// <summary>The slot's replay filename. Must match what Save writes below (dash, not underscore).</summary>
    private static string SlotPath(int i) => $"Replays/aab020-{i:0000}.rpy";

    string ReadFile(int i)
    {
        string empty = $"No. {i:00} -------- --/--/-- --:-- ------- --- --- ---";
        string path = SlotPath(i);
        if (!File.Exists(path))
            return empty;

        Replay.ReplayJson? header;
        try { header = Replay.ReadHeader(path); }
        catch { return empty; }
        if (header == null)
            return empty;

        // Actually fill the slot line from the header (it used to be read and then thrown away, so every used
        // slot rendered as the blank template). Format mirrors the save line built in TopUpdate.
        DateTime t = header.Timestamp;
        string difficulty = header.Difficulty >= 0 && header.Difficulty < Helper.DifficultyIds.Length
            ? Helper.DifficultyIds[header.Difficulty]
            : header.Difficulty.ToString();
        string nickname = string.IsNullOrWhiteSpace(header.Nickname) ? "--------" : header.Nickname.PadRight(8, '-');
        return $"No. {i:00} {nickname} {t.Year % 100:00}/{t.Month:00}/{t.Day:00} {t.Hour:00}:{t.Minute:00} {header.Person,7} {difficulty} {header.Stage} {header.Slowdown}%";
    }

    public override void Render()
    {
        base.Render();
        DrawBackground();
        float state = (float)Helper.ComputeObjectTime((float)GetTime(),
                KeyboardModeSwitchInTime, 0.5,
                KeyboardModeSwitchOutTime, 0.5);
        if (InKeyboardMode)
            DrawTexture(MenuItems[Index].Texture, X, Y, Rgba.Yellow);
        else
            for (int i = 0; i < 20; i++)
            {
                DrawTexture(MenuItems[i].Texture, X,
                    Y + (i*LineHeight), i == Index ? Rgba.Yellow : Rgba.White);
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
        // Debounce the Enter/Z that opened this screen from the pause menu: without arming the input cooldown
        // here, that same press is seen on the first frame and immediately re-triggers (re-opening the save flow).
        LastInputTime = GetTime();
    }

    public override void Unload()
    {
        foreach (var item in MenuItems)
        {
            UnloadRenderTexture(item);
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
        if (GetTime() - LastInputTime < InputDelay)
            return;
        if (IsKeyDown(KeyCode.Z) || IsKeyDown(KeyCode.Enter))
        {
            var font = Runtime.CurrentRuntime.Fonts["googlesans"];
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
            KeyboardModeSwitchInTime = (float)GetTime();
            KeyboardModeSwitchOutTime = float.MaxValue;
            InKeyboardMode = true;
            LetterIndex = 0;
            Current = "";
            var time = DateTime.Now;
            CurrentFormat = $"No. {Index:00} %s {time.Year%100:00}/{time.Month:00}/{time.Day:00} {time.Hour:00}:{time.Minute:00} {GameplayScreen.GameBox.ProtogonistId,7} {Helper.DifficultyIds[GameplayScreen.GameBox.Difficulty]} All 0.0%";
            var rJson = new Replay.ReplayJson();
            rJson.Timestamp = time;
            rJson.Person = GameplayScreen.GameBox.ProtogonistId;
            rJson.Difficulty = GameplayScreen.GameBox.Difficulty;
            rJson.Stage = "All";
            rJson.Slowdown = "0.0";
            LastInputTime = GetTime();
            Keyboard.Reset();
            Keyboard.SetKeyboardCallback(a =>
            {
                if (a == '\n')
                {
                    InKeyboardMode = false;
                    LastInputTime = GetTime();
                    KeyboardModeSwitchOutTime = (float)GetTime()+0.5f;
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
                    // Record which stages this run covers (and each stage's inputs offset + resource snapshot) so
                    // the viewer can list the levels and start playback from any of them.
                    rJson.ReplayStageInfo = Controller.RecordedStages.ToArray();
                    // Only the used portion of the buffer is meaningful; the rest is trailing zeros.
                    byte[] moves = Controller.WrittenLength < Controller.Movements.Length
                        ? Controller.Movements[..Controller.WrittenLength]
                        : Controller.Movements;
                    var r = new Replay(moves, rJson);
                    r.Save(SlotPath(Index));
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["extend"]);   // a "saved!" cue instead of a click
                    return;
                }
                if (a == null)
                {
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
                    InKeyboardMode = false;
                    LastInputTime = GetTime();
                    MenuItems[Index] = Helper.DrawText(ReadFile(Index), FontSize, 0, 0, 
                        Spacing, font, "gradient", 
                        Runtime.CurrentRuntime.ScaleF);
                    KeyboardModeSwitchOutTime = (float)GetTime()+0.5f;
                    return;
                }

                if (LetterIndex < 8)
                {
                    Current = $"{Current.Substring(0, LetterIndex)}{a}".PadRight(8, '-');
                    LetterIndex++;
                }
                UnloadRenderTexture(MenuItems[Index]);
                MenuItems[Index] = Helper.DrawText(CurrentFormat.Replace("%s", Current), FontSize, 0, 0, 
                    Spacing, font, "gradient", 
                    Runtime.CurrentRuntime.ScaleF);
            });
        }
        else if (IsKeyDown(KeyCode.Up))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            Index = (Index + 19) % 20;
            LastInputTime = GetTime();
        }
        else if (IsKeyDown(KeyCode.Down))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            Index = (Index + 1) % 20;
            LastInputTime = GetTime();
        }
        else if (IsKeyDown(KeyCode.Escape))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
        base.TopUpdate();
    }
}