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
    public GameplayScreen(ProtogonistData data, Stage stage, int difficulty)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["gameplay_background.png"]);
        game = new Game(data, stage, this, difficulty);
        Source = new Rectangle(0, 0, 384, -448);
        Dest = new Rectangle(32 * Runtime.CurrentRuntime.ScaleF, 16 * Runtime.CurrentRuntime.ScaleF, 384 * Runtime.CurrentRuntime.ScaleF, 448 * Runtime.CurrentRuntime.ScaleF);
        DialogDest = Helper.GetFullscreenSource();
        DialogSource = Helper.GetFullscreenSource();
        DialogSource.Height *= -1;
        DieShader = Runtime.CurrentRuntime.Shaders["die"];
        PauseMenu = new PauseMenu(this);
        SetShaderValue(
            DieShader, 
            GetShaderLocation(DieShader, "scale"),
            Runtime.CurrentRuntime.ScaleF,
            ShaderUniformDataType.Float
            );
        LocationDiePosition = GetShaderLocation(DieShader, "pos");
        LocationDieTime = GetShaderLocation(DieShader, "time");
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

    private Shader DieShader;
    private int LocationDiePosition;
    private int LocationDieTime;
    
    public override void Render()
    {
        float time = (float)GetTime();
        DrawBackground();
        game!.RenderGame();
        if (game.IsDied)
        {
            SetShaderValue(DieShader, LocationDieTime, (time- game.DiedTimestamp) / Game.DieAnimationLength, ShaderUniformDataType.Float);
            SetShaderValue(DieShader, LocationDiePosition, game.DiePosition, ShaderUniformDataType.Vec2);
            BeginShaderMode(Runtime.CurrentRuntime.Shaders["die"]);
        }
        DrawTexturePro(game.Background.Texture,
            Source, Dest,
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(game.Gameplay.Texture,
            Source, Dest,
            Vector2.Zero, 0, Color.White);
        if(game.IsDied)
            EndShaderMode();
        DrawTexturePro(game.Dialog.Texture,
            DialogSource, DialogDest,
            Vector2.Zero, 0, Color.White);
    }

    public override void Unload()
    {
        Runtime.CurrentRuntime.SetScreenRenderingFrom(0);
        game = null;
    }

    public override void Created()
    {
        Runtime.CurrentRuntime.SetScreenRenderingFrom(Runtime.CurrentRuntime.GetScreenIndex(this));
        base.Created();
    }
}
