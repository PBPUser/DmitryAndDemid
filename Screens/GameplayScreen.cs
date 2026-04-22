using DmitryAndDemid.Common;
using static Raylib_cs.Raylib;
using DmitryAndDemid;
using DmitryAndDemid.Data;
using Raylib_cs;
using System.Numerics;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class GameplayScreen : Screen
{
    public GameplayScreen(ProtogonistData data, Stage stage)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["practice_background.png"]);
        game = new Game(data, stage, this);
        Source = new Rectangle(0, 0, 384, -448);
        Dest = new Rectangle((float)(32 * Runtime.CurrentRuntime.Scale), (float)(16 * Runtime.CurrentRuntime.Scale), (float)(384 * Runtime.CurrentRuntime.Scale), (float)(448 * Runtime.CurrentRuntime.Scale));
        DialogDest = Helper.GetFullscreenSource();
        DialogSource = Helper.GetFullscreenSource();
        DialogSource.Height *= -1;

        PauseMenu = new PauseMenu(this);
    }

    public PauseMenu PauseMenu;

    Rectangle Source;
    Rectangle Dest;
    Rectangle DialogSource;
    Rectangle DialogDest;

    Game? game;

    public void Resume()
    {
        game!.TogglePause(false);
    }
    
    public override void PreRender(double f)
    {
        if (IsKeyDown(KeyboardKey.Escape))
        {
            MenuScreen.PreviousKeyTimestamp = GetTime();
            game!.TogglePause(true);
            Runtime.CurrentRuntime.AddScreen(PauseMenu);
        }
        game!.Update();
        game!.ProcessInput();
    }

    public override void Render()
    {
        DrawBackground();
        game!.RenderGame();
        DrawTexturePro(game.Gameplay.Texture,
            Source, Dest,
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(game.Dialog.Texture,
            DialogSource, DialogDest,
            Vector2.Zero, 0, Color.White);
    }

    public override void Unload()
    {
        game = null;
    }
}
